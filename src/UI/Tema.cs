using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Launcher.UI
{
    /// <summary>
    /// O tema escuro do launcher, num lugar só.
    ///
    /// Cinza puro deixa a grade com cara de caixa de diálogo; o escuro daqui é levemente
    /// azulado, e as capas — que são o conteúdo — ficam sendo a única coisa colorida da
    /// tela. Trocar a paleta inteira do launcher é mexer nas constantes deste arquivo.
    /// </summary>
    public static class Tema
    {
        // ---- Superfícies ---------------------------------------------------------------------

        /// <summary>Fundo da grade e das janelas.</summary>
        public static readonly Color Fundo = Color.FromArgb(0x16, 0x18, 0x1D);

        /// <summary>Barra superior, rodapé, menus: um degrau acima do fundo.</summary>
        public static readonly Color Superficie = Color.FromArgb(0x1E, 0x21, 0x2A);

        /// <summary>Campo de busca, combo, botão: o degrau em que se clica.</summary>
        public static readonly Color Controle = Color.FromArgb(0x26, 0x2A, 0x35);

        /// <summary>O mesmo controle sob o mouse.</summary>
        public static readonly Color ControleAceso = Color.FromArgb(0x2F, 0x34, 0x41);

        /// <summary>Borda discreta que separa um controle do painel atrás dele.</summary>
        public static readonly Color Borda = Color.FromArgb(0x2E, 0x34, 0x40);

        /// <summary>Borda de quem está sob o mouse — clara, mas ainda não é seleção.</summary>
        public static readonly Color BordaClara = Color.FromArgb(0x4A, 0x52, 0x63);

        // ---- Texto ----------------------------------------------------------------------------

        public static readonly Color Texto = Color.FromArgb(0xC8, 0xCD, 0xD6);

        /// <summary>Texto que precisa saltar: título selecionado, nome do jogo no rodapé.</summary>
        public static readonly Color TextoForte = Color.FromArgb(0xE8, 0xEC, 0xF2);

        public static readonly Color TextoFraco = Color.FromArgb(0x7E, 0x87, 0x94);

        public static readonly Color Erro = Color.FromArgb(230, 120, 120);

        // ---- Destaque -------------------------------------------------------------------------

        /// <summary>O azul da seleção, que virou o azul de tudo: foco, borda e botão principal.</summary>
        public static readonly Color Acento = Color.FromArgb(120, 180, 255);

        /// <summary>Contorno do card selecionado na grade.</summary>
        public static readonly Color Selecao = Acento;

        /// <summary>Faixa do jogo em execução.</summary>
        public static readonly Color Sucesso = Color.FromArgb(62, 190, 130);

        // ---- Janela de revisão do scan --------------------------------------------------------

        /// <summary>Linha de baixa confiança (placar &lt; 30) na janela de revisão.</summary>
        public static readonly Color FundoBaixaConfianca = Color.FromArgb(92, 76, 16);

        public static readonly Color TextoBaixaConfianca = Color.FromArgb(255, 230, 150);

        /// <summary>Candidato vetado (instalador, redistribuível): visível, mas apagado.</summary>
        public static readonly Color TextoExcluido = Color.FromArgb(130, 110, 110);

        // ---- Janelas --------------------------------------------------------------------------

        /// <summary>
        /// Veste uma janela com o tema: ícone do gamepad e barra de título escura.
        ///
        /// A barra de título é do Windows, não nossa — sem o aviso ao DWM, uma faixa branca
        /// fica em cima de uma janela preta. O atributo é ignorado em Windows antigo, e
        /// falhar aqui não pode impedir a janela de abrir.
        /// </summary>
        public static void AplicarNaJanela(Form janela)
        {
            janela.BackColor = Fundo;
            janela.ForeColor = Texto;

            if (IconeDoLauncher.DaJanela is { } icone) janela.Icon = icone;

            janela.HandleCreated += (_, _) => EscurecerBarraDeTitulo(janela.Handle);
            if (janela.IsHandleCreated) EscurecerBarraDeTitulo(janela.Handle);
        }

        private const int ModoEscuro = 20;
        private const int ModoEscuroAntesDo20H1 = 19;

        private static void EscurecerBarraDeTitulo(IntPtr janela)
        {
            var ligado = 1;

            try
            {
                if (DwmSetWindowAttribute(janela, ModoEscuro, ref ligado, sizeof(int)) != 0)
                    DwmSetWindowAttribute(janela, ModoEscuroAntesDo20H1, ref ligado, sizeof(int));
            }
            catch (Exception)
            {
                // Windows sem DWM (ou sessão sem composição): a barra fica clara e pronto.
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr janela, int atributo, ref int valor, int tamanho);
    }

    /// <summary>
    /// Geometria repetida no desenho à mão. O retângulo arredondado aparece no card, no
    /// botão, no campo de busca e no combo — vale ter um lugar só que saiba fazê-lo.
    /// </summary>
    public static class Formas
    {
        public static GraphicsPath Arredondado(Rectangle area, int raio)
        {
            var caminho = new GraphicsPath();

            // Área degenerada (controle ainda sem tamanho) viraria arco inválido no GDI+.
            if (area.Width <= 0 || area.Height <= 0) return caminho;

            var limite = Math.Max(1, Math.Min(raio, Math.Min(area.Width, area.Height) / 2));
            var d = limite * 2;

            caminho.AddArc(area.X, area.Y, d, d, 180, 90);
            caminho.AddArc(area.Right - d, area.Y, d, d, 270, 90);
            caminho.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
            caminho.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
            caminho.CloseFigure();

            return caminho;
        }
    }
}
