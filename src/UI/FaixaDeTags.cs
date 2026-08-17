using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// A faixa de tags do acervo, logo abaixo da barra superior: as mais usadas primeiro,
    /// clicáveis, ligando e desligando o filtro <c>#tag</c> da busca.
    ///
    /// Desenhada à mão pelo mesmo motivo da grade — um <c>Button</c> por tag seria uma
    /// janela nativa por tag, e um acervo com trinta tags viraria trinta handles para
    /// mostrar trinta palavras. Aqui é um controle só, sem timer e sem imagem.
    ///
    /// Some sozinha quando não há tag nenhuma: acervo recém-escaneado não precisa de uma
    /// faixa vazia ocupando 34 px.
    /// </summary>
    public sealed class FaixaDeTags : PainelDeControles
    {
        private const int AlturaDoChip = 22;
        private const int Vao = 6;
        private const int MargemLateral = 12;
        private const int RecuoInterno = 10;

        private readonly List<ContagemDeTag> _tags = new List<ContagemDeTag>();
        private readonly List<Rectangle> _areas = new List<Rectangle>();
        private readonly HashSet<string> _ativas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _sobOMouse = -1;

        public FaixaDeTags()
        {
            Dock = DockStyle.Top;
            LinhaEmBaixo = true;
            Height = 0;
            Visible = false;
            Font = new Font("Segoe UI", 8.5f);
        }

        /// <summary>Cliquei numa tag. A janela decide o que fazer com a busca.</summary>
        public event EventHandler<string>? TagAcionada;

        /// <summary>
        /// Recalcula a faixa a partir do acervo inteiro (não do filtrado): a lista de tags
        /// não pode encolher conforme eu filtro, senão desliga o filtro que eu acabei de
        /// ligar e não há como voltar.
        /// </summary>
        public void Definir(IEnumerable<Jogo> acervo, ConsultaDeBusca consulta)
        {
            _tags.Clear();
            _tags.AddRange(Etiquetas.PorFrequencia(acervo));

            _ativas.Clear();
            foreach (var tag in consulta.Tags) _ativas.Add(tag);

            _sobOMouse = -1;
            Visible = _tags.Count > 0;
            Height = _tags.Count > 0 ? AlturaDoChip + 12 : 0;

            RecalcularAreas();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalcularAreas();
        }

        /// <summary>
        /// Uma linha só, e o que não couber fica de fora. Duas linhas roubariam altura da
        /// grade em toda sessão por causa de um acervo com muitas tags — e quem tem trinta
        /// tags acha mais rápido digitando <c>#</c> do que procurando na faixa.
        /// </summary>
        private void RecalcularAreas()
        {
            _areas.Clear();
            if (_tags.Count == 0) return;

            var x = MargemLateral;
            var y = (Height - AlturaDoChip) / 2;

            foreach (var tag in _tags)
            {
                // Medida sem Graphics de propósito: a faixa é montada durante a carga da
                // janela, e CreateGraphics ali forçaria o handle a nascer antes da hora.
                var largura = TextRenderer.MeasureText(Rotulo(tag), Font,
                    new Size(int.MaxValue, AlturaDoChip), TextFormatFlags.NoPadding).Width + (2 * RecuoInterno);

                if (x + largura > Width - MargemLateral) break;

                _areas.Add(new Rectangle(x, y, largura, AlturaDoChip));
                x += largura + Vao;
            }
        }

        private static string Rotulo(ContagemDeTag tag) => $"{tag.Tag}  {tag.Quantidade}";

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (var i = 0; i < _areas.Count; i++)
            {
                var ativa = _ativas.Contains(_tags[i].Tag);
                var aceso = i == _sobOMouse;

                using (var caminho = Formas.Arredondado(_areas[i], AlturaDoChip / 2))
                {
                    using (var pincel = new SolidBrush(ativa
                               ? Color.FromArgb(46, 62, 92)
                               : aceso ? Tema.ControleAceso : Tema.Controle))
                    {
                        g.FillPath(pincel, caminho);
                    }

                    using (var caneta = new Pen(ativa ? Tema.Acento : aceso ? Tema.BordaClara : Tema.Borda))
                        g.DrawPath(caneta, caminho);
                }

                TextRenderer.DrawText(g, Rotulo(_tags[i]), Font, _areas[i],
                    ativa ? Tema.TextoForte : aceso ? Tema.Texto : Tema.TextoFraco,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
            }
        }

        // ---- Mouse -----------------------------------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var indice = IndiceEm(e.Location);
            if (indice == _sobOMouse) return;

            _sobOMouse = indice;
            Cursor = indice >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_sobOMouse < 0) return;

            _sobOMouse = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            var indice = IndiceEm(e.Location);
            if (indice >= 0) TagAcionada?.Invoke(this, _tags[indice].Tag);
        }

        private int IndiceEm(Point ponto)
        {
            for (var i = 0; i < _areas.Count; i++)
            {
                if (_areas[i].Contains(ponto)) return i;
            }
            return -1;
        }
    }
}
