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
    /// </summary>
    public sealed class GradeDeCapas : Panel
    {
        private const int RaioDoCard = 6;
        private const int EspessuraDaSelecao = 3;

        private readonly CacheDeMiniaturas _miniaturas;
        private readonly List<Jogo> _jogos = new List<Jogo>();
        private readonly HashSet<string> _idsVisiveis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private LayoutDaGrade _layout;
        private TamanhoCard _tamanhoDoCard = TamanhoCard.M;
        private int _selecionado = -1;
        private string? _idEmExecucao;

        public GradeDeCapas(CacheDeMiniaturas miniaturas)
        {
            _miniaturas = miniaturas ?? throw new ArgumentNullException(nameof(miniaturas));

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Cores.Fundo;
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

            _selecionado = novo;
            GarantirSelecaoVisivel();
            Invalidate();

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

        // ---- Desenho -----------------------------------------------------------------------

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

            for (var i = primeiro; i <= ultimo && i < _jogos.Count; i++)
            {
                var celula = _layout.Celula(i);
                celula.Offset(0, -deslocamento);

                DesenharCard(g, _jogos[i], celula, i == _selecionado);
                _idsVisiveis.Add(_jogos[i].Id);
            }

            CardsVisiveis = _idsVisiveis.Count;

            // Fim do desenho: tudo que não está aqui sai da memória.
            _miniaturas.ManterSomente(_idsVisiveis);
        }

        private void DesenharVazio(Graphics g)
        {
            using (var fonte = new Font("Segoe UI", 11f))
            using (var pincel = new SolidBrush(Cores.TextoFraco))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("Nenhum jogo para mostrar.\r\nUse \"Escanear jogos\" (F6) ou limpe a busca.",
                    fonte, pincel, new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), formato);
            }
        }

        private void DesenharCard(Graphics g, Jogo jogo, Rectangle celula, bool selecionado)
        {
            var areaDaCapa = _layout.AreaDaCapa(celula);
            var imagem = _miniaturas.Obter(jogo);

            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var caminho = Arredondado(areaDaCapa, RaioDoCard))
            {
                if (imagem != null)
                {
                    var estado = g.Save();
                    g.SetClip(caminho);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(imagem, areaDaCapa);
                    g.Restore(estado);
                }
                else
                {
                    // Placeholder cinza enquanto a thread de carga não devolve a imagem.
                    using (var pincel = new SolidBrush(Cores.FundoControle))
                        g.FillPath(pincel, caminho);
                }

                // "Em execução" tem prioridade sobre "não encontrado": se está rodando,
                // saber que está rodando é o que importa.
                if (string.Equals(jogo.Id, _idEmExecucao, StringComparison.OrdinalIgnoreCase))
                    DesenharFaixa(g, areaDaCapa, "em execução", Color.FromArgb(200, 40, 90, 60));
                else if (!jogo.ExecutavelExiste())
                    DesenharFaixa(g, areaDaCapa, "não encontrado", Color.FromArgb(200, 120, 40, 40));

                if (selecionado)
                {
                    using (var caneta = new Pen(Cores.Selecao, EspessuraDaSelecao))
                    {
                        caneta.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                        g.DrawPath(caneta, caminho);
                    }
                }
            }

            if (jogo.Favorito) DesenharEstrela(g, areaDaCapa);

            DesenharTitulo(g, jogo, _layout.AreaDoTitulo(celula), selecionado);
        }

        private static void DesenharFaixa(Graphics g, Rectangle areaDaCapa, string texto, Color cor)
        {
            var faixa = new Rectangle(areaDaCapa.X, areaDaCapa.Bottom - 26, areaDaCapa.Width, 26);

            using (var fundo = new SolidBrush(cor))
            using (var fonte = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var pincel = new SolidBrush(Color.White))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.FillRectangle(fundo, faixa);
                g.DrawString(texto, fonte, pincel, faixa, formato);
            }
        }

        private static void DesenharEstrela(Graphics g, Rectangle areaDaCapa)
        {
            using (var fonte = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var sombra = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (var pincel = new SolidBrush(Color.FromArgb(255, 214, 102)))
            {
                var ponto = new PointF(areaDaCapa.Right - 26, areaDaCapa.Top + 4);
                g.DrawString("★", fonte, sombra, ponto.X + 1, ponto.Y + 1);
                g.DrawString("★", fonte, pincel, ponto.X, ponto.Y);
            }
        }

        private void DesenharTitulo(Graphics g, Jogo jogo, Rectangle area, bool selecionado)
        {
            using (var fonte = new Font("Segoe UI", _tamanhoDoCard == TamanhoCard.P ? 8f : 9f))
            using (var pincel = new SolidBrush(selecionado ? Cores.Texto : Cores.TextoFraco))
            using (var formato = new StringFormat
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Near,
                       Trimming = StringTrimming.EllipsisCharacter
                   })
            {
                var caixa = new RectangleF(area.X, area.Y + 5, area.Width, area.Height - 5);
                g.DrawString(jogo.Titulo, fonte, pincel, caixa, formato);
            }
        }

        private static GraphicsPath Arredondado(Rectangle area, int raio)
        {
            var caminho = new GraphicsPath();
            var d = raio * 2;

            caminho.AddArc(area.X, area.Y, d, d, 180, 90);
            caminho.AddArc(area.Right - d, area.Y, d, d, 270, 90);
            caminho.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
            caminho.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
            caminho.CloseFigure();

            return caminho;
        }

        // ---- Entrada ------------------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            var indice = _layout.IndiceEm(e.Location, -AutoScrollPosition.Y);
            if (indice >= 0) Selecionar(indice);
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
            if (disposing) _miniaturas.ImagemPronta -= AoFicarPronta;
            base.Dispose(disposing);
        }
    }
}
