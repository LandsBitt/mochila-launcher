using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Launcher.Modelo;

namespace Launcher.UI
{
    /// <summary>
    /// A grade de capas.
    ///
    /// Um controle só, desenhado à mão — nada de um PictureBox por jogo. Só os cards
    /// visíveis são desenhados, e só as imagens deles ficam na memória. Com 200 jogos, a
    /// diferença entre isto e uma tela de controles é a diferença entre abrir e não abrir
    /// num notebook fraco.
    ///
    /// O acabamento (canto arredondado, hover, seleção grossa) segue a mesma regra: nada
    /// que custe por frame. O hover repinta um card, não a grade; as fontes e os pincéis
    /// são criados uma vez por pintura e não um por card; e não há timer, animação nem
    /// bitmap intermediário em lugar nenhum daqui.
    /// </summary>
    public sealed class GradeDeCapas : Panel
    {
        private const int RaioDoCard = 8;
        private const int EspessuraDaSelecao = 4;
        private const int EspessuraDoHover = 2;

        private readonly CacheDeMiniaturas _miniaturas;
        private readonly List<Jogo> _jogos = new List<Jogo>();
        private readonly HashSet<string> _idsVisiveis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private LayoutDaGrade _layout;
        private TamanhoCard _tamanhoDoCard = TamanhoCard.M;
        private int _selecionado = -1;
        private int _sobOMouse = -1;
        private string? _idEmExecucao;

        /// <summary>
        /// Só existe depois do primeiro hover: quem roda o bench nunca passa o mouse, e
        /// uma ToolTip criada à toa é uma janela nativa a mais no processo.
        /// </summary>
        private ToolTip? _dica;

        public GradeDeCapas(CacheDeMiniaturas miniaturas)
        {
            _miniaturas = miniaturas ?? throw new ArgumentNullException(nameof(miniaturas));

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Tema.Fundo;
            TabStop = true;

            _layout = new LayoutDaGrade(_tamanhoDoCard, LayoutDaGrade.LarguraUtil(Width), 0);
            _miniaturas.ImagemPronta += AoFicarPronta;
        }

        /// <summary>Enter ou duplo clique num card.</summary>
        public event EventHandler<Jogo>? JogoAcionado;

        public event EventHandler? SelecaoMudou;

        public IReadOnlyList<Jogo> Jogos => _jogos;

        public Jogo? JogoSelecionado
            => _selecionado >= 0 && _selecionado < _jogos.Count ? _jogos[_selecionado] : null;

        public int IndiceSelecionado => _selecionado;

        /// <summary>
        /// Quantos cards o último desenho pintou. É o número que a memória do launcher
        /// deve acompanhar — não o tamanho da biblioteca.
        /// </summary>
        public int CardsVisiveis { get; private set; }

        /// <summary>Layout atual — a fatia testável da grade. (Control.Layout é um evento; daí o nome.)</summary>
        public LayoutDaGrade LayoutAtual => _layout;

        /// <summary>
        /// Id do jogo que está aberto agora, ou null. O card ganha faixa e fica travado
        /// contra um segundo lançamento.
        /// </summary>
        public string? IdEmExecucao
        {
            get => _idEmExecucao;
            set
            {
                if (string.Equals(_idEmExecucao, value, StringComparison.OrdinalIgnoreCase)) return;

                _idEmExecucao = value;
                Invalidate();
            }
        }

        public TamanhoCard TamanhoDoCard
        {
            get => _tamanhoDoCard;
            set
            {
                if (_tamanhoDoCard == value) return;

                _tamanhoDoCard = value;
                _sobOMouse = -1;
                RecalcularLayout();
                GarantirSelecaoVisivel();
                Invalidate();
            }
        }

        /// <summary>Troca a lista mostrada (resultado da busca/ordenação) preservando a seleção.</summary>
        public void DefinirJogos(IEnumerable<Jogo> jogos)
        {
            var idSelecionado = JogoSelecionado?.Id;

            _jogos.Clear();
            _jogos.AddRange(jogos);

            _selecionado = -1;
            _sobOMouse = -1;

            if (idSelecionado != null)
            {
                for (var i = 0; i < _jogos.Count; i++)
                {
                    if (string.Equals(_jogos[i].Id, idSelecionado, StringComparison.OrdinalIgnoreCase))
                    {
                        _selecionado = i;
                        break;
                    }
                }
            }

            if (_selecionado < 0 && _jogos.Count > 0) _selecionado = 0;

            RecalcularLayout();
            AutoScrollPosition = new Point(0, 0);
            GarantirSelecaoVisivel();
            Invalidate();

            SelecaoMudou?.Invoke(this, EventArgs.Empty);
        }

