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
using Mochila.Util;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Mede a promessa da fase 11: <b>abrir e fechar a tela de detalhes cinquenta vezes
    /// não pode subir o working set</b>.
    ///
    /// É a medida que separa este painel de um <c>Form</c> por jogo. Cada abertura carrega
    /// uma imagem em resolução cheia (600x900), e uma que não fosse liberada some da vista
    /// mas não da memória — cinquenta delas são mais de cem MB. Um teste de unidade não
    /// enxerga isso: o que conta é o processo inteiro, com GDI+ carregado e a arte
    /// realmente decodificada.
    ///
    /// A série importa mais que o número final, pelo mesmo motivo do bench da grade:
    /// vazamento é uma linha que só sobe, não uma medida alta.
    /// </summary>
    public static class BenchDeDetalhes
    {
        /// <summary>Aberturas antes da primeira medida: cache de thumb, JIT e buffers do GDI+.</summary>
        private const int Aquecimento = 5;

        /// <summary>De quantas em quantas aberturas o bench anota uma linha.</summary>
        private const int PassoDaAmostra = 10;

        // Crescimento tolerado entre a primeira e a última amostra.
        private const double ToleranciaWorkingSetMb = 2.0;
        private const double ToleranciaHeapMb = 0.5;
        private const int ToleranciaGdi = 5;
        private const int ToleranciaUser = 5;

        private static readonly Size TamanhoDaJanela = new Size(1200, 800);

        private sealed class Amostra
        {
            public int Aberturas;
            public double WorkingSetMb;
            public double HeapMb;
            public int Gdi;
            public int User;
        }

        public static bool Executar(int aberturas, Action<string> escrever)
        {
            AcervoDeDemonstracao.Montar(40);

            try
            {
                return Medir(Math.Max(PassoDaAmostra, aberturas), escrever);
            }
            finally
            {
                AcervoDeDemonstracao.Limpar();
            }
        }

        private static bool Medir(int aberturas, Action<string> escrever)
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

                // Fora da tela não chega WM_PAINT: sem um desenho de verdade a grade nunca
                // pede miniatura e a medida sai otimista.
                Desenhar(janela);

                for (var i = 0; i < Aquecimento; i++) AbrirEFechar(janela);

                var amostras = new List<Amostra> { Amostrar(0) };

                for (var feitas = 0; feitas < aberturas; feitas++)
                {
                    AbrirEFechar(janela);
                    if ((feitas + 1) % PassoDaAmostra == 0) amostras.Add(Amostrar(feitas + 1));
                }

                var sobrouArte = janela.PainelDeDetalhesParaDiagnostico.ArteCarregadaParaDiagnostico;

                escrever("");
                escrever($"Aberturas medidas            : {aberturas} (mais {Aquecimento} de aquecimento)");
                escrever("");
                escrever("Aberturas   Working set        Heap     GDI    USER");

                foreach (var a in amostras)
                {
                    escrever($"{a.Aberturas,9}   {a.WorkingSetMb,8:F1} MB {a.HeapMb,9:F3} MB " +
                             $"{a.Gdi,7} {a.User,7}");
                }

                janela.Close();

                escrever("");
                var semArte = !sobrouArte;
                escrever(semArte
                    ? "OK: nenhuma arte grande viva depois do último fechamento."
                    : "FALHA: a arte do último jogo continuou na memória depois de fechar.");

                var estavel = true;
                estavel &= Conferir(escrever, amostras, "working set", a => a.WorkingSetMb, ToleranciaWorkingSetMb, "MB");
                estavel &= Conferir(escrever, amostras, "heap gerenciado", a => a.HeapMb, ToleranciaHeapMb, "MB");
                estavel &= Conferir(escrever, amostras, "objetos GDI", a => a.Gdi, ToleranciaGdi, "");
                estavel &= Conferir(escrever, amostras, "objetos USER", a => a.User, ToleranciaUser, "");

                return semArte && estavel;
            }
        }

        /// <summary>
        /// Um ciclo completo, pelo mesmo caminho do "I": abre, desenha (é o desenho que
        /// exercita fonte, pincel e a arte em si) e fecha.
        /// </summary>
        private static void AbrirEFechar(FormPrincipal janela)
        {
            var painel = janela.AbrirDetalhesParaDiagnostico();

            if (painel.Width > 0 && painel.Height > 0)
            {
                using (var descartavel = new Bitmap(painel.Width, painel.Height))
                    painel.DrawToBitmap(descartavel, new Rectangle(0, 0, painel.Width, painel.Height));
            }

            janela.FecharDetalhesParaDiagnostico();

            // Deixa a fila andar: a thread de miniaturas volta a trabalhar quando a grade
            // reaparece, e ignorar isso mediria um estado que não existe no uso real.
            Application.DoEvents();
            Thread.Sleep(1);
        }

        private static void Desenhar(Form janela)
        {
            using (var descartavel = new Bitmap(janela.Width, janela.Height))
                janela.DrawToBitmap(descartavel, new Rectangle(0, 0, janela.Width, janela.Height));

            Application.DoEvents();
        }

        private static Amostra Amostrar(int aberturas)
        {
            Memoria.ColetarTudo();

            return new Amostra
            {
                Aberturas = aberturas,
                WorkingSetMb = Memoria.WorkingSetMb(),
                HeapMb = Memoria.HeapGerenciadoMb(),
                Gdi = Memoria.ObjetosGdi(),
                User = Memoria.ObjetosUser()
            };
        }

        private static bool Conferir(Action<string> escrever, List<Amostra> amostras, string nome,
                                     Func<Amostra, double> ler, double tolerancia, string unidade)
        {
            var crescimento = ler(amostras[amostras.Count - 1]) - ler(amostras[0]);
            var ok = crescimento <= tolerancia;
            var sufixo = unidade.Length > 0 ? " " + unidade : "";

            escrever(ok
                ? $"  OK: {nome} estável ({crescimento:+0.000;-0.000;0}{sufixo})."
                : $"  VAZAMENTO: {nome} cresceu {crescimento:+0.000;-0.000;0}{sufixo} " +
                  $"em {amostras[amostras.Count - 1].Aberturas} aberturas.");

            return ok;
        }
    }
}
#endif   // DEBUG
