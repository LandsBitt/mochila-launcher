using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Launcher.UI
{
    /// <summary>
    /// O desenho comum de botão, chip e campo: retângulo arredondado, cor conforme o
    /// estado do mouse e nada da moldura cinza que o WinForms põe sozinho.
    /// </summary>
    internal static class PinturaPlana
    {
        public const int Raio = 5;

        public static void Fundo(Graphics g, Rectangle area, Color parente,
                                 bool sobOMouse, bool pressionado, bool ativo, bool habilitado)
        {
            g.Clear(parente);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var cor = !habilitado ? Tema.Superficie
                : ativo ? Color.FromArgb(46, 62, 92)                 // marcado: azul lavado, não berrante
                : pressionado ? Tema.Superficie
                : sobOMouse ? Tema.ControleAceso
                : Tema.Controle;

            var borda = !habilitado ? Tema.Borda
                : ativo ? Tema.Acento
                : sobOMouse ? Tema.BordaClara
                : Tema.Borda;

            using (var caminho = Formas.Arredondado(area, Raio))
            {
                using (var pincel = new SolidBrush(cor))
                    g.FillPath(pincel, caminho);

                using (var caneta = new Pen(borda))
                    g.DrawPath(caneta, caminho);
            }
        }
    }

    /// <summary>
    /// Botão do tema: cantos arredondados e estados de mouse desenhados por nós.
    ///
    /// É um controle, e não só um <see cref="Button"/> configurado, porque canto redondo e
    /// hover discreto não existem no FlatStyle do WinForms — ele insiste na moldura cinza
    /// do sistema, que é justamente o que destoa numa janela escura.
    /// </summary>
    public class BotaoPlano : Button
    {
        private bool _sobOMouse;
        private bool _pressionado;

        public BotaoPlano()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Tema.Controle;
            ForeColor = Tema.Texto;
            UseVisualStyleBackColor = false;
        }

        /// <summary>Botão principal da janela: contorno e texto no azul do tema.</summary>
        public bool Destaque { get; set; }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _sobOMouse = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _sobOMouse = false;
            _pressionado = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressionado = true; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressionado = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var area = new Rectangle(0, 0, Width - 1, Height - 1);

            PinturaPlana.Fundo(e.Graphics, area, Parent?.BackColor ?? Tema.Superficie,
                               _sobOMouse, _pressionado, Destaque, Enabled);

            var cor = !Enabled ? Tema.TextoFraco : Destaque ? Tema.Acento : ForeColor;

            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), cor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Chip que liga e desliga (o filtro de favoritos), com o mesmo desenho do botão.</summary>
    public sealed class ChipAlternavel : CheckBox
    {
        private bool _sobOMouse;

        public ChipAlternavel()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            FlatStyle = FlatStyle.Flat;
            BackColor = Tema.Controle;
            ForeColor = Tema.Texto;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _sobOMouse = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _sobOMouse = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var area = new Rectangle(0, 0, Width - 1, Height - 1);

            PinturaPlana.Fundo(e.Graphics, area, Parent?.BackColor ?? Tema.Superficie,
                               _sobOMouse, pressionado: false, ativo: Checked, habilitado: Enabled);

            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height),
                Checked ? Tema.TextoForte : Tema.TextoFraco,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// Campo de texto do tema: sem borda própria (quem desenha a moldura é o painel, em
    /// <see cref="PainelDeControles.Emoldurar"/>) e com texto de dica quando está vazio.
    ///
    /// A dica é a nativa do Windows (EM_SETCUEBANNER): some ao digitar, não entra no
    /// Text e não precisa de evento nenhum para se manter — um placeholder feito à mão
    /// custaria mais código e ainda erraria na hora de limpar a busca.
    /// </summary>
    public sealed class CampoDeTexto : TextBox
    {
        private const int DefinirDica = 0x1501;   // EM_SETCUEBANNER

        private string _dica = "";

        public CampoDeTexto()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Tema.Controle;
            ForeColor = Tema.TextoForte;
        }

        public string Dica
        {
            get => _dica;
            set
            {
                _dica = value ?? "";
                AplicarDica();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AplicarDica();
        }

        private void AplicarDica()
        {
            if (!IsHandleCreated || _dica.Length == 0) return;

            try
            {
                SendMessage(Handle, DefinirDica, new IntPtr(1), _dica);
            }
            catch (Exception)
            {
                // Sem a dica o campo continua funcionando; não vale derrubar a janela.
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr janela, int mensagem, IntPtr wParam, string lParam);
    }

    /// <summary>
    /// Combo do tema. O WinForms não deixa pintar um ComboBox pelo OnPaint, então o
    /// desenho vem depois do WM_PAINT dele: cobrimos a seta branca do sistema, o miolo e a
    /// moldura. Os itens, esses sim, são owner-draw de verdade.
    /// </summary>
    public sealed class ComboEscuro : ComboBox
    {
        private const int Pintar = 0x000F;         // WM_PAINT
        private const int PintarJanela = 0x0317;   // WM_PRINT
        private const int PintarNoDc = 0x0318;     // WM_PRINTCLIENT
        private const int LarguraDaSeta = 20;

        private bool _sobOMouse;

        public ComboEscuro()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = Tema.Controle;
            ForeColor = Tema.Texto;
            ItemHeight = 20;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _sobOMouse = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _sobOMouse = false; Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;

            // O mesmo evento desenha o item da lista aberta e o texto do combo fechado.
            var naLista = (e.State & DrawItemState.ComboBoxEdit) == 0;
            var destacado = naLista && (e.State & DrawItemState.Selected) != 0;

            using (var pincel = new SolidBrush(destacado ? Color.FromArgb(46, 62, 92) : Tema.Controle))
                e.Graphics.FillRectangle(pincel, e.Bounds);

            var texto = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString(), Font, texto,
                destacado ? Tema.TextoForte : Tema.Texto,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void WndProc(ref Message mensagem)
        {
            base.WndProc(ref mensagem);

            // WM_PAINT é o desenho na tela. WM_PRINTCLIENT é o mesmo combo pedido para um
            // DC de fora (DrawToBitmap, e é o que a captura de tela do diagnóstico usa):
            // sem tratá-lo, a foto sairia com a seta branca do sistema que a tela não tem.
            if (mensagem.Msg == Pintar)
            {
                using (var g = Graphics.FromHwnd(Handle))
                    DesenharMoldura(g);
            }
            else if ((mensagem.Msg == PintarNoDc || mensagem.Msg == PintarJanela) && mensagem.WParam != IntPtr.Zero)
            {
                using (var g = Graphics.FromHdc(mensagem.WParam))
                    DesenharMoldura(g);
            }
        }

        private void DesenharMoldura(Graphics g)
        {
            var area = new Rectangle(0, 0, Width - 1, Height - 1);

            // A seta do sistema é clara e ignora BackColor: cobrimos a faixa dela inteira.
            using (var pincel = new SolidBrush(_sobOMouse ? Tema.ControleAceso : Tema.Controle))
                g.FillRectangle(pincel, new Rectangle(Width - LarguraDaSeta, 1, LarguraDaSeta - 1, Height - 2));

            g.SmoothingMode = SmoothingMode.AntiAlias;

            var centro = Width - (LarguraDaSeta / 2) - 2;
            var meio = Height / 2;

            using (var caneta = new Pen(Tema.TextoFraco, 1.4f))
            {
                g.DrawLine(caneta, centro - 4, meio - 2, centro, meio + 2);
                g.DrawLine(caneta, centro, meio + 2, centro + 4, meio - 2);
            }

            using (var caminho = Formas.Arredondado(area, PinturaPlana.Raio))
            {
                // Os cantos que sobram fora do arredondado recebem a cor do painel.
                using (var regiao = new Region(new Rectangle(0, 0, Width, Height)))
                {
                    regiao.Exclude(caminho);
                    using (var pincel = new SolidBrush(Parent?.BackColor ?? Tema.Superficie))
                        g.FillRegion(pincel, regiao);
                }

                using (var caneta = new Pen(Focused ? Tema.Acento : _sobOMouse ? Tema.BordaClara : Tema.Borda))
                    g.DrawPath(caneta, caminho);
            }
        }
    }

    /// <summary>
    /// Painel de fundo da barra superior e do rodapé: cor do tema, um fio de separação e
    /// a moldura dos campos que moram dentro dele.
    ///
    /// A moldura ser desenhada aqui, e não por cada campo, é o que evita um painel extra
    /// só para arredondar a caixa de busca — janela a mais é handle a mais.
    /// </summary>
    public class PainelDeControles : Panel
    {
        private readonly List<Control> _emoldurados = new List<Control>();

        public PainelDeControles()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            BackColor = Tema.Superficie;
            ForeColor = Tema.Texto;
        }

        /// <summary>Fio de separação no alto (rodapé) ou embaixo (barra superior).</summary>
        public bool LinhaEmCima { get; set; }

        public bool LinhaEmBaixo { get; set; }

        /// <summary>Campo que ganha moldura arredondada e, se pedido, a lupa da busca.</summary>
        public void Emoldurar(Control campo, bool comLupa = false)
        {
            campo.Tag = comLupa ? "lupa" : null;
            _emoldurados.Add(campo);

            // Foco muda a cor da borda: repinta só a faixa do campo, não o painel inteiro.
            campo.GotFocus += (_, _) => Invalidate(Moldura(campo));
            campo.LostFocus += (_, _) => Invalidate(Moldura(campo));
        }

        /// <summary>
        /// A moldura é maior que o campo, e com lupa a folga da esquerda é bem maior: a
        /// lupa mora nesse vão. Desenhá-la sob o campo seria desenhá-la para ninguém — o
        /// TextBox é uma janela filha e pinta por cima do painel.
        /// </summary>
        private static Rectangle Moldura(Control campo)
        {
            var area = campo.Bounds;
            var folgaEsquerda = campo.Tag as string == "lupa" ? 30 : 10;

            return Rectangle.FromLTRB(area.Left - folgaEsquerda, area.Top - 7, area.Right + 10, area.Bottom + 7);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var campo in _emoldurados)
            {
                var area = Moldura(campo);

                using (var caminho = Formas.Arredondado(area, PinturaPlana.Raio))
                {
                    using (var pincel = new SolidBrush(Tema.Controle))
                        g.FillPath(pincel, caminho);

                    using (var caneta = new Pen(campo.Focused ? Tema.Acento : Tema.Borda))
                        g.DrawPath(caneta, caminho);
                }

                if (campo.Tag as string == "lupa") DesenharLupa(g, area);
            }

            g.SmoothingMode = SmoothingMode.None;

            using (var caneta = new Pen(Tema.Borda))
            {
                if (LinhaEmBaixo) g.DrawLine(caneta, 0, Height - 1, Width, Height - 1);
                if (LinhaEmCima) g.DrawLine(caneta, 0, 0, Width, 0);
            }
        }

        private static void DesenharLupa(Graphics g, Rectangle moldura)
        {
            var centro = new Point(moldura.X + 14, moldura.Y + (moldura.Height / 2) - 1);

            using (var caneta = new Pen(Tema.TextoFraco, 1.5f))
            {
                g.DrawEllipse(caneta, centro.X - 5, centro.Y - 5, 9, 9);
                g.DrawLine(caneta, centro.X + 4, centro.Y + 4, centro.X + 7, centro.Y + 7);
            }
        }
    }

    /// <summary>
    /// Rodapé: contagem à esquerda, jogo selecionado em destaque e o resto apagado.
    ///
    /// Desenhado à mão porque uma Label só sabe uma cor, e o que faz o rodapé ser legível
    /// de canto de olho é justamente o nome do jogo saltar do resto da frase.
    /// </summary>
    public sealed class Rodape : PainelDeControles
    {
        private string _contagem = "";
        private string _destaque = "";
        private string _detalhe = "";
        private bool _alerta;

        public Rodape()
        {
            LinhaEmCima = true;
            Height = 28;
        }

        /// <summary>
        /// <paramref name="destaque"/> é o nome do jogo (ou vazio); <paramref name="alerta"/>
        /// pinta o detalhe de amarelo, para o recado da última sessão.
        /// </summary>
        public void Definir(string contagem, string destaque, string detalhe, bool alerta)
        {
            _contagem = contagem ?? "";
            _destaque = destaque ?? "";
            _detalhe = detalhe ?? "";
            _alerta = alerta;

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var x = Padding.Left;
            var linha = new Rectangle(0, 1, Width, Height - 1);

            x += Escrever(e.Graphics, _contagem, x, linha, Tema.TextoFraco);

            if (_destaque.Length > 0)
            {
                x += Escrever(e.Graphics, "   •   ", x, linha, Tema.Borda);
                x += Escrever(e.Graphics, _destaque, x, linha, Tema.TextoForte);
            }

            if (_detalhe.Length > 0)
            {
                if (_destaque.Length == 0) x += Escrever(e.Graphics, "   •   ", x, linha, Tema.Borda);
                Escrever(e.Graphics, _detalhe, x, linha, _alerta ? Tema.TextoBaixaConfianca : Tema.TextoFraco);
            }
        }

        /// <summary>Escreve um trecho e devolve a largura dele, para o próximo continuar.</summary>
        private int Escrever(Graphics g, string texto, int x, Rectangle linha, Color cor)
        {
            if (texto.Length == 0 || x >= Width) return 0;

            var area = new Rectangle(x, linha.Y, Width - x - Padding.Right, linha.Height);

            TextRenderer.DrawText(g, texto, Font, area, cor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);

            return TextRenderer.MeasureText(g, texto, Font, new Size(int.MaxValue, linha.Height),
                TextFormatFlags.NoPadding).Width;
        }
    }

    /// <summary>
    /// Menu de contexto escuro. O renderizador padrão do WinForms desenha borda, margem e
    /// realce claros — no meio de uma janela preta, o menu do botão direito era a única
    /// coisa branca da tela.
    /// </summary>
    public sealed class RenderizadorEscuro : ToolStripProfessionalRenderer
    {
        public RenderizadorEscuro() : base(new CoresDoMenu()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Tema.TextoFraco
                : e.Item.Selected ? Tema.TextoForte
                : Tema.Texto;

            base.OnRenderItemText(e);
        }

        private sealed class CoresDoMenu : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Tema.Superficie;
            public override Color MenuBorder => Tema.Borda;
            public override Color MenuItemBorder => Tema.BordaClara;
            public override Color MenuItemSelected => Color.FromArgb(46, 62, 92);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(46, 62, 92);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(46, 62, 92);
            public override Color ImageMarginGradientBegin => Tema.Superficie;
            public override Color ImageMarginGradientMiddle => Tema.Superficie;
            public override Color ImageMarginGradientEnd => Tema.Superficie;
            public override Color SeparatorDark => Tema.Borda;
            public override Color SeparatorLight => Tema.Borda;
        }
    }

    /// <summary>Botões chatos de montar na mão, sempre iguais.</summary>
    public static class Botoes
    {
        public static Button Criar(string texto, Point posicao, int largura, int altura = 30)
            => new BotaoPlano
            {
                Text = texto,
                Location = posicao,
                Size = new Size(largura, altura)
            };

        /// <summary>O botão que a janela espera que eu clique (Adicionar, Salvar, Baixar).</summary>
        public static Button CriarPrincipal(string texto, Point posicao, int largura, int altura = 30)
        {
            var botao = (BotaoPlano)Criar(texto, posicao, largura, altura);
            botao.Destaque = true;
            return botao;
        }
    }
}
