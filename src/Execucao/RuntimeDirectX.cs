using System;
using System.IO;
using System.Linq;
using Mochila.Dados;
using Mochila.Scanner;

namespace Mochila.Execucao
{
    /// <summary>
    /// O DirectX 9 portátil: faz o jogo achar as DLLs de <c>_mochila\runtime\directx</c>
    /// num PC que nunca rodou o instalador do DirectX, sem pedir admin.
    ///
    /// O Windows 11 já traz o Direct3D 9. O que falta num PC novo são as bibliotecas
    /// auxiliares (D3DX9, XInput 1.3, XAudio2, XACT), que só o instalador antigo colocava
    /// no System32. São dois mecanismos, um para cada tipo de DLL:
    ///
    /// 1. <b>PATH do processo do jogo.</b> A pasta da arquitetura certa entra na frente do
    ///    PATH que o jogo herda. O Windows procura DLL primeiro na pasta do jogo, depois no
    ///    System32 e só no fim no PATH. Ou seja: <b>PC com o DirectX instalado continua
    ///    usando o dele</b>, e o HD só entra quando o sistema não tem a DLL.
    /// 2. <b>Registro COM no HKCU</b>, para XACT e XAudio2 ≤ 2.7, que o jogo pede por
    ///    CLSID e não por nome. Ver <see cref="RegistroComDoDirectX"/>.
    ///
    /// Tudo aqui é opcional e nada impede o lançamento: sem runtime baixado, exe que não é
    /// PE, arquitetura desconhecida ou registro recusado, o jogo abre como abria antes.
    /// </summary>
    public static class RuntimeDirectX
    {
        /// <summary>
        /// Gravado por último pelo instalador. Pasta sem ele é instalação pela metade (HD
        /// arrancado no meio da extração) e não vale.
        /// </summary>
        public const string ArquivoDeConclusao = "pronto.txt";

        public static string PastaX86 => Path.Combine(Caminhos.PastaRuntimeDirectX, "x86");

        public static string PastaX64 => Path.Combine(Caminhos.PastaRuntimeDirectX, "x64");

        public static bool EstaPronto
        {
            get
            {
                try
                {
                    return File.Exists(Path.Combine(Caminhos.PastaRuntimeDirectX, ArquivoDeConclusao)) &&
                           Directory.Exists(PastaX86);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>A pasta de DLLs que serve para um exe desta arquitetura, ou null.</summary>
        public static string? PastaDaArquitetura(ArquiteturaPe arquitetura) => arquitetura switch
        {
            ArquiteturaPe.X86 => PastaX86,
            ArquiteturaPe.X64 => PastaX64,
            // ARM64 não tem DirectX legado: o pacote de 2010 não conhece a arquitetura.
            _ => null
        };

        /// <summary>
        /// Deixa o runtime pronto para <paramref name="executavel"/>: descobre a arquitetura e
        /// registra as classes COM que faltam. Nunca lança.
        /// </summary>
        public static PreparoDoRuntime Preparar(string executavel)
        {
            if (!EstaPronto) return PreparoDoRuntime.Nenhum;

            var arquitetura = LeitorPe.Ler(executavel)?.Arquitetura ?? ArquiteturaPe.Desconhecida;
            var pasta = PastaDaArquitetura(arquitetura);
            if (pasta is null || !Directory.Exists(pasta)) return PreparoDoRuntime.Nenhum;

            try
            {
                var registradas = RegistroComDoDirectX.Registrar(arquitetura, pasta);
                return new PreparoDoRuntime(pasta, registradas, aviso: null);
            }
            catch (Exception erro)
            {
                // O PATH continua valendo: D3DX e XInput resolvem mesmo sem o registro. Só o
                // som de jogo com XACT/XAudio2 antigo fica em risco, e isso vira recado.
                return new PreparoDoRuntime(pasta, 0,
                    $"Não consegui registrar o áudio do DirectX portátil ({erro.Message}). " +
                    "Se o jogo abrir sem som, é isso.");
            }
        }

        /// <summary>
        /// Apaga o que <see cref="Preparar"/> registrou. Chamado quando o jogo fecha e na
        /// abertura do Mochila (sobra de uma sessão que terminou com o launcher morto).
        /// </summary>
        public static void DesfazerRegistro() => RegistroComDoDirectX.Remover();

        /// <summary>Quantas DLLs há em cada pasta e quanto ocupam. Para a tela de configurações.</summary>
        public static string Descrever()
        {
            if (!EstaPronto) return "Não baixado. Jogos antigos dependem do DirectX instalado em cada PC.";

            try
            {
                var x86 = Directory.GetFiles(PastaX86, "*.dll");
                var x64 = Directory.Exists(PastaX64) ? Directory.GetFiles(PastaX64, "*.dll") : Array.Empty<string>();
                var bytes = x86.Concat(x64).Sum(arquivo => new FileInfo(arquivo).Length);

                return $"Pronto: {x86.Length} DLLs de 32 bits e {x64.Length} de 64 bits " +
                       $"({bytes / 1024.0 / 1024.0:F0} MB). Vale para todos os jogos, em qualquer PC.";
            }
            catch (Exception)
            {
                return "Não consegui ler a pasta do runtime (o HD ainda está conectado?).";
            }
        }
    }

    /// <summary>O que o runtime DirectX fez para um lançamento.</summary>
    public sealed class PreparoDoRuntime
    {
        public static readonly PreparoDoRuntime Nenhum = new PreparoDoRuntime(null, 0, null);

        public PreparoDoRuntime(string? pasta, int classesRegistradas, string? aviso)
        {
            Pasta = pasta;
            ClassesRegistradas = classesRegistradas;
            Aviso = aviso;
        }

        /// <summary>A pasta que entra no PATH do jogo. null = o runtime não se aplica.</summary>
        public string? Pasta { get; }

        public int ClassesRegistradas { get; }

        public string? Aviso { get; }

        /// <summary>
        /// Põe a pasta no PATH do <b>próprio Mochila</b> até o Dispose.
        ///
        /// Por que no Mochila, e não no ProcessStartInfo: o jogo sobe por ShellExecute
        /// (<c>UseShellExecute = true</c>), que é o que deixa o Windows pedir elevação ao jogo
        /// que exige admin. ShellExecute não aceita ambiente próprio, mas o processo criado
        /// herda o de quem o criou. Então o PATH muda só no instante do Process.Start e volta
        /// logo depois: o jogo leva uma cópia, e o Mochila fica como estava.
        /// </summary>
        public IDisposable AplicarNoAmbiente() => new PathTemporario(Pasta);

        private sealed class PathTemporario : IDisposable
        {
            private readonly string? _anterior;
            private readonly bool _mudou;

            public PathTemporario(string? pasta)
            {
                if (pasta is null) return;

                _anterior = Environment.GetEnvironmentVariable("PATH");
                Environment.SetEnvironmentVariable("PATH",
                    string.IsNullOrEmpty(_anterior) ? pasta : pasta + Path.PathSeparator + _anterior);
                _mudou = true;
            }

            public void Dispose()
            {
                if (_mudou) Environment.SetEnvironmentVariable("PATH", _anterior);
            }
        }
    }
}
