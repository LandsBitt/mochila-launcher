// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Desenha a janela num arquivo PNG, sem depender do que está na tela.
    ///
    /// Serve para conferir o visual da grade em máquina sem ninguém olhando (e sem
    /// roubar o foco de um jogo aberto). Usa DrawToBitmap, então captura o desenho do
    /// próprio controle, não um print do monitor.
    /// </summary>
    public static class CapturaDeTela
    {
        /// <summary>Tempo dado à thread de miniaturas antes de desenhar.</summary>
        private const int EsperaDeCargaMs = 2500;

        /// <summary>
        /// <paramref name="preparar"/> roda com a janela já criada e antes do desenho —
        /// é como se fotografa uma tela que só existe depois de eu apertar algo — e devolve
        /// o painel sobreposto, se houver (ver <see cref="Sobrepor"/>).
        /// </summary>
        public static void Capturar(Form janela, string caminhoDoPng, Size tamanho,
                                    Func<Form, Control?>? preparar = null)
        {
            janela.StartPosition = FormStartPosition.Manual;
            janela.ShowInTaskbar = false;
            janela.Size = tamanho;

            // Fora da área visível: a janela precisa existir de verdade para ter handle
            // e layout, mas não pode aparecer na frente do que o usuário está fazendo.
            janela.Location = new Point(-32000, -32000);
            janela.Show();

            var sobreposto = preparar?.Invoke(janela);

            var area = new Rectangle(0, 0, janela.Width, janela.Height);

            // Janela fora da tela não recebe WM_PAINT, e Refresh() no formulário não força
            // o desenho dos controles filhos. Sem esse primeiro desenho a grade nunca pede
            // as miniaturas, e a captura sai só com os placeholders cinza. Um DrawToBitmap
            // descartado resolve: ele chama o OnPaint da grade de verdade.
            using (var aquecimento = new Bitmap(janela.Width, janela.Height))
                janela.DrawToBitmap(aquecimento, area);

            BombearMensagens(EsperaDeCargaMs);

            using (var bitmap = new Bitmap(janela.Width, janela.Height))
            {
                janela.DrawToBitmap(bitmap, area);
                Sobrepor(bitmap, janela, sobreposto);

                bitmap.Save(caminhoDoPng, ImageFormat.Png);
            }

            janela.Close();
        }

        /// <summary>
        /// Redesenha um painel sobreposto no lugar dele.
        ///
        /// Existe porque <c>DrawToBitmap</c> percorre os filhos na ordem da coleção, e não
        /// na ordem Z: um painel trazido para a frente (a tela de detalhes) sai da foto
        /// coberto pela grade, ao contrário do que a tela mostra. Na tela quem pinta é o
        /// Windows, e lá a ordem Z vale — isto é conserto de foto, não de desenho.
        /// </summary>
        private static void Sobrepor(Bitmap bitmap, Form janela, Control? painel)
        {
            if (painel is null || !painel.Visible || painel.Width <= 0 || painel.Height <= 0) return;

            // O bitmap cobre a janela inteira, borda e barra de título inclusas; as
            // coordenadas do painel são da área cliente.
            var origemDaCliente = janela.PointToScreen(Point.Empty);
            var deslocamento = new Point(origemDaCliente.X - janela.Left, origemDaCliente.Y - janela.Top);

            using (var recorte = new Bitmap(painel.Width, painel.Height))
            {
                painel.DrawToBitmap(recorte, new Rectangle(0, 0, painel.Width, painel.Height));

                using (var g = Graphics.FromImage(bitmap))
                    g.DrawImageUnscaled(recorte, deslocamento.X + painel.Left, deslocamento.Y + painel.Top);
            }
        }

        /// <summary>
        /// Deixa a fila de mensagens andar pelo tempo pedido: é o que permite a carga
        /// assíncrona das miniaturas terminar antes do desenho.
        /// </summary>
        private static void BombearMensagens(int milissegundos)
        {
            var fim = Environment.TickCount + milissegundos;

            while (Environment.TickCount < fim)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }
    }
}
#endif   // DEBUG
