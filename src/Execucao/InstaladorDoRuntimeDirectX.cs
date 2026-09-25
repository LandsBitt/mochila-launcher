using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Mochila.Dados;
using Mochila.Scanner;

namespace Mochila.Execucao
{
    /// <summary>Em que pé está a preparação do runtime. Percentual null = sem como medir.</summary>
    public sealed class ProgressoDoRuntime
    {
        public ProgressoDoRuntime(string etapa, int? percentual)
        {
            Etapa = etapa;
            Percentual = percentual;
        }

        public string Etapa { get; }

        public int? Percentual { get; }
    }

    /// <summary>Preparação do runtime que não chegou ao fim, com o motivo em português.</summary>
    public sealed class FalhaNoRuntimeException : Exception
    {
        public FalhaNoRuntimeException(string mensagem, Exception? causa = null) : base(mensagem, causa) { }
    }

    /// <summary>
    /// Monta <c>_mochila\runtime\directx</c> a partir do pacote oficial da Microsoft, baixado
    /// na hora, de <c>download.microsoft.com</c>.
    ///
    /// <b>Por que baixar em vez de trazer as DLLs junto:</b> a licença do DirectX SDK só deixa
    /// redistribuir essas DLLs dentro do instalador oficial, e o Mochila é GPL. Quem
    /// distribui é a Microsoft; o Mochila só abre o pacote no HD de quem baixou.
    ///
    /// <b>Nada aqui pede admin.</b> O pacote é um autoextrator (<c>asInvoker</c>) e
    /// <c>/Q /T:pasta</c> só descompacta os CABs, sem rodar o DXSETUP. Os CABs são abertos
    /// pelo <c>expand.exe</c> do próprio Windows.
    ///
    /// O resultado é trocado de uma vez só: tudo é montado em <c>runtime\_preparando</c> e
    /// só vira <c>runtime\directx</c> no fim, com o <see cref="RuntimeDirectX.ArquivoDeConclusao"/>
    /// dentro. HD arrancado no meio deixa o runtime antigo intacto, ou nenhum, mas nunca
    /// um pela metade.
    /// </summary>
    public static class InstaladorDoRuntimeDirectX
    {
        /// <summary>DirectX End-User Runtimes (June 2010), a última versão que existe.</summary>
        public const string UrlDoPacote =
            "https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe";

        public const string NomeDoPacote = "directx_Jun2010_redist.exe";

        /// <summary>
        /// SHA-256 do pacote de 2010. Se a Microsoft um dia reassinar o arquivo, o hash muda
        /// e a assinatura digital passa a ser a prova (ver <see cref="PacoteConfiavel"/>).
        /// </summary>
        public const string Sha256DoPacote = "053f76dcbb28802e23341b6a787e3b0791c0fa5c8d4d011b1044172dbf89c73b";

        /// <summary>Pacote (~96 MB) + CABs extraídos (~96 MB) + DLLs (~225 MB), com folga.</summary>
        public const long EspacoNecessario = 600L * 1024 * 1024;

        private static readonly TimeSpan LimiteDoProcesso = TimeSpan.FromMinutes(5);

        private static string PastaDePreparo => Path.Combine(Caminhos.PastaRuntime, "_preparando");

        private static string PastaAntiga => Path.Combine(Caminhos.PastaRuntime, "_antigo");

        /// <summary>
        /// Quem não tem internet no PC pode deixar o pacote aqui, baixado em outro lugar, e
        /// o Mochila usa ele em vez de baixar.
        /// </summary>
        public static string PacoteDeixadoAMao => Path.Combine(Caminhos.PastaRuntime, NomeDoPacote);

