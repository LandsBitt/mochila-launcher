using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// Pergunta uma tag. Uma caixa de texto e a lista das que já existem, para eu não
    /// inventar "corrida", "corridas" e "carro" para a mesma coisa.
    ///
    /// A lista é o ponto: sem ela, tag vira campo livre e o acervo termina com quarenta
    /// etiquetas quase iguais que não filtram nada.
    /// </summary>
    public sealed class FormTag : Form
    {
        private readonly CampoDeTexto _campo;

        public FormTag(string titulo, string explicacao, IEnumerable<ContagemDeTag> existentes)
        {
            Text = titulo;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(420, 320);

            Tema.AplicarNaJanela(this);

            _campo = new CampoDeTexto
            {
                Location = new Point(18, 46),
                Size = new Size(384, 24),
                Dica = "corrida"
            };

            var lista = new ListBox
            {
                Location = new Point(18, 92),
                Size = new Size(384, 168),
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false
            };

            foreach (var tag in existentes) lista.Items.Add($"{tag.Tag}   ({tag.Quantidade})");

            // Clicar na lista preenche o campo: continuo podendo editar antes de confirmar.
            lista.SelectedIndexChanged += (_, _) =>
            {
                if (lista.SelectedIndex < 0) return;

                var texto = lista.Items[lista.SelectedIndex]?.ToString() ?? "";
                var fim = texto.IndexOf("   (", StringComparison.Ordinal);

                _campo.Text = fim > 0 ? texto.Substring(0, fim) : texto;
            };

            Controls.Add(Rotulo(explicacao, 18, 16, 384));
            Controls.Add(_campo);
            Controls.Add(Rotulo(lista.Items.Count > 0 ? "Tags já usadas no acervo" : "Nenhuma tag no acervo ainda",
                                18, 74, 384, Tema.TextoFraco));
            Controls.Add(lista);

            var confirmar = Botoes.CriarPrincipal("Aplicar", new Point(222, 272), 84);
            confirmar.DialogResult = DialogResult.OK;

            var cancelar = Botoes.Criar("Cancelar", new Point(314, 272), 88);
            cancelar.DialogResult = DialogResult.Cancel;

            Controls.Add(confirmar);
            Controls.Add(cancelar);

            AcceptButton = confirmar;
            CancelButton = cancelar;

            confirmar.Click += (_, _) =>
            {
                if (TagEscolhida != null) return;

                MessageBox.Show(this, "Escreva uma tag (ou escolha uma da lista).", titulo,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

                DialogResult = DialogResult.None;
                _campo.Focus();
            };
        }

        /// <summary>A tag já normalizada, ou null se o campo está vazio.</summary>
        public string? TagEscolhida => Etiquetas.Normalizar(_campo.Text);

        private static Label Rotulo(string texto, int x, int y, int largura, Color? cor = null)
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
