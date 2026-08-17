using System;
using System.Drawing;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// Editar título e argumentos de um jogo. Duas caixas de texto — não precisa ser mais
    /// que isso, e uma janela grande para dois campos só atrapalharia.
    ///
    /// O caminho do executável aparece, mas não se edita aqui: trocar de executável é
    /// "Localizar executável...", que valida o drive e marca a escolha como minha.
    /// </summary>
    public sealed class FormEditarJogo : Form
    {
        private readonly CampoDeTexto _titulo;
        private readonly CampoDeTexto _argumentos;

        public FormEditarJogo(Jogo jogo)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));

            Text = "Editar jogo";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(520, 250);

            Tema.AplicarNaJanela(this);

            _titulo = new CampoDeTexto
            {
                Location = new Point(18, 40),
                Size = new Size(484, 28),
                Text = jogo.Titulo
            };

            _argumentos = new CampoDeTexto
            {
                Location = new Point(18, 108),
                Size = new Size(484, 28),
                Text = jogo.Argumentos ?? ""
            };

            Controls.Add(Rotulo("Título", 18, 18));
            Controls.Add(_titulo);
            Controls.Add(Rotulo("Argumentos de linha de comando (opcional)", 18, 86));
            Controls.Add(_argumentos);
            Controls.Add(Rotulo(jogo.ExecutavelRelativo, 18, 152, Tema.TextoFraco, 484));

            var salvar = Botoes.CriarPrincipal("Salvar", new Point(322, 196), 84);
            salvar.DialogResult = DialogResult.OK;

            var cancelar = Botoes.Criar("Cancelar", new Point(414, 196), 88);
            cancelar.DialogResult = DialogResult.Cancel;

            Controls.Add(salvar);
            Controls.Add(cancelar);

            AcceptButton = salvar;
            CancelButton = cancelar;

            // Título vazio deixaria um card sem nome e um jogo impossível de achar na busca.
            salvar.Click += (_, e) =>
            {
                if (TituloEscolhido.Length != 0) return;

                MessageBox.Show(this, "O título não pode ficar vazio.", "Editar jogo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

                DialogResult = DialogResult.None;
                _titulo.Focus();
            };
        }

        public string TituloEscolhido => _titulo.Text.Trim();

        public string ArgumentosEscolhidos => _argumentos.Text.Trim();

        private static Label Rotulo(string texto, int x, int y, Color? cor = null, int largura = 400)
            => new Label
            {
                Text = texto,
                Location = new Point(x, y),
                Size = new Size(largura, 20),
                ForeColor = cor ?? Tema.Texto,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
    }
}