        /// <summary>
        /// Baixa, confere, extrai e instala. Lança <see cref="FalhaNoRuntimeException"/> com
        /// uma frase para o usuário, ou <see cref="OperationCanceledException"/>.
        /// </summary>
        public static void Instalar(IProgress<ProgressoDoRuntime>? progresso, CancellationToken token,
                                    HttpMessageHandler? manipulador = null)
        {
            Avisar(progresso, "Conferindo o espaço no HD...", null);
            ConferirEspaco();

            ApagarSemErro(PastaDePreparo);

            try
            {
                Directory.CreateDirectory(PastaDePreparo);

                var pacote = File.Exists(PacoteDeixadoAMao)
                    ? PacoteDeixadoAMao
                    : Baixar(Path.Combine(PastaDePreparo, NomeDoPacote), progresso, token, manipulador);

                token.ThrowIfCancellationRequested();

                Avisar(progresso, "Conferindo a assinatura da Microsoft...", null);
                if (!PacoteConfiavel(pacote))
                {
                    throw new FalhaNoRuntimeException(
                        "O arquivo baixado não é o pacote oficial da Microsoft (assinatura inválida). " +
                        "Nada foi instalado.");
                }

                var extraido = Path.Combine(PastaDePreparo, "pacote");
                Directory.CreateDirectory(extraido);

                Avisar(progresso, "Abrindo o pacote...", null);
                var saida = Rodar(pacote, $"/Q /T:\"{extraido}\"", token);
                if (saida != 0)
                    throw new FalhaNoRuntimeException($"O pacote da Microsoft não abriu (código {saida}).");

                var destino = Path.Combine(PastaDePreparo, "directx");
                ExtrairDlls(extraido, destino, progresso, token);
                ConferirResultado(destino);

                File.WriteAllText(Path.Combine(destino, "LEIA-ME.txt"), TextoDoLeiaMe(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(destino, RuntimeDirectX.ArquivoDeConclusao),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine, Encoding.UTF8);

                Avisar(progresso, "Instalando no HD...", null);
                Trocar(destino);
            }
            finally
            {
                ApagarSemErro(PastaDePreparo);
            }

            Avisar(progresso, "Pronto.", 100);
        }

        /// <summary>Apaga o runtime do HD. As chaves de registro de uma sessão aberta saem junto.</summary>
        public static void Remover()
        {
            RuntimeDirectX.DesfazerRegistro();

            if (!Directory.Exists(Caminhos.PastaRuntimeDirectX)) return;

            // A marca sai primeiro: se o resto emperrar (DLL presa por um jogo aberto), o
            // que sobrar já não conta como runtime pronto.
            var marca = Path.Combine(Caminhos.PastaRuntimeDirectX, RuntimeDirectX.ArquivoDeConclusao);
            if (File.Exists(marca)) File.Delete(marca);

            Directory.Delete(Caminhos.PastaRuntimeDirectX, recursive: true);
        }

        // ---- Download ----------------------------------------------------------------------

        private static string Baixar(string destino, IProgress<ProgressoDoRuntime>? progresso,
                                     CancellationToken token, HttpMessageHandler? manipulador)
        {
            Avisar(progresso, "Conectando a download.microsoft.com...", null);

            using (var http = manipulador is null ? new HttpClient() : new HttpClient(manipulador, disposeHandler: false))
            {
                http.Timeout = TimeSpan.FromMinutes(60);

                HttpResponseMessage resposta;
                try
                {
                    resposta = http.GetAsync(UrlDoPacote, HttpCompletionOption.ResponseHeadersRead, token)
                                   .GetAwaiter().GetResult();
                }
                catch (HttpRequestException erro)
                {
                    throw new FalhaNoRuntimeException(
                        "Não consegui falar com o site da Microsoft. O PC está conectado à internet?", erro);
                }

                using (resposta)
                {
                    if (!resposta.IsSuccessStatusCode)
                    {
                        throw new FalhaNoRuntimeException(
                            $"O site da Microsoft respondeu {(int)resposta.StatusCode} ({resposta.ReasonPhrase}). " +
                            $"Se o link saiu do ar, baixe \"{NomeDoPacote}\" em outro lugar e coloque em " +
                            $"\"{PacoteDeixadoAMao}\".");
                    }

                    var total = resposta.Content.Headers.ContentLength ?? 0;

                    using (var entrada = resposta.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var saida = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                    {
                        var buffer = new byte[81920];
                        long lidos = 0;
                        long ultimoMarco = -1;

                        int n;
                        while ((n = entrada.ReadAsync(buffer, 0, buffer.Length, token).GetAwaiter().GetResult()) > 0)
                        {
                            saida.Write(buffer, 0, n);
                            lidos += n;

                            // Um recado por ponto percentual (ou por MB, sem tamanho conhecido):
                            // um por bloco de 80 KB seriam mil repinturas da janela.
                            var percentual = total > 0 ? (int)(lidos * 100 / total) : (int?)null;
                            var marco = percentual ?? lidos / 1048576;
                            if (marco != ultimoMarco)
                            {
                                ultimoMarco = marco;
                                Avisar(progresso,
                                    total > 0
                                        ? $"Baixando da Microsoft: {lidos / 1048576} de {total / 1048576} MB"
                                        : $"Baixando da Microsoft: {lidos / 1048576} MB",
                                    percentual);
                            }
                        }

                        if (total > 0 && lidos != total)
                            throw new FalhaNoRuntimeException("O download foi interrompido no meio. Tente de novo.");
                    }
                }
            }

            return destino;
        }

        // ---- Verificação -------------------------------------------------------------------

        /// <summary>
        /// O pacote é o da Microsoft? Vale o hash conhecido, ou uma assinatura Authenticode
        /// válida emitida para a Microsoft Corporation.
        /// </summary>
        public static bool PacoteConfiavel(string caminho)
        {
            try
            {
                if (string.Equals(Sha256(caminho), Sha256DoPacote, StringComparison.OrdinalIgnoreCase)) return true;
                return AssinadoPelaMicrosoft(caminho);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string Sha256(string caminho)
        {
            using (var sha = SHA256.Create())
            using (var fluxo = File.OpenRead(caminho))
                return string.Concat(sha.ComputeHash(fluxo).Select(b => b.ToString("x2")));
        }

        /// <summary>Assinatura válida (WinVerifyTrust) E emitida para a Microsoft.</summary>
        public static bool AssinadoPelaMicrosoft(string caminho)
        {
            try
            {
                if (!AssinaturaValida(caminho)) return false;

                var certificado = X509Certificate.CreateFromSignedFile(caminho);
                return certificado.Subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- Extração ----------------------------------------------------------------------

        /// <summary>
        /// Para qual pasta vai o conteúdo deste CAB, ou Desconhecida para pular. Fica de fora
        /// o Managed DirectX (MDX, assemblies .NET que só funcionam instalados no GAC) e os
        /// CABs do próprio instalador.
        /// </summary>
        public static ArquiteturaPe ArquiteturaDoCab(string nome)
        {
            if (nome.IndexOf("MDX", StringComparison.OrdinalIgnoreCase) >= 0) return ArquiteturaPe.Desconhecida;
            if (nome.StartsWith("dx", StringComparison.OrdinalIgnoreCase)) return ArquiteturaPe.Desconhecida;

            if (nome.EndsWith("_x86.cab", StringComparison.OrdinalIgnoreCase)) return ArquiteturaPe.X86;
            if (nome.EndsWith("_x64.cab", StringComparison.OrdinalIgnoreCase)) return ArquiteturaPe.X64;
            return ArquiteturaPe.Desconhecida;
        }

        private static void ExtrairDlls(string extraido, string destino, IProgress<ProgressoDoRuntime>? progresso,
                                        CancellationToken token)
        {
            var cabs = Directory.GetFiles(extraido, "*.cab")
                .Select(cab => (Cab: cab, Arquitetura: ArquiteturaDoCab(Path.GetFileName(cab))))
                .Where(item => item.Arquitetura != ArquiteturaPe.Desconhecida)
                .ToList();

            if (cabs.Count == 0)
                throw new FalhaNoRuntimeException("O pacote abriu, mas veio sem as bibliotecas dentro.");

            var expand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe");
            var feitos = 0;

            foreach (var (cab, arquitetura) in cabs)
            {
                token.ThrowIfCancellationRequested();

                var pasta = Path.Combine(destino, arquitetura == ArquiteturaPe.X86 ? "x86" : "x64");
                Directory.CreateDirectory(pasta);

                // Só *.dll: os .inf e .cat do CAB são do instalador e não servem para nada aqui.
                var saida = Rodar(expand, $"\"{cab}\" -F:*.dll \"{pasta}\"", token);
                if (saida != 0)
                    throw new FalhaNoRuntimeException($"O Windows não conseguiu abrir {Path.GetFileName(cab)} (código {saida}).");

                feitos++;
                Avisar(progresso, $"Extraindo as bibliotecas: {feitos} de {cabs.Count}", feitos * 100 / cabs.Count);
            }
        }

        /// <summary>
        /// Confere o resultado antes de ele virar o runtime: as DLLs que os jogos mais pedem
        /// estão lá, e cada pasta tem DLL da arquitetura dela. Uma pasta x86 com DLL de 64
        /// bits faria o jogo falhar de um jeito bem pior que "DLL não encontrada".
        /// </summary>
        private static void ConferirResultado(string destino)
        {
            foreach (var (subpasta, arquitetura) in new[] { ("x86", ArquiteturaPe.X86), ("x64", ArquiteturaPe.X64) })
            {
                foreach (var essencial in new[] { "d3dx9_43.dll", "xinput1_3.dll", "XAudio2_7.dll" })
                {
                    var caminho = Path.Combine(destino, subpasta, essencial);
                    if (!File.Exists(caminho))
                        throw new FalhaNoRuntimeException($"A extração terminou sem {subpasta}\\{essencial}.");

                    var lido = LeitorPe.Ler(caminho)?.Arquitetura;
                    if (lido != arquitetura)
                        throw new FalhaNoRuntimeException($"{subpasta}\\{essencial} veio com a arquitetura errada ({lido}).");
                }
            }
        }

        /// <summary>Troca o runtime antigo pelo novo com dois Directory.Move, desfazendo se o segundo falhar.</summary>
        private static void Trocar(string novo)
        {
            ApagarSemErro(PastaAntiga);

            var atual = Caminhos.PastaRuntimeDirectX;
            var haviaAntigo = Directory.Exists(atual);

            if (haviaAntigo) Directory.Move(atual, PastaAntiga);

            try
            {
                Directory.Move(novo, atual);
            }
            catch (Exception erro)
            {
                if (haviaAntigo && !Directory.Exists(atual)) Directory.Move(PastaAntiga, atual);
                throw new FalhaNoRuntimeException($"Não consegui pôr o runtime no lugar: {erro.Message}", erro);
            }

            ApagarSemErro(PastaAntiga);
        }

        private static void ConferirEspaco()
        {
            try
            {
                Directory.CreateDirectory(Caminhos.PastaRuntime);
                var raiz = Path.GetPathRoot(Path.GetFullPath(Caminhos.PastaRuntime));
                if (string.IsNullOrEmpty(raiz)) return;

                var livre = new DriveInfo(raiz!).AvailableFreeSpace;
                if (livre < EspacoNecessario)
                {
                    throw new FalhaNoRuntimeException(
                        $"O HD tem {livre / 1048576} MB livres, e a preparação precisa de uns " +
                        $"{EspacoNecessario / 1048576} MB (no fim ficam uns 225 MB).");
                }
            }
            catch (FalhaNoRuntimeException)
            {
                throw;
            }
            catch (Exception)
            {
                // Unidade que não informa espaço (rede, mapeamento estranho): segue e deixa
                // o erro de disco cheio, se vier, aparecer na hora.
            }
        }

        // ---- Utilitários -------------------------------------------------------------------

        private static int Rodar(string programa, string argumentos, CancellationToken token)
        {
            using (var processo = Process.Start(new ProcessStartInfo(programa, argumentos)
                   {
                       UseShellExecute = false,
                       CreateNoWindow = true,
                       WindowStyle = ProcessWindowStyle.Hidden
                   }))
            {
                if (processo is null) throw new FalhaNoRuntimeException($"Não consegui rodar {Path.GetFileName(programa)}.");

                var limite = DateTime.UtcNow + LimiteDoProcesso;
                while (!processo.WaitForExit(200))
                {
                    if (token.IsCancellationRequested || DateTime.UtcNow > limite)
                    {
                        try { processo.Kill(); } catch (Exception) { }
                        token.ThrowIfCancellationRequested();
                        throw new FalhaNoRuntimeException($"{Path.GetFileName(programa)} passou de {LimiteDoProcesso.TotalMinutes:F0} minutos e foi interrompido.");
                    }
                }

                return processo.ExitCode;
            }
        }

        private static void Avisar(IProgress<ProgressoDoRuntime>? progresso, string etapa, int? percentual)
            => progresso?.Report(new ProgressoDoRuntime(etapa, percentual));

        private static void ApagarSemErro(string pasta)
        {
            try
            {
                if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
            }
            catch (Exception)
            {
                // Arquivo preso por antivírus: a próxima preparação tenta de novo.
            }
        }

        private static string TextoDoLeiaMe() => string.Join(Environment.NewLine, new[]
        {
            "Runtime DirectX portatil do Mochila",
            "===================================",
            "",
            "Estas DLLs sao do pacote oficial \"DirectX End-User Runtimes (June 2010)\" da",
            "Microsoft, baixado de download.microsoft.com por este HD. O Mochila so abriu o",
            "pacote: nenhum arquivo foi alterado.",
            "",
            "  x86\\  bibliotecas de 32 bits (a maioria dos jogos antigos)",
            "  x64\\  bibliotecas de 64 bits",
            "",
            "Quando um jogo abre pelo Mochila, a pasta da arquitetura dele entra no PATH do",
            "jogo. Se o PC ja tiver o DirectX instalado, o Windows usa o do PC.",
            "",
            "A licenca da Microsoft nao permite redistribuir estas DLLs soltas. Nao copie esta",
            "pasta para outras pessoas: cada Mochila baixa o pacote por conta propria.",
            "",
            "Para remover: Configuracoes do Mochila > Runtime DirectX > Remover, ou apagar esta pasta."
        });

        // ---- WinVerifyTrust ----------------------------------------------------------------

        private static readonly Guid AcaoVerificarGenerica = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private static bool AssinaturaValida(string caminho)
        {
            var arquivo = new WintrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WintrustFileInfo)),
                pcwszFilePath = caminho
            };

            var ponteiroDoArquivo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WintrustFileInfo)));
            try
            {
                Marshal.StructureToPtr(arquivo, ponteiroDoArquivo, false);

                var dados = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WintrustData)),
                    dwUIChoice = 2,                 // WTD_UI_NONE
                    fdwRevocationChecks = 0,        // WTD_REVOKE_NONE: sem rede, certificado de 2010
                    dwUnionChoice = 1,              // WTD_CHOICE_FILE
                    pFile = ponteiroDoArquivo,
                    dwStateAction = 0,
                    dwProvFlags = 0x00001000        // WTD_CACHE_ONLY_URL_RETRIEVAL
                };

                return WinVerifyTrust(IntPtr.Zero, AcaoVerificarGenerica, ref dados) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(ponteiroDoArquivo, typeof(WintrustFileInfo));
                Marshal.FreeHGlobal(ponteiroDoArquivo);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid acao,
                                                 ref WintrustData dados);
    }
}
