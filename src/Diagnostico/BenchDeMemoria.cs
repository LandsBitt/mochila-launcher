// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Launcher.Util;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Mede a RAM e os handles da janela cheia de jogos.
    ///
    /// A spec põe um número: com 200 jogos, o launcher parado não pode passar de ~80 MB.
    /// Isso não dá para verificar com o teste de unidade do cache — o que conta é o
    /// processo inteiro, com WinForms e GDI+ carregados, depois de rolar a biblioteca
    /// toda. Daí este modo separado: processo limpo, acervo sintético e rolagem de
    /// verdade.
    ///
    /// A janela é criada fora da tela e desenhada com DrawToBitmap. Janela fora da área
    /// visível não recebe WM_PAINT, e sem OnPaint a grade nunca pede miniatura nenhuma —
    /// a medição sairia otimista e mentirosa.
    ///
    /// Com --ciclos N a biblioteca é percorrida de ida e volta N vezes, medindo ao fim de
    /// cada volta. Uma medida só, no fim da descida, não enxerga vazamento: vazamento é
    /// uma série que sobe sem parar, não um número alto.
    /// </summary>
    public static class BenchDeMemoria
    {
        /// <summary>Teto da spec, em MB.</summary>
        private const double OrcamentoMb = 80.0;

        /// <summary>
        /// Ciclos iniciais que podem crescer sem ser vazamento: é o cache de miniaturas
        /// em disco enchendo e o GDI+ acomodando os buffers dele.
        /// </summary>
        private const int CiclosDeAquecimento = 3;

        // Teto de crescimento absoluto do fim do aquecimento até o último ciclo.
        //
        // Trabalha em dupla com o teste de monotonia: monotonia com piso de ruído pega
        // vazamento rápido, este delta pega o lento — alguns KB por ciclo passam pelo piso
        // e, num uso de meses, viram MB.
        private const double ToleranciaWorkingSetMb = 2.0;
        private const double ToleranciaHeapMb = 0.5;
        private const int ToleranciaGdi = 10;
        private const int ToleranciaUser = 10;

        // Piso de ruído: variação abaixo disso não conta como "subiu".
        //
        // Sem isso, o teste de subida monotônica acusa vazamento por causa de alguns KB
        // por ciclo — o working set oscila com o que o Windows resolve paginar, e o heap
        // gerenciado mexe sozinho (JIT de caminho frio, buffer que cresce uma vez). Handle
        // é contagem inteira e não tem ruído nenhum: ali qualquer +1 por ciclo conta.
        private const double RuidoWorkingSetMb = 0.25;
        private const double RuidoHeapMb = 0.02;

        private static readonly Size TamanhoDaJanela = new Size(1200, 800);

        /// <summary>Tempo dado à thread de miniaturas a cada tela antes de rolar de novo.</summary>
        private const int EsperaPorTelaMs = 400;

        /// <summary>Uma leitura das quatro métricas ao fim de um ciclo.</summary>
        private sealed class Amostra
        {
            public int Ciclo;
            public double WorkingSetMb;
            public double HeapMb;
            public int Gdi;
            public int User;
            public int ImagensVivas;
        }

        public static bool Executar(int quantidade, int ciclos, Action<string> escrever)
        {
            AcervoDeDemonstracao.Montar(quantidade);

            try
            {
                return Medir(quantidade, Math.Max(1, ciclos), escrever);
            }
            finally
            {
                AcervoDeDemonstracao.Limpar();
            }
        }

        private static bool Medir(int quantidade, int ciclos, Action<string> escrever)
        {
            Memoria.ColetarTudo();
            escrever($"Antes de abrir a janela      : {Memoria.WorkingSetMb(),7:F1} MB, " +
                     $"{Memoria.ObjetosGdi()} GDI, {Memoria.ObjetosUser()} USER");

            using (var janela = new FormPrincipal())
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Size = TamanhoDaJanela;
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                var amostras = new List<Amostra>(ciclos);
                var telas = 0;

                for (var ciclo = 1; ciclo <= ciclos; ciclo++)
                {
                    telas = PercorrerDeIdaEVolta(janela, escrever);
                    amostras.Add(Amostrar(ciclo, janela));
                }

                RelatarCorrida(escrever, quantidade, ciclos, telas, janela);
                RelatarSerie(escrever, amostras);

                var ultima = amostras[amostras.Count - 1];
                janela.Close();

                return Aprovar(escrever, amostras, ultima, janela.GradeParaDiagnostico.CardsVisiveis);
            }
        }

        // ---- Rolagem ------------------------------------------------------------------------

        /// <summary>
        /// Desce a grade inteira de PageDown em PageDown e volta de PageUp em PageUp,
        /// desenhando e deixando a thread de miniaturas trabalhar entre uma tela e outra.
        /// A volta é o que importa para achar vazamento: o mesmo card é carregado,
        /// descartado e carregado de novo.
        /// </summary>
        private static int PercorrerDeIdaEVolta(FormPrincipal janela, Action<string> escrever)
            => Percorrer(janela, Keys.PageDown, escrever) + Percorrer(janela, Keys.PageUp, escrever);

        private static int Percorrer(FormPrincipal janela, Keys tecla, Action<string> escrever)
        {
            var grade = janela.GradeParaDiagnostico;
            var area = new Rectangle(0, 0, janela.Width, janela.Height);
            var telas = 0;

            while (true)
            {
                using (var descartavel = new Bitmap(janela.Width, janela.Height))
                    janela.DrawToBitmap(descartavel, area);

                BombearMensagens(EsperaPorTelaMs);
                telas++;

                var antes = grade.IndiceSelecionado;
                grade.TratarTecla(tecla);

                if (grade.IndiceSelecionado == antes) break;   // chegou na ponta

                // Trava de segurança: biblioteca gigante não pode rodar para sempre.
                if (telas > 200)
                {
                    escrever("(parei a rolagem em 200 telas)");
                    break;
                }
            }
            return telas;
        }

        private static Amostra Amostrar(int ciclo, FormPrincipal janela)
        {
            // Coleta antes de ler: lixo pendente não pode passar por vazamento.
            Memoria.ColetarTudo();

            return new Amostra
            {
                Ciclo = ciclo,
                WorkingSetMb = Memoria.WorkingSetMb(),
                HeapMb = Memoria.HeapGerenciadoMb(),
                Gdi = Memoria.ObjetosGdi(),
                User = Memoria.ObjetosUser(),
                ImagensVivas = janela.MiniaturasParaDiagnostico.ImagensEmMemoria
            };
        }

        // ---- Relatório ----------------------------------------------------------------------

        private static void RelatarCorrida(Action<string> escrever, int quantidade, int ciclos,
                                           int telasPorCiclo, FormPrincipal janela)
        {
            var miniaturas = janela.MiniaturasParaDiagnostico;

            escrever("");
            escrever($"Jogos na biblioteca          : {quantidade}");
            escrever($"Ciclos (ida e volta)         : {ciclos}");
            escrever($"Telas por ciclo              : {telasPorCiclo}");
            escrever($"Cards visíveis por tela      : {janela.GradeParaDiagnostico.CardsVisiveis}");
            escrever($"Miniaturas carregadas        : {miniaturas.Carregadas} " +
                     $"({miniaturas.MiniaturasGeradas} geradas do zero)");
        }

        private static void RelatarSerie(Action<string> escrever, List<Amostra> amostras)
        {
            escrever("");
            // Heap em três casas: o movimento que interessa aqui é de KB, e arredondar
            // para MB esconderia justamente a subida que estamos procurando.
            escrever("Ciclo   Working set        Heap     GDI    USER   Imagens vivas");

            foreach (var a in amostras)
            {
                escrever($"{a.Ciclo,5}   {a.WorkingSetMb,8:F1} MB {a.HeapMb,9:F3} MB " +
                         $"{a.Gdi,7} {a.User,7} {a.ImagensVivas,15}");
            }
        }

        // ---- Aprovação ----------------------------------------------------------------------

        private static bool Aprovar(Action<string> escrever, List<Amostra> amostras,
                                    Amostra ultima, int cardsVisiveis)
        {
            escrever("");

            var dentroDoOrcamento = ultima.WorkingSetMb <= OrcamentoMb;
            escrever(dentroDoOrcamento
                ? $"OK: {ultima.WorkingSetMb:F1} MB, abaixo do teto de {OrcamentoMb:F0} MB da spec."
                : $"ACIMA do teto de {OrcamentoMb:F0} MB da spec: {ultima.WorkingSetMb:F1} MB.");

            var proporcional = ultima.ImagensVivas <= Math.Max(cardsVisiveis * 2, 12);
            escrever(proporcional
                ? "OK: a memória segue o tamanho da janela, não o da biblioteca."
                : $"ATENÇÃO: {ultima.ImagensVivas} imagens vivas para {cardsVisiveis} cards visíveis — " +
                  "algo não está sendo descartado.");

            var estavel = true;
            estavel &= ConferirPlato(escrever, amostras, "working set", a => a.WorkingSetMb,
                                     ToleranciaWorkingSetMb, RuidoWorkingSetMb, "MB");
            estavel &= ConferirPlato(escrever, amostras, "heap gerenciado", a => a.HeapMb,
                                     ToleranciaHeapMb, RuidoHeapMb, "MB");
            estavel &= ConferirPlato(escrever, amostras, "objetos GDI", a => a.Gdi, ToleranciaGdi, 0, "");
            estavel &= ConferirPlato(escrever, amostras, "objetos USER", a => a.User, ToleranciaUser, 0, "");

            return dentroDoOrcamento && proporcional && estavel;
        }

        /// <summary>
        /// Confere se a métrica virou platô depois do aquecimento. Reprova de duas formas:
        /// crescimento acima da tolerância, ou crescimento a cada ciclo sem exceção — uma
        /// série que só sobe é vazamento mesmo quando a soma ainda é pequena.
        /// </summary>
        private static bool ConferirPlato(Action<string> escrever, List<Amostra> amostras, string nome,
                                          Func<Amostra, double> ler, double tolerancia, double ruido,
                                          string unidade)
        {
            if (amostras.Count <= CiclosDeAquecimento)
            {
                escrever($"  {nome}: poucos ciclos para julgar (rode com --ciclos {CiclosDeAquecimento + 3}).");
                return true;
            }

            var inicio = ler(amostras[CiclosDeAquecimento - 1]);
            var fim = ler(amostras[amostras.Count - 1]);
            var crescimento = fim - inicio;

            // Subiu de verdade em todo ciclo, ou só tremeu dentro do ruído?
            var sempreSubindo = true;
            for (var i = CiclosDeAquecimento; i < amostras.Count; i++)
            {
                if (ler(amostras[i]) - ler(amostras[i - 1]) <= ruido) { sempreSubindo = false; break; }
            }

            var sufixo = unidade.Length > 0 ? " " + unidade : "";
            var ok = crescimento <= tolerancia && !sempreSubindo;

            escrever(ok
                ? $"  OK: {nome} estabilizou (ciclo {CiclosDeAquecimento} -> {amostras.Count}: " +
                  $"{crescimento:+0.000;-0.000;0}{sufixo})."
                : $"  VAZAMENTO: {nome} cresce sem parar (ciclo {CiclosDeAquecimento} -> {amostras.Count}: " +
                  $"{crescimento:+0.000;-0.000;0}{sufixo}{(sempreSubindo ? ", subindo em todo ciclo" : "")}).");

            return ok;
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
    }
}
#endif   // DEBUG
