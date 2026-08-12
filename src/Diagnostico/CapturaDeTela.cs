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

namespace Launcher.Diagnostico
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

        public static void Capturar(Form janela, string caminhoDoPng, Size tamanho)
        {
            janela.StartPosition = FormStartPosition.Manual;
            janela.ShowInTaskbar = false;
            janela.Size = tamanho;

            // Fora da área visível: a janela precisa existir de verdade para ter handle
            // e layout, mas não pode aparecer na frente do que o usuário está fazendo.
            janela.Location = new Point(-32000, -32000);
            janela.Show();

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
                bitmap.Save(caminhoDoPng, ImageFormat.Png);
            }

            janela.Close();
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