        public void Selecionar(int indice)
        {
            if (_jogos.Count == 0)
            {
                _selecionado = -1;
                return;
            }

            var novo = Math.Max(0, Math.Min(_jogos.Count - 1, indice));
            if (novo == _selecionado) return;

            var anterior = _selecionado;
            var rolagemAntes = AutoScrollPosition.Y;

            _selecionado = novo;
            GarantirSelecaoVisivel();

            // Rolou: a tela inteira trocou de conteúdo. Não rolou: só dois cards mudaram.
            if (AutoScrollPosition.Y != rolagemAntes)
            {
                Invalidate();
            }
            else
            {
                InvalidarCard(anterior);
                InvalidarCard(novo);
            }

            SelecaoMudou?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Índice do card sob um ponto do controle, ou -1. Usado pelo arrastar-e-soltar.</summary>
        public int IndiceEmPonto(Point pontoNoControle)
            => _layout.IndiceEm(pontoNoControle, -AutoScrollPosition.Y);

        /// <summary>Jogo sob um ponto do controle, ou null.</summary>
        public Jogo? JogoEmPonto(Point pontoNoControle)
        {
            var indice = IndiceEmPonto(pontoNoControle);
            return indice >= 0 && indice < _jogos.Count ? _jogos[indice] : null;
        }

        /// <summary>Move a seleção pelas setas (também é o que um d-pad de controle faria).</summary>
        public void MoverSelecao(int colunas, int linhas)
            => Selecionar(_layout.Mover(_selecionado, colunas, linhas));

        public void AcionarSelecionado()
        {
            if (JogoSelecionado is { } jogo) JogoAcionado?.Invoke(this, jogo);
        }

        /// <summary>
        /// Solta todas as miniaturas da memória. Chamada quando a janela some para dar
        /// lugar ao jogo: nada está à vista, então nada precisa estar carregado. Elas
        /// voltam sozinhas no próximo desenho.
        /// </summary>
        public void LiberarImagens()
        {
            CardsVisiveis = 0;
            _idsVisiveis.Clear();
            _miniaturas.ManterSomente(Array.Empty<string>());
        }

        // ---- Layout e rolagem ------------------------------------------------------------

        private void RecalcularLayout()
        {
            // Largura EXTERNA (Width, não ClientSize.Width): ClientSize encolhe quando a
            // barra de rolagem aparece, e é essa dependência que faz a grade oscilar na
            // largura-limite. LarguraUtil reserva a barra sempre.
            _layout = new LayoutDaGrade(_tamanhoDoCard, LayoutDaGrade.LarguraUtil(Width), _jogos.Count);

            AutoScrollMinSize = new Size(0, _layout.AlturaTotal);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalcularLayout();
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();   // conteúdo desenhado à mão precisa de repintura inteira
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        private void GarantirSelecaoVisivel()
        {
            if (_selecionado < 0) return;

            var celula = _layout.Celula(_selecionado);
            var deslocamento = -AutoScrollPosition.Y;
            var altura = ClientSize.Height;

            if (celula.Top < deslocamento)
                AutoScrollPosition = new Point(0, Math.Max(0, celula.Top - _layout.Espacamento));
            else if (celula.Bottom > deslocamento + altura)
                AutoScrollPosition = new Point(0, Math.Max(0, celula.Bottom - altura + _layout.Espacamento));
        }

        /// <summary>Repinta um card só. É o que faz o hover não custar a grade inteira.</summary>
        private void InvalidarCard(int indice)
        {
            if (indice < 0 || indice >= _jogos.Count) return;

            var celula = _layout.Celula(indice);
            celula.Offset(0, AutoScrollPosition.Y);   // AutoScrollPosition.Y já vem negativo
            celula.Inflate(EspessuraDaSelecao, EspessuraDaSelecao);

            Invalidate(celula);
        }

        // ---- Desenho -----------------------------------------------------------------------

        /// <summary>
        /// As fontes e os pincéis de uma pintura inteira.
        ///
        /// Existe para não criar objeto GDI por card: com 24 cards na tela, a versão
        /// anterior fazia uma centena de <c>new Font</c>/<c>new SolidBrush</c> por frame
        /// só para escrever o mesmo título com a mesma cor.
        /// </summary>
        private sealed class TintaDaGrade : IDisposable
        {
            public readonly Font Titulo;
            public readonly Font Faixa;
            public readonly Font Estrela;
            public readonly SolidBrush Vazio;
            public readonly SolidBrush Sombra;
            public readonly SolidBrush Favorito;
            public readonly SolidBrush Fundo;
            public readonly Pen Selecao;
            public readonly Pen Hover;

            /// <summary>As quatro pontas do card, na origem. Ver <see cref="ArredondarCantos"/>.</summary>
            public readonly GraphicsPath Cantos;

            public TintaDaGrade(TamanhoCard tamanho, Size capa)
            {
                Titulo = new Font("Segoe UI", tamanho == TamanhoCard.P ? 8f : 8.75f);
                Faixa = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                Estrela = new Font("Segoe UI", 12f, FontStyle.Bold);
                Vazio = new SolidBrush(Tema.Superficie);
                Sombra = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                Favorito = new SolidBrush(Color.FromArgb(255, 214, 102));
                Fundo = new SolidBrush(Tema.Fundo);
                Selecao = new Pen(Tema.Selecao, EspessuraDaSelecao) { Alignment = PenAlignment.Inset };
                Hover = new Pen(Tema.BordaClara, EspessuraDoHover) { Alignment = PenAlignment.Inset };
                Cantos = MontarCantos(capa, RaioDoCard);
            }

            /// <summary>
            /// As quatro pontas quadradas que sobram fora do canto arredondado, como uma
            /// figura só. Todos os cards têm o mesmo tamanho, então esta geometria é
            /// montada uma vez e cada card só a translada.
            /// </summary>
            private static GraphicsPath MontarCantos(Size capa, int raio)
            {
                var caminho = new GraphicsPath();
                var d = raio * 2;
                var largura = capa.Width;
                var altura = capa.Height;

                caminho.AddArc(0, 0, d, d, 180, 90);
                caminho.AddLine(raio, 0, 0, 0);
                caminho.CloseFigure();

                caminho.StartFigure();
                caminho.AddArc(largura - d, 0, d, d, 270, 90);
                caminho.AddLine(largura, raio, largura, 0);
                caminho.CloseFigure();

                caminho.StartFigure();
                caminho.AddArc(largura - d, altura - d, d, d, 0, 90);
                caminho.AddLine(largura - raio, altura, largura, altura);
                caminho.CloseFigure();

                caminho.StartFigure();
                caminho.AddArc(0, altura - d, d, d, 90, 90);
                caminho.AddLine(0, altura - raio, 0, altura);
                caminho.CloseFigure();

                return caminho;
            }

            public void Dispose()
            {
                Titulo.Dispose();
                Faixa.Dispose();
                Estrela.Dispose();
                Vazio.Dispose();
                Sombra.Dispose();
                Favorito.Dispose();
                Fundo.Dispose();
                Selecao.Dispose();
                Hover.Dispose();
                Cantos.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            if (_jogos.Count == 0)
            {
                DesenharVazio(g);
                CardsVisiveis = 0;
                _miniaturas.ManterSomente(Array.Empty<string>());
                return;
            }

            var deslocamento = -AutoScrollPosition.Y;
            _layout.FaixaVisivel(deslocamento, ClientSize.Height, out var primeiro, out var ultimo);

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            _idsVisiveis.Clear();

            using (var tinta = new TintaDaGrade(_tamanhoDoCard, new Size(_layout.LarguraCard, _layout.AlturaCapa)))
            {
                for (var i = primeiro; i <= ultimo && i < _jogos.Count; i++)
                {
                    // O id entra na lista mesmo quando o card fica fora do clip: quem manda
                    // no que continua em memória é a faixa visível, não o pedaço repintado.
                    _idsVisiveis.Add(_jogos[i].Id);

                    var celula = _layout.Celula(i);
                    celula.Offset(0, -deslocamento);

                    if (!celula.IntersectsWith(e.ClipRectangle)) continue;

                    DesenharCard(g, tinta, _jogos[i], celula, i);
                }
            }

            CardsVisiveis = _idsVisiveis.Count;

            // Fim do desenho: tudo que não está aqui sai da memória.
            _miniaturas.ManterSomente(_idsVisiveis);
        }

        private void DesenharVazio(Graphics g)
        {
            var marca = new RectangleF(0, (ClientSize.Height / 2f) - 96, ClientSize.Width, 96);

            // O gamepad do ícone, bem apagado: a tela vazia continua sendo o launcher.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            IconeDoLauncher.Desenhar(g, marca, transparencia: 38);

            using (var fonte = new Font("Segoe UI", 10.5f))
            using (var pincel = new SolidBrush(Tema.TextoFraco))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("Nenhum jogo para mostrar.\r\nUse \"Escanear jogos\" (F6) ou limpe a busca.",
                    fonte, pincel, new RectangleF(0, marca.Bottom + 10, ClientSize.Width, 52), formato);
            }
        }

        private void DesenharCard(Graphics g, TintaDaGrade tinta, Jogo jogo, Rectangle celula, int indice)
        {
            var areaDaCapa = _layout.AreaDaCapa(celula);
            var imagem = _miniaturas.Obter(jogo);

            var selecionado = indice == _selecionado;
            var sobOMouse = indice == _sobOMouse;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (imagem != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(imagem, areaDaCapa);
            }
            else
            {
                // Placeholder enquanto a thread de carga não devolve a imagem.
                g.FillRectangle(tinta.Vazio, areaDaCapa);
            }

            // "Em execução" tem prioridade sobre "não encontrado": se está rodando,
            // saber que está rodando é o que importa.
            if (string.Equals(jogo.Id, _idEmExecucao, StringComparison.OrdinalIgnoreCase))
                DesenharFaixa(g, tinta, areaDaCapa, "EM EXECUÇÃO", Color.FromArgb(215, 24, 92, 62));
            else if (!jogo.ExecutavelExiste())
                DesenharFaixa(g, tinta, areaDaCapa, "NÃO ENCONTRADO", Color.FromArgb(215, 120, 40, 40));

            ArredondarCantos(g, tinta, areaDaCapa);

            if (selecionado || sobOMouse)
            {
                using (var caminho = Formas.Arredondado(areaDaCapa, RaioDoCard))
                    g.DrawPath(selecionado ? tinta.Selecao : tinta.Hover, caminho);
            }

            if (jogo.Favorito) DesenharEstrela(g, tinta, areaDaCapa);

            DesenharTitulo(g, tinta, jogo, _layout.AreaDoTitulo(celula), selecionado, sobOMouse);
        }

        /// <summary>
        /// Arredonda o card tapando as quatro pontas com a cor do fundo, depois de a capa
        /// já estar desenhada.
        ///
        /// O caminho natural seria recortar (SetClip com o retângulo arredondado), e foi
        /// assim que isto nasceu — mas um clip por card obriga o GDI+ a montar uma região
        /// por card, e o bench de memória acusou 2,4 MB de working set a mais por causa
        /// disso. Tapar a ponta é um FillPath de geometria já pronta: mesmo desenho, e o
        /// bench voltou ao número de antes do arredondamento.
        /// </summary>
        private static void ArredondarCantos(Graphics g, TintaDaGrade tinta, Rectangle areaDaCapa)
        {
            g.TranslateTransform(areaDaCapa.X, areaDaCapa.Y);
            g.FillPath(tinta.Fundo, tinta.Cantos);
            g.TranslateTransform(-areaDaCapa.X, -areaDaCapa.Y);
        }

        private static void DesenharFaixa(Graphics g, TintaDaGrade tinta, Rectangle areaDaCapa, string texto, Color cor)
        {
            var faixa = new Rectangle(areaDaCapa.X, areaDaCapa.Bottom - 22, areaDaCapa.Width, 22);

            using (var fundo = new SolidBrush(cor))
                g.FillRectangle(fundo, faixa);

            TextRenderer.DrawText(g, texto, tinta.Faixa, faixa, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private static void DesenharEstrela(Graphics g, TintaDaGrade tinta, Rectangle areaDaCapa)
        {
            var ponto = new PointF(areaDaCapa.Right - 25, areaDaCapa.Top + 3);

            g.DrawString("★", tinta.Estrela, tinta.Sombra, ponto.X + 1, ponto.Y + 1);
            g.DrawString("★", tinta.Estrela, tinta.Favorito, ponto.X, ponto.Y);
        }

        /// <summary>
        /// Título em uma linha só, com reticências. Duas linhas custavam altura reservada
        /// em TODOS os cards por causa dos poucos com nome comprido — o nome inteiro vai
        /// para a ToolTip do hover, onde não ocupa espaço nenhum da grade.
        /// </summary>
        private static void DesenharTitulo(Graphics g, TintaDaGrade tinta, Jogo jogo, Rectangle area,
                                           bool selecionado, bool sobOMouse)
        {
            var cor = selecionado ? Tema.TextoForte : sobOMouse ? Tema.Texto : Tema.TextoFraco;

            TextRenderer.DrawText(g, jogo.Titulo, tinta.Titulo, area, cor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        // ---- Entrada ------------------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            var indice = _layout.IndiceEm(e.Location, -AutoScrollPosition.Y);
            if (indice >= 0) Selecionar(indice);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ApontarPara(_layout.IndiceEm(e.Location, -AutoScrollPosition.Y));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            ApontarPara(-1);
        }

        /// <summary>
        /// Troca o card sob o mouse: repinta os dois cards envolvidos (nunca a grade
        /// inteira) e põe o título completo na ToolTip, que é o que salva o nome cortado.
        /// </summary>
        private void ApontarPara(int indice)
        {
            if (indice >= _jogos.Count) indice = -1;
            if (indice == _sobOMouse) return;

            InvalidarCard(_sobOMouse);
            _sobOMouse = indice;
            InvalidarCard(indice);

            if (indice < 0)
            {
                _dica?.Hide(this);
                return;
            }

            (_dica ??= CriarDica()).SetToolTip(this, _jogos[indice].Titulo);
        }

        private ToolTip CriarDica()
        {
            var dica = new ToolTip
            {
                OwnerDraw = true,
                InitialDelay = 450,
                ReshowDelay = 120,
                AutoPopDelay = 8000,
                BackColor = Tema.Superficie,
                ForeColor = Tema.TextoForte
            };

            // Sem isto a ToolTip aparece no amarelo do sistema no meio de uma janela preta.
            dica.Draw += (_, e) =>
            {
                using (var fundo = new SolidBrush(Tema.Superficie))
                    e.Graphics.FillRectangle(fundo, e.Bounds);

                using (var caneta = new Pen(Tema.Borda))
                    e.Graphics.DrawRectangle(caneta, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);

                TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Tema.TextoForte,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            return dica;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            var indice = _layout.IndiceEm(e.Location, -AutoScrollPosition.Y);
            if (indice >= 0)
            {
                Selecionar(indice);
                AcionarSelecionado();
            }
        }

        /// <summary>Setas não chegam no OnKeyDown de um Panel sem isto.</summary>
        protected override bool IsInputKey(Keys chave)
        {
            switch (chave)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Enter:
                    return true;
                default:
                    return base.IsInputKey(chave);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (TratarTecla(e.KeyCode)) e.Handled = true;
        }

        /// <summary>
        /// Navegação por teclado (e por controle, que manda as mesmas setas quando
        /// mapeado como teclado). Público para a janela poder repassar as teclas
        /// enquanto o foco está na busca.
        /// </summary>
        public bool TratarTecla(Keys chave)
        {
            var porPagina = Math.Max(1, ClientSize.Height / Math.Max(1, _layout.AlturaDaCelula)) * _layout.Colunas;

            switch (chave)
            {
                case Keys.Left: MoverSelecao(-1, 0); return true;
                case Keys.Right: MoverSelecao(+1, 0); return true;
                case Keys.Up: MoverSelecao(0, -1); return true;
                case Keys.Down: MoverSelecao(0, +1); return true;
                case Keys.PageUp: Selecionar(Math.Max(0, _selecionado - porPagina)); return true;
                case Keys.PageDown: Selecionar(_selecionado + porPagina); return true;
                case Keys.Home: Selecionar(0); return true;
                case Keys.End: Selecionar(_jogos.Count - 1); return true;
                case Keys.Enter: AcionarSelecionado(); return true;
                default: return false;
            }
        }

        // ---- Carga assíncrona -----------------------------------------------------------------

        private void AoFicarPronta(object? remetente, EventArgs e)
        {
            // Vem da thread de carga: só repinta, e sem travar quem chamou.
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); }));
            }
            catch (Exception)
            {
                // Janela fechando no meio da carga.
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _miniaturas.ImagemPronta -= AoFicarPronta;
                _dica?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
