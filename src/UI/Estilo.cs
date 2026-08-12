using System.Drawing;
using System.Windows.Forms;

namespace Launcher.UI
{
    /// <summary>Paleta escura única do launcher. A grade da fase 4 usa as mesmas cores.</summary>
    public static class Cores
    {
        public static readonly Color Fundo = Color.FromArgb(24, 24, 28);
        public static readonly Color FundoPainel = Color.FromArgb(32, 32, 38);
        public static readonly Color FundoControle = Color.FromArgb(52, 52, 60);
        public static readonly Color Texto = Color.Gainsboro;
        public static readonly Color TextoFraco = Color.FromArgb(150, 150, 160);
        public static readonly Color Erro = Color.FromArgb(230, 120, 120);

        /// <summary>Linha de baixa confiança (placar &lt; 30) na janela de revisão.</summary>
        public static readonly Color FundoBaixaConfianca = Color.FromArgb(92, 76, 16);

        public static readonly Color TextoBaixaConfianca = Color.FromArgb(255, 230, 150);

        /// <summary>Candidato vetado (instalador, redistribuível): visível, mas apagado.</summary>
        public static readonly Color TextoExcluido = Color.FromArgb(130, 110, 110);

        /// <summary>Contorno do card selecionado na grade.</summary>
        public static readonly Color Selecao = Color.FromArgb(120, 180, 255);
    }

    /// <summary>Botões chatos de montar na mão, sempre iguais.</summary>
    public static class Botoes
    {
        public static Button Criar(string texto, Point posicao, int largura, int altura = 30)
        {
            var botao = new Button
            {
                Text = texto,
                Location = posicao,
                Size = new Size(largura, altura),
                FlatStyle = FlatStyle.Flat,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto,
                UseVisualStyleBackColor = false
            };
            botao.FlatAppearance.BorderSize = 0;
            return botao;
        }
    }
}
