// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Launcher.Dados;
using Launcher.Modelo;
using Launcher.Util;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Mede o launcher enquanto um jogo de verdade está aberto.
    ///
    /// O número dos 80 MB da fase 4 não vale aqui: aquilo é o custo de escolher o jogo,
    /// quando ninguém mais disputa recurso. A métrica desta fase é outra — quanto sobra
    /// do launcher DEPOIS de esconder a janela e devolver a RAM ao sistema, com um
    /// processo-jogo ativo. Alvo: abaixo de 10 MB.
    ///
    /// O "jogo" é uma cópia do próprio Launcher.exe rodando com --dormir, dentro da
    /// sandbox. Processo real, com handle real e evento de saída real: medir contra um
    /// processo falso não provaria nada.
    /// </summary>
    public static class BenchDeLancamento
    {
        /// <summary>Alvo da fase, em MB, com a janela escondida e o jogo aberto.</summary>
        private const double AlvoEscondidoMb = 10.0;

        /// <summary>
        /// Teto de CPU aceitável em 10 s dormindo. Acima disso sobrou timer, polling ou
        /// thread viva — exatamente o que a spec proíbe enquanto o jogo roda.
        /// </summary>
        private const double TetoDeCpuMs = 50.0;

        /// <summary>
        /// O primeiro ciclo usa um jogo longo: precisa de folga para os 10 s de medição
        /// de CPU e para a sessão passar de meio minuto (senão nada é creditado).
        /// </summary>
        private const int SegundosDoJogoSimulado = 40;

        /// <summary>Ciclos seguintes só exercitam esconder/voltar: jogo curto basta.</summary>
        private const int SegundosDoJogoEmCiclosExtras = 8;

        private const int SegundosDeObservacaoDeCpu = 10;
        private const int JogosNoAcervo = 60;

        private static readonly Size TamanhoDaJanela = new Size(1200, 800);

        /// <summary>Um ciclo de lançar, esconder e voltar.</summary>
        private sealed class Ciclo
        {
            public int Numero;
            public Instantanea Antes;
            public Instantanea Escondido;
            public Instantanea Reconstruida;
            public bool Voltou;
            public int SegundosCreditados;
        }

        public static bool Executar(int ciclos, Action<string> escrever)
        {
            AcervoDeDemonstracao.Montar(JogosNoAcervo);

            try
            {
                return Medir(Math.Max(1, ciclos), escrever);
            }
            finally
            {
                AcervoDeDemonstracao.Limpar();
            }
        }

        private static bool Medir(int quantosCiclos, Action<string> escrever)
        {
            var idDoJogo = AcrescentarJogoSimulado();

            using (var janela = new FormPrincipal())
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Size = TamanhoDaJanela;
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                var jogo = janela.BibliotecaParaDiagnostico.ObterPorId(idDoJogo);
                if (jogo is null)
                {
                    escrever("ERRO: não achei o jogo simulado na biblioteca.");
                    return false;
                }

                var cpuMs = double.NaN;
                var ciclos = new List<Ciclo>(quantosCiclos);

                for (var numero = 1; numero <= quantosCiclos; numero++)
                {
                    var longo = numero == 1;
                    jogo.Argumentos = $"--dormir {(longo ? SegundosDoJogoSimulado : SegundosDoJogoEmCiclosExtras)}";

                    var ciclo = ExecutarCiclo(janela, jogo, numero, escrever,
                                              medirCpu: longo, out var cpuDoCiclo);
                    if (longo) cpuMs = cpuDoCiclo;

                    ciclos.Add(ciclo);
                }

                RelatarSerie(escrever, ciclos);
                janela.Close();

                return Aprovar(escrever, ciclos, cpuMs);
            }
        }

        private static Ciclo ExecutarCiclo(FormPrincipal janela, Jogo jogo, int numero,
                                           Action<string> escrever, bool medirCpu, out double cpuMs)
        {
            cpuMs = double.NaN;

            // Enche a grade de miniaturas: é o estado real na hora em que eu aperto Enter.
            EncherAGrade(janela);
            Memoria.ColetarTudo();

            var ciclo = new Ciclo { Numero = numero, Antes = Instantaneo() };
            var segundosAntes = jogo.SegundosJogados;

            if (numero == 1)
                escrever($"Antes de lançar               : {Descrever(ciclo.Antes)}");

            janela.LancarParaDiagnostico(jogo);

            BombearMensagens(2000);
            Memoria.ColetarTudo();
            ciclo.Escondido = Instantaneo();

            if (numero == 1)
            {
                escrever($"2 s depois de esconder        : {Descrever(ciclo.Escondido)}");
                escrever($"  janela visível: {janela.Visible}");
            }

            if (medirCpu)
            {
                cpuMs = MedirCpuDormindo(janela);
                escrever($"CPU em {SegundosDeObservacaoDeCpu} s dormindo          : {cpuMs,7:F1} ms");
            }

            ciclo.Voltou = EsperarAJanelaVoltar(janela);
            Memoria.ColetarTudo();

            if (numero == 1)
                escrever($"Depois que o jogo encerrou    : {Descrever(Instantaneo())}");

            // A grade volta a se desenhar: sem isso a leitura seria de uma janela vazia.
            EncherAGrade(janela);
            Memoria.ColetarTudo();
            ciclo.Reconstruida = Instantaneo();

            if (numero == 1)
                escrever($"Com a grade redesenhada       : {Descrever(ciclo.Reconstruida)}");

            ciclo.SegundosCreditados = jogo.SegundosJogados - segundosAntes;
            return ciclo;
        }

        private static void RelatarSerie(Action<string> escrever, List<Ciclo> ciclos)
        {
            if (ciclos.Count < 2) return;

            escrever("");
            escrever("Ciclo   Escondido   Reconstruído     GDI    USER   Segundos");

            foreach (var c in ciclos)
            {
                escrever($"{c.Numero,5}   {c.Escondido.WorkingSetMb,6:F1} MB   " +
                         $"{c.Reconstruida.WorkingSetMb,8:F1} MB {c.Reconstruida.Gdi,7} " +
                         $"{c.Reconstruida.User,7} {c.SegundosCreditados,10}");
            }
        }

        // ---- Aprovação -----------------------------------------------------------------------

        private static bool Aprovar(Action<string> escrever, List<Ciclo> ciclos, double cpuMs)
        {
            var primeiro = ciclos[0];
            var antes = primeiro.Antes;
            var escondido = primeiro.Escondido;
            var reconstruida = primeiro.Reconstruida;
            var voltou = primeiro.Voltou;
            var segundos = primeiro.SegundosCreditados;

            escrever("");

            var enxuto = escondido.WorkingSetMb < AlvoEscondidoMb;
            escrever(enxuto
                ? $"OK: {escondido.WorkingSetMb:F1} MB escondido, abaixo do alvo de {AlvoEscondidoMb:F0} MB."
                : $"ACIMA do alvo: {escondido.WorkingSetMb:F1} MB escondido (alvo {AlvoEscondidoMb:F0} MB).");

            var liberou = escondido.WorkingSetMb < antes.WorkingSetMb;
            escrever(liberou
                ? $"OK: a grade foi mesmo liberada ({antes.WorkingSetMb:F1} -> {escondido.WorkingSetMb:F1} MB)."
                : "ATENÇÃO: esconder a janela não devolveu memória — a grade não foi liberada.");

            var dormindo = cpuMs < TetoDeCpuMs;
            escrever(dormindo
                ? $"OK: {cpuMs:F1} ms de CPU em {SegundosDeObservacaoDeCpu} s — o launcher está dormindo."
                : $"ACORDADO: {cpuMs:F1} ms de CPU em {SegundosDeObservacaoDeCpu} s. Sobrou timer ou polling.");

            escrever(voltou
                ? "OK: a janela voltou sozinha pelo evento de saída do processo."
                : "FALHOU: a janela não voltou depois que o jogo encerrou.");

            var contou = segundos > 0;
            escrever(contou
                ? $"OK: a sessão foi contabilizada ({segundos} s)."
                : "FALHOU: a sessão não foi contabilizada.");

            // Reconstruir a grade não pode custar handles a mais que a primeira montagem.
            var semVazamento = reconstruida.Gdi <= antes.Gdi + 10 && reconstruida.User <= antes.User + 10;
            escrever(semVazamento
                ? $"OK: grade reconstruída sem vazar handles (GDI {antes.Gdi} -> {reconstruida.Gdi}, " +
                  $"USER {antes.User} -> {reconstruida.User})."
                : $"VAZAMENTO na reconstrução: GDI {antes.Gdi} -> {reconstruida.Gdi}, " +
                  $"USER {antes.User} -> {reconstruida.User}.");

            return enxuto && liberou && dormindo && voltou && contou && semVazamento &&
                   ConferirHandlesEntreCiclos(escrever, ciclos);
        }

        /// <summary>
        /// Uma medida só não separa custo único de vazamento por lançamento. Se os
        /// handles subirem em TODO ciclo, cada jogo aberto deixa lixo para trás — e a
        /// conta acaba no limite de 10.000 handles do processo, que não deixa o app
        /// lento: faz ele parar de desenhar.
        /// </summary>
        private static bool ConferirHandlesEntreCiclos(Action<string> escrever, List<Ciclo> ciclos)
        {
            if (ciclos.Count < 3)
            {
                escrever($"  (handles por ciclo: rode com --ciclos 4 para separar custo único de vazamento)");
                return true;
            }

            var gdiOk = !SempreSubindo(ciclos, c => c.Reconstruida.Gdi);
            var userOk = !SempreSubindo(ciclos, c => c.Reconstruida.User);

            var primeiro = ciclos[0].Reconstruida;
            var ultimo = ciclos[ciclos.Count - 1].Reconstruida;

            escrever(gdiOk && userOk
                ? $"  OK: handles estabilizaram em {ciclos.Count} lançamentos " +
                  $"(GDI {primeiro.Gdi} -> {ultimo.Gdi}, USER {primeiro.User} -> {ultimo.User})."
                : $"  VAZAMENTO por lançamento: GDI {primeiro.Gdi} -> {ultimo.Gdi}, " +
                  $"USER {primeiro.User} -> {ultimo.User} (subindo em todo ciclo).");

            return gdiOk && userOk;
        }

        private static bool SempreSubindo(List<Ciclo> ciclos, Func<Ciclo, int> ler)
        {
            for (var i = 1; i < ciclos.Count; i++)
            {
                if (ler(ciclos[i]) <= ler(ciclos[i - 1])) return false;
            }
            return true;
        }

        // ---- Medição -------------------------------------------------------------------------

        private struct Instantanea
        {
            public double WorkingSetMb;
            public int Gdi;
            public int User;
        }

        private static Instantanea Instantaneo() => new Instantanea
        {
            WorkingSetMb = Memoria.WorkingSetMb(),
            Gdi = Memoria.ObjetosGdi(),
            User = Memoria.ObjetosUser()
        };

        private static string Descrever(Instantanea i)
            => $"{i.WorkingSetMb,7:F1} MB, {i.Gdi,4} GDI, {i.User,4} USER";

        /// <summary>
        /// Mede a CPU do processo com a janela escondida, rodando um laço de mensagens de
        /// verdade — o mesmo em que o launcher fica parado enquanto o jogo roda.
        ///
        /// Não dá para usar DoEvents num laço aqui: o próprio laço queimaria CPU e a
        /// medida sairia suja. Bloquear em GetMessage é o estado real, e qualquer timer
        /// vivo apareceria justamente porque as mensagens dele seriam processadas.
        /// </summary>
        private static double MedirCpuDormindo(Form janela)
        {
            var contexto = new ApplicationContext();
            var inicio = Memoria.CpuDoProcesso();

            // Timer de threadpool só para sair do laço: dispara uma vez, custo desprezível.
            using (var despertador = new System.Threading.Timer(_ => EncerrarLaco(janela, contexto), null,
                                                               SegundosDeObservacaoDeCpu * 1000, Timeout.Infinite))
            {
                Application.Run(contexto);
                GC.KeepAlive(despertador);
            }

            return (Memoria.CpuDoProcesso() - inicio).TotalMilliseconds;
        }

        private static void EncerrarLaco(Form janela, ApplicationContext contexto)
        {
            try
            {
                if (janela.IsDisposed || !janela.IsHandleCreated) { contexto.ExitThread(); return; }
                janela.BeginInvoke((Action)(() => contexto.ExitThread()));
            }
            catch (Exception)
            {
                contexto.ExitThread();
            }
        }

        private static bool EsperarAJanelaVoltar(Form janela)
        {
            var limite = Environment.TickCount + (SegundosDoJogoSimulado + 15) * 1000;

            while (Environment.TickCount < limite)
            {
                if (janela.Visible) return true;
                Application.DoEvents();
                Thread.Sleep(50);
            }
            return janela.Visible;
        }

        /// <summary>
        /// Força o desenho da grade e dá tempo à thread de miniaturas. Janela fora da tela
        /// não recebe WM_PAINT, e sem OnPaint nenhuma imagem é carregada.
        /// </summary>
        private static void EncherAGrade(Form janela)
        {
            for (var i = 0; i < 3; i++)
            {
                using (var descartavel = new Bitmap(janela.Width, janela.Height))
                    janela.DrawToBitmap(descartavel, new Rectangle(0, 0, janela.Width, janela.Height));

                BombearMensagens(700);
            }
        }

        private static void BombearMensagens(int milissegundos)
        {
            var fim = Environment.TickCount + milissegundos;

            while (Environment.TickCount < fim)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        // ---- Jogo simulado --------------------------------------------------------------------

        /// <summary>
        /// Copia o próprio Launcher.exe para dentro da sandbox e cadastra como jogo, com
        /// --dormir nos argumentos. Devolve o id.
        /// </summary>
        private static string AcrescentarJogoSimulado()
        {
            var pasta = Path.Combine(Caminhos.PastaBase, "Jogos", "bench-lancamento");
            Directory.CreateDirectory(pasta);

            var origem = Assembly.GetEntryAssembly()!.Location;
            var destino = Path.Combine(pasta, "jogo-de-mentira.exe");
            File.Copy(origem, destino, overwrite: true);

            var biblioteca = Biblioteca.Carregar();
            var jogo = new Jogo
            {
                Id = biblioteca.GerarId("Jogo de mentira"),
                Titulo = "Jogo de mentira (bench)",
                ExecutavelRelativo = Caminhos.ParaRelativo(destino),
                Argumentos = $"--dormir {SegundosDoJogoSimulado}"
            };

            biblioteca.Jogos.Add(jogo);
            biblioteca.Salvar();

            return jogo.Id;
        }
    }
}
#endif   // DEBUG
