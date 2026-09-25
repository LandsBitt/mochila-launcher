// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using Mochila.Dados;
using Mochila.Execucao;
using Mochila.Scanner;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação do runtime DirectX portátil: arquitetura do exe, escolha da pasta, PATH
    /// herdado pelo jogo e o registro COM no HKCU.
    ///
    /// O download de verdade NÃO roda aqui (são 96 MB): ele tem o modo próprio
    /// <c>--preparar-runtime-dx &lt;pasta&gt;</c>. O registro, sim, mexe no HKCU de verdade,
    /// só com CLSIDs inventados na hora, e apaga tudo no fim.
    /// </summary>
    public static class AutoTesteRuntimeDirectX
    {
        private const string NomePastaSandbox = "_autoteste-runtime-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;
            var sandbox = Path.Combine(raizReal, NomePastaSandbox);

            try
            {
                ApagarSandbox(sandbox);
                Directory.CreateDirectory(sandbox);
                Caminhos.DefinirPastaBase(sandbox);

                TestarArquitetura(v);
                TestarTabelaDeClasses(v);
                TestarClassificacaoDosCabs(v);
                TestarEstadoDoRuntime(v);
                TestarPathHerdado(v, sandbox);
                TestarRegistroCom(v, sandbox);
                TestarVerificacaoDoPacote(v, sandbox);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
                ApagarSandbox(sandbox);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- LeitorPe ----------------------------------------------------------------------

        private static void TestarArquitetura(Verificador v)
        {
            v.Escrever("Arquitetura do executável");

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var notepad32 = Path.Combine(windows, "SysWOW64", "notepad.exe");
            if (File.Exists(notepad32))
            {
                v.Verificar("notepad do SysWOW64 é x86",
                    LeitorPe.Ler(notepad32)?.Arquitetura == ArquiteturaPe.X86,
                    LeitorPe.Ler(notepad32)?.Arquitetura.ToString());
            }

            var nativo = Path.Combine(windows, Environment.Is64BitProcess ? "System32" : "Sysnative", "notepad.exe");
            if (Environment.Is64BitOperatingSystem && File.Exists(nativo))
            {
                var lido = LeitorPe.Ler(nativo)?.Arquitetura;
                v.Verificar("notepad do System32 é x64 (ou ARM64 num Windows ARM)",
                    lido == ArquiteturaPe.X64 || lido == ArquiteturaPe.Arm64, lido?.ToString());
            }

            // Arquivo que não é PE (o card de um .bat, um .lnk): sem arquitetura, sem runtime.
            var sandboxTexto = Path.Combine(Caminhos.PastaBase, "nao-e-exe.bat");
            File.WriteAllText(sandboxTexto, "@echo off");
            v.Verificar("arquivo que não é PE não tem arquitetura", LeitorPe.Ler(sandboxTexto) is null);
        }

        private static void TestarTabelaDeClasses(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Tabela de classes COM");

            var classes = RegistroComDoDirectX.ClassesDoPacote;
            // 11 XACT 2.x + 8 XACT 3.x + 8 XAudio2 x 3 classes = 43, a mesma conta do HKLM
            // de um PC com o redist de junho de 2010 instalado.
            v.Verificar("43 classes (XACT 2.0–3.7 e XAudio2 2.0–2.7 com os efeitos)", classes.Count == 43,
                classes.Count.ToString());
            v.Verificar("todo CLSID é um GUID com chaves",
                classes.All(c => c.Clsid.StartsWith("{") && Guid.TryParse(c.Clsid, out _)));
            v.Verificar("nenhum CLSID repetido",
                classes.Select(c => c.Clsid.ToLowerInvariant()).Distinct().Count() == classes.Count);
            v.Verificar("toda DLL da tabela é xactengine ou XAudio2",
                classes.All(c => c.Dll.StartsWith("xactengine", StringComparison.OrdinalIgnoreCase) ||
                                 c.Dll.StartsWith("XAudio2_", StringComparison.OrdinalIgnoreCase)));
        }

        private static void TestarClassificacaoDosCabs(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Escolha dos CABs do pacote");

            v.Verificar("d3dx9_35 x86 vai para x86",
                InstaladorDoRuntimeDirectX.ArquiteturaDoCab("AUG2007_d3dx9_35_x86.cab") == ArquiteturaPe.X86);
            v.Verificar("XAudio x64 vai para x64",
                InstaladorDoRuntimeDirectX.ArquiteturaDoCab("Jun2010_XAudio_x64.cab") == ArquiteturaPe.X64);
            v.Verificar("Managed DirectX fica de fora",
                InstaladorDoRuntimeDirectX.ArquiteturaDoCab("Apr2006_MDX1_x86.cab") == ArquiteturaPe.Desconhecida);
            v.Verificar("CAB do instalador (dxdllreg) fica de fora",
                InstaladorDoRuntimeDirectX.ArquiteturaDoCab("dxdllreg_x86.cab") == ArquiteturaPe.Desconhecida);
            v.Verificar("dxupdate.cab fica de fora",
                InstaladorDoRuntimeDirectX.ArquiteturaDoCab("dxupdate.cab") == ArquiteturaPe.Desconhecida);
        }

        // ---- Estado e escolha da pasta -----------------------------------------------------

        private static void TestarEstadoDoRuntime(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Runtime baixado ou não");

            v.Verificar(@"o runtime mora em _mochila\runtime\directx",
                Caminhos.PastaRuntimeDirectX.EndsWith(@"_mochila\runtime\directx", StringComparison.OrdinalIgnoreCase));

            v.Verificar("sem nada baixado, não está pronto", !RuntimeDirectX.EstaPronto);

            var exe32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "notepad.exe");
            v.Verificar("sem runtime, o lançamento não ganha pasta nenhuma",
                RuntimeDirectX.Preparar(exe32).Pasta is null);

            // Pasta montada mas sem o arquivo de conclusão: extração interrompida.
            Directory.CreateDirectory(RuntimeDirectX.PastaX86);
            Directory.CreateDirectory(RuntimeDirectX.PastaX64);
            v.Verificar("pasta sem o pronto.txt não conta (instalação pela metade)", !RuntimeDirectX.EstaPronto);

            File.WriteAllText(Path.Combine(Caminhos.PastaRuntimeDirectX, RuntimeDirectX.ArquivoDeConclusao), "teste");
            v.Verificar("com o pronto.txt, está pronto", RuntimeDirectX.EstaPronto);

            v.VerificarTexto("exe de 32 bits usa a pasta x86", RuntimeDirectX.PastaX86,
                RuntimeDirectX.PastaDaArquitetura(ArquiteturaPe.X86));
            v.VerificarTexto("exe de 64 bits usa a pasta x64", RuntimeDirectX.PastaX64,
                RuntimeDirectX.PastaDaArquitetura(ArquiteturaPe.X64));
            v.Verificar("ARM64 não ganha pasta", RuntimeDirectX.PastaDaArquitetura(ArquiteturaPe.Arm64) is null);

            if (File.Exists(exe32))
            {
                var preparo = RuntimeDirectX.Preparar(exe32);
                v.VerificarTexto("Preparar num exe x86 de verdade escolhe a x86", RuntimeDirectX.PastaX86, preparo.Pasta);
                v.Verificar("pasta sem DLL não registra classe nenhuma", preparo.ClassesRegistradas == 0,
                    preparo.ClassesRegistradas.ToString());
            }
        }

        // ---- PATH --------------------------------------------------------------------------

        private static void TestarPathHerdado(Verificador v, string sandbox)
        {
            v.Escrever("");
            v.Escrever("PATH herdado pelo jogo");

            var original = Environment.GetEnvironmentVariable("PATH");
            var preparo = new PreparoDoRuntime(RuntimeDirectX.PastaX86, 0, null);
            var saidaDoFilho = Path.Combine(sandbox, "path-do-filho.txt");

            using (preparo.AplicarNoAmbiente())
            {
                var durante = Environment.GetEnvironmentVariable("PATH") ?? "";
                v.Verificar("durante o lançamento, a pasta vem NA FRENTE do PATH",
                    durante.StartsWith(RuntimeDirectX.PastaX86 + ";", StringComparison.OrdinalIgnoreCase));
                v.Verificar("o PATH antigo continua inteiro depois dela",
                    durante.EndsWith(original ?? "", StringComparison.Ordinal));

                // O jogo sobe por ShellExecute, igual ao LancadorDeJogos. É isso que precisa
                // provar que herda: ProcessStartInfo.Environment não vale com ShellExecute.
                using (var filho = Process.Start(new ProcessStartInfo
                       {
                           FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                           Arguments = $"/c echo %PATH%> \"{saidaDoFilho}\"",
                           UseShellExecute = true,
                           WindowStyle = ProcessWindowStyle.Hidden
                       }))
                {
                    filho?.WaitForExit(15000);
                }
            }

            v.Verificar("depois do Process.Start, o PATH do Mochila volta a ser o de antes",
                Environment.GetEnvironmentVariable("PATH") == original);

            var herdado = File.Exists(saidaDoFilho) ? File.ReadAllText(saidaDoFilho).Trim() : "";
            v.Verificar("processo aberto por ShellExecute herda a pasta no PATH",
                herdado.StartsWith(RuntimeDirectX.PastaX86 + ";", StringComparison.OrdinalIgnoreCase),
                herdado.Length > 80 ? herdado.Substring(0, 80) + "..." : herdado);

            using (PreparoDoRuntime.Nenhum.AplicarNoAmbiente())
            {
                v.Verificar("sem runtime, o PATH nem é tocado",
                    Environment.GetEnvironmentVariable("PATH") == original);
            }
        }

        // ---- Registro COM ------------------------------------------------------------------

        private static void TestarRegistroCom(Verificador v, string sandbox)
        {
            v.Escrever("");
            v.Escrever("Registro COM no HKCU (CLSIDs inventados para o teste)");

            var pastaDll = Path.Combine(sandbox, "dlls");
            Directory.CreateDirectory(pastaDll);
            File.WriteAllBytes(Path.Combine(pastaDll, "mochila_teste.dll"), new byte[] { 0x4D, 0x5A });

            var nossa = new ClasseComDoDirectX("{" + Guid.NewGuid() + "}", "mochila_teste.dll", "Teste do Mochila");
            var semDll = new ClasseComDoDirectX("{" + Guid.NewGuid() + "}", "nao_existe.dll", "Teste do Mochila");
            var alheia = new ClasseComDoDirectX("{" + Guid.NewGuid() + "}", "mochila_teste.dll", "Teste do Mochila");
            var classes = new[] { nossa, semDll, alheia };

            // Uma chave "de outro programa" no mesmo lugar: sem a marca, não pode ser tocada.
            using (var hkcu32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (var chave = hkcu32.CreateSubKey(@"Software\Classes\CLSID\" + alheia.Clsid))
            {
                chave!.SetValue("", "de outro programa");
            }

            try
            {
                var gravadas = RegistroComDoDirectX.Registrar(ArquiteturaPe.X86, pastaDll, classes, (_, _) => false);

                v.Verificar("grava só a classe com DLL e sem dono", gravadas == 1, gravadas.ToString());

                using (var hkcu32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
                using (var servidor = hkcu32.OpenSubKey(@"Software\Classes\CLSID\" + nossa.Clsid + @"\InprocServer32"))
                using (var chave = hkcu32.OpenSubKey(@"Software\Classes\CLSID\" + nossa.Clsid))
                {
                    v.VerificarTexto("InprocServer32 aponta para a DLL do HD (visão de 32 bits)",
                        Path.Combine(pastaDll, "mochila_teste.dll"), servidor?.GetValue("") as string);
                    v.VerificarTexto("ThreadingModel igual ao do instalador oficial", "Both",
                        servidor?.GetValue("ThreadingModel") as string);
                    v.Verificar("a chave leva a marca do Mochila",
                        chave?.GetValue(RegistroComDoDirectX.ValorDaMarca) != null);
                }

                if (Environment.Is64BitOperatingSystem)
                {
                    using (var hkcu64 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                    using (var chave = hkcu64.OpenSubKey(@"Software\Classes\CLSID\" + nossa.Clsid))
                    {
                        v.Verificar("jogo de 32 bits não suja a visão de 64 bits", chave is null);
                    }
                }

                using (var hkcu32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
                using (var chave = hkcu32.OpenSubKey(@"Software\Classes\CLSID\" + alheia.Clsid))
                {
                    v.VerificarTexto("chave de outro programa continua como estava", "de outro programa",
                        chave?.GetValue("") as string);
                }

                var comSistema = RegistroComDoDirectX.Registrar(ArquiteturaPe.X64, pastaDll, classes, (_, _) => true);
                v.Verificar("PC com o DirectX instalado não ganha chave nenhuma", comSistema == 0, comSistema.ToString());

                var apagadas = RegistroComDoDirectX.Remover(classes);
                v.Verificar("Remover apaga só a chave marcada", apagadas == 1, apagadas.ToString());

                using (var hkcu32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
                {
                    using (var chave = hkcu32.OpenSubKey(@"Software\Classes\CLSID\" + nossa.Clsid))
                        v.Verificar("a chave do Mochila sumiu", chave is null);

                    using (var chave = hkcu32.OpenSubKey(@"Software\Classes\CLSID\" + alheia.Clsid))
                        v.Verificar("a chave alheia sobreviveu ao Remover", chave != null);
                }

                v.Verificar("Remover sem nada para apagar não quebra", RegistroComDoDirectX.Remover(classes) == 0);

                v.Verificar("este PC tem o XAudio2 2.7 no HKLM (o runtime não registraria por cima)",
                    RegistroComDoDirectX.SistemaTem("{5a508685-a254-4fba-9b82-9a24b00306af}", RegistryView.Registry32) ||
                    !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "XAudio2_7.dll")));
                v.Verificar("CLSID inventado não existe no HKLM",
                    !RegistroComDoDirectX.SistemaTem(nossa.Clsid, RegistryView.Registry32));
            }
            finally
            {
                RegistroComDoDirectX.Remover(classes);
                foreach (var visao in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, visao))
                            hkcu.DeleteSubKeyTree(@"Software\Classes\CLSID\" + alheia.Clsid, throwOnMissingSubKey: false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        // ---- Verificação do pacote ---------------------------------------------------------

        private static void TestarVerificacaoDoPacote(Verificador v, string sandbox)
        {
            v.Escrever("");
            v.Escrever("Conferência do pacote baixado");

            var falso = Path.Combine(sandbox, InstaladorDoRuntimeDirectX.NomeDoPacote);
            File.WriteAllBytes(falso, new byte[] { 0x4D, 0x5A, 1, 2, 3 });
            v.Verificar("arquivo qualquer com o nome do pacote é recusado",
                !InstaladorDoRuntimeDirectX.PacoteConfiavel(falso));

            var semAssinatura = System.Reflection.Assembly.GetEntryAssembly()!.Location;
            v.Verificar("exe sem assinatura (o próprio Mochila) não passa por Microsoft",
                !InstaladorDoRuntimeDirectX.AssinadoPelaMicrosoft(semAssinatura));

            var assinado = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe")
            }.FirstOrDefault(File.Exists);

            if (assinado != null)
            {
                v.Verificar($"exe assinado pela Microsoft passa ({Path.GetFileName(assinado)})",
                    InstaladorDoRuntimeDirectX.AssinadoPelaMicrosoft(assinado));
            }

            var cancelado = new CancellationTokenSource();
            cancelado.Cancel();
            var cancelou = false;
            try
            {
                InstaladorDoRuntimeDirectX.Instalar(null, cancelado.Token);
            }
            catch (OperationCanceledException)
            {
                cancelou = true;
            }
            catch (Exception)
            {
            }

            // O runtime montado no teste de estado ainda está lá: cancelar não pode estragá-lo.
            v.Verificar("cancelar vira OperationCanceledException", cancelou);
            v.Verificar("cancelar não mexe no runtime que já estava pronto", RuntimeDirectX.EstaPronto);
            v.Verificar("cancelar não deixa a pasta de preparo para trás",
                !Directory.Exists(Path.Combine(Caminhos.PastaRuntime, "_preparando")));
        }

        private static void ApagarSandbox(string sandbox)
        {
            try
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }
}
#endif   // DEBUG
