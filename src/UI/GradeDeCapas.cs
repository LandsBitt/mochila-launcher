using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Mochila.Entrada;
using Mochila.Modelo;

namespace Mochila.UI
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

        /// <summary>
        /// Os cards marcados para uma ação em lote (fase 12), por id.
        ///
        /// Por id e não por índice: o índice muda a cada busca digitada, e uma marcação
        /// que escorrega de jogo é pior que marcação nenhuma. Ainda assim ela é apagada
        /// quando a lista visível troca — ver <see cref="DefinirJogos"/>.
        /// </summary>
        private readonly HashSet<string> _marcados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private LayoutDaGrade _layout;
        private TamanhoCard _tamanhoDoCard = TamanhoCard.M;
        private int _selecionado = -1;
        private int _sobOMouse = -1;
        private string? _idEmExecucao;

        /// <summary>Onde o Shift+setas começou a marcar. -1 quando não há série em curso.</summary>
        private int _ancora = -1;

        /// <summary>
        /// Quantos dos primeiros cards formam a seção "Continuar jogando" (fase 13).
        /// Zero significa lista única, que é o estado de sempre.
        /// </summary>
        private int _quantosNaPrimeiraSecao;

        private string _tituloDaPrimeiraSecao = "";
        private string _tituloDaSegundaSecao = "";

        /// <summary>Liga enquanto o Shift+setas mexe na seleção, para a âncora não se perder.</summary>
        private bool _estendendo;

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

        /// <summary>Enter no card (ou A no controle): abre o jogo.</summary>
        public event EventHandler<Jogo>? JogoAcionado;

        /// <summary>
        /// Duplo clique num card. Desde a fase 11 ele abre a tela de detalhes, e não o
        /// jogo: lançar é a ação cara (salva, esconde a janela, sobe um processo) e ficou
        /// só com Enter e A, que são deliberados. O duplo clique vira o caminho de olhar.
        /// </summary>
        public event EventHandler<Jogo>? DetalhesPedidos;

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
        /// O recado da tela vazia. Quem define é a janela, porque só ela sabe o motivo:
        /// acervo ainda não catalogado e filtro que não casou com nada são a mesma tela e
        /// pedem instruções opostas.
        /// </summary>
        public string TextoDeVazio { get; set; } =
            "Nenhum jogo para mostrar.\r\nUse \"Escanear jogos\" (F6) ou limpe a busca.";

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
        public void DefinirJogos(IEnumerable<Jogo> jogos) => DefinirJogos(jogos, 0, "", "");

        /// <summary>
        /// Troca a lista mostrada, com os <paramref name="quantosNaPrimeiraSecao"/> primeiros
        /// cards formando a seção "Continuar jogando" (fase 13).
        ///
        /// <b>Nenhum jogo aparece duas vezes.</b> Uma seção de destaque que repete cards
        /// abaixo pareceria natural e quebraria três coisas de uma vez: a marcação da fase
        /// 12 é por id (marcar a cópia marcaria as duas), a seleção reencontrada em
        /// <see cref="DefinirJogos"/> é por id (voltaria sempre para a primeira cópia) e o
        /// rodapé conta <c>Jogos.Count</c> como "quantos jogos estão à vista". Os cinco
        /// recentes saem da parte de baixo e sobem para a seção — a lista continua sendo
        /// uma permutação do filtro, não uma lista maior que ele.
        /// </summary>
        public void DefinirJogos(IEnumerable<Jogo> jogos, int quantosNaPrimeiraSecao,
                                 string tituloDaPrimeiraSecao, string tituloDaSegundaSecao)
        {
            var idSelecionado = JogoSelecionado?.Id;

            _jogos.Clear();
            _jogos.AddRange(jogos);

            _quantosNaPrimeiraSecao = Math.Max(0, Math.Min(_jogos.Count, quantosNaPrimeiraSecao));
            _tituloDaPrimeiraSecao = tituloDaPrimeiraSecao ?? "";
            _tituloDaSegundaSecao = tituloDaSegundaSecao ?? "";

            _selecionado = -1;
            _sobOMouse = -1;

            // Continua marcado só quem continua à vista. É regra, não economia: marcação
            // escondida seria um jogo fora do filtro atual sendo etiquetado porque eu o
            // marquei três buscas atrás. Preservar o que ficou visível é o que permite
            // aplicar duas tags seguidas na mesma seleção — a ação reaplica o filtro.
            ManterMarcacaoVisivel();

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

            if (!_estendendo) _ancora = -1;

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

        // ---- Multi-seleção (fase 12) ---------------------------------------------------------

        /// <summary>Quantos cards estão marcados agora.</summary>
        public int QuantidadeMarcada => _marcados.Count;

        public bool EstaMarcado(Jogo jogo) => _marcados.Contains(jogo.Id);

        /// <summary>
        /// Em quem uma ação em lote vai mexer: os marcados, ou — se não há marcação — só o
        /// card selecionado.
        ///
        /// É o que faz o mesmo menu servir para um jogo e para trinta, sem duas versões de
        /// cada item. A lista sai na ordem da grade, e só com jogos que estão à vista.
        /// </summary>
        public List<Jogo> SelecaoParaAcao()
        {
            var alvos = new List<Jogo>();

            if (_marcados.Count > 0)
            {
                foreach (var jogo in _jogos)
                {
                    if (_marcados.Contains(jogo.Id)) alvos.Add(jogo);
                }
                return alvos;
            }

            if (JogoSelecionado is { } selecionado) alvos.Add(selecionado);
            return alvos;
        }

        private void ManterMarcacaoVisivel()
        {
            _ancora = -1;
            if (_marcados.Count == 0) return;

            var visiveis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var jogo in _jogos) visiveis.Add(jogo.Id);

            _marcados.RemoveWhere(id => !visiveis.Contains(id));
        }

        public void LimparMarcacao()
        {
            if (_marcados.Count == 0) return;

            _marcados.Clear();
            _ancora = -1;
            Invalidate();

            MarcacaoMudou?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Marca ou desmarca um card (Ctrl+clique).</summary>
        public void AlternarMarcacao(int indice)
        {
            if (indice < 0 || indice >= _jogos.Count) return;

            var id = _jogos[indice].Id;
            if (!_marcados.Remove(id)) _marcados.Add(id);

            InvalidarCard(indice);
            MarcacaoMudou?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Ctrl+A: marca tudo que está à vista — e nada além disso.</summary>
        public void MarcarTodos()
        {
            if (_jogos.Count == 0) return;

            _marcados.Clear();
            foreach (var jogo in _jogos) _marcados.Add(jogo.Id);

            Invalidate();
            MarcacaoMudou?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Marcou ou desmarcou algo. A janela usa para atualizar o rodapé.</summary>
        public event EventHandler? MarcacaoMudou;

        /// <summary>
        /// Shift+setas e Ctrl+A. Ficam fora do <see cref="ComandoDeNavegacao"/> de
        /// propósito: a spec não põe multi-seleção no controle nesta fase, e inventar
        /// comando abstrato para algo que só o teclado faz seria vocabulário morto.
        /// </summary>
        public bool TratarTeclaDeSelecao(Keys chave, Keys modificadores)
        {
            if (modificadores == Keys.Control && chave == Keys.A)
            {
                MarcarTodos();
                return true;
            }

            if (modificadores != Keys.Shift) return false;

            switch (chave)
            {
                case Keys.Left: return EstenderSelecao(-1, 0);
                case Keys.Right: return EstenderSelecao(+1, 0);
                case Keys.Up: return EstenderSelecao(0, -1);
                case Keys.Down: return EstenderSelecao(0, +1);
                default: return false;
            }
        }

        private bool EstenderSelecao(int colunas, int linhas)
        {
            if (_jogos.Count == 0) return true;
            if (_ancora < 0) _ancora = Math.Max(0, _selecionado);

            _estendendo = true;
            try
            {
                Selecionar(_layout.Mover(_selecionado, colunas, linhas));
            }
            finally
            {
                _estendendo = false;
            }

            MarcarIntervalo(_ancora, _selecionado);
            return true;
        }

        /// <summary>
        /// A faixa entre a âncora e a seleção vira a marcação inteira. Substituir em vez de
        /// somar é o que permite encolher a faixa voltando a seta.
        /// </summary>
        private void MarcarIntervalo(int de, int ate)
        {
            _marcados.Clear();

            var inicio = Math.Max(0, Math.Min(de, ate));
            var fim = Math.Min(_jogos.Count - 1, Math.Max(de, ate));

            for (var i = inicio; i <= fim; i++) _marcados.Add(_jogos[i].Id);

            Invalidate();
            MarcacaoMudou?.Invoke(this, EventArgs.Empty);
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
            _layout = new LayoutDaGrade(_tamanhoDoCard, LayoutDaGrade.LarguraUtil(Width), _jogos.Count,
                                        _quantosNaPrimeiraSecao);

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

            // Subir traz o cabeçalho da seção junto — ver LayoutDaGrade.TopoParaRolar.
            if (celula.Top < deslocamento)
                AutoScrollPosition = new Point(0, Math.Max(0, _layout.TopoParaRolar(_selecionado) - _layout.Espacamento));
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
            public readonly SolidBrush Marcado;
            public readonly SolidBrush Acento;
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
                Marcado = new SolidBrush(Color.FromArgb(64, Tema.Acento));
                Acento = new SolidBrush(Tema.Acento);
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
                Marcado.Dispose();
                Acento.Dispose();
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
                DesenharCabecalhos(g, deslocamento, e.ClipRectangle);

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

        /// <summary>
        /// Os títulos das duas seções da fase 13. São dois textos por pintura, não um por
        /// card: o custo é irrelevante e por isso eles não passam pela faixa visível — o
        /// que decide se desenham é o clip, como qualquer outro pedaço da tela.
        /// </summary>
        private void DesenharCabecalhos(Graphics g, int deslocamento, Rectangle clip)
        {
            if (!_layout.TemSecoes) return;

            using (var fonte = new Font("Segoe UI", 9.75f, FontStyle.Bold))
            using (var caneta = new Pen(Tema.Borda))
            {
                for (var secao = 0; secao < _layout.Secoes; secao++)
                {
                    var titulo = secao == 0 ? _tituloDaPrimeiraSecao : _tituloDaSegundaSecao;
                    if (titulo.Length == 0) continue;

                    var area = _layout.AreaDoCabecalho(secao);
                    if (area.IsEmpty) continue;

                    area.Offset(0, -deslocamento);
                    if (!area.IntersectsWith(clip)) continue;

                    var texto = new Rectangle(area.X, area.Y, area.Width, area.Height - 8);
                    TextRenderer.DrawText(g, titulo.ToUpperInvariant(), fonte, texto, Tema.TextoFraco,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.EndEllipsis);

                    var largura = TextRenderer.MeasureText(g, titulo.ToUpperInvariant(), fonte,
                        new Size(int.MaxValue, area.Height), TextFormatFlags.NoPadding).Width;

                    // A linha continua o título até a borda da grade: separa as seções sem
                    // precisar de fundo, faixa nem outro degrau de cor.
                    var y = texto.Y + (texto.Height / 2);
                    if (largura + 12 < area.Width)
                        g.DrawLine(caneta, area.X + largura + 10, y, area.Right, y);
                }
            }
        }

        private void DesenharVazio(Graphics g)
        {
            var marca = new RectangleF(0, (ClientSize.Height / 2f) - 96, ClientSize.Width, 96);

            // O gamepad do ícone, bem apagado: a tela vazia continua sendo o launcher.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            IconeDaMochila.Desenhar(g, marca, transparencia: 38);

            using (var fonte = new Font("Segoe UI", 10.5f))
            using (var pincel = new SolidBrush(Tema.TextoFraco))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(TextoDeVazio, fonte, pincel,
                    new RectangleF(0, marca.Bottom + 10, ClientSize.Width, 52), formato);
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

            // Véu na cor do acento por cima da capa: o card marcado precisa se distinguir de longe,
            // com a grade cheia, sem depender de um detalhe de um canto só.
            var marcado = _marcados.Contains(jogo.Id);
            if (marcado) g.FillRectangle(tinta.Marcado, areaDaCapa);

            ArredondarCantos(g, tinta, areaDaCapa);

            if (selecionado || marcado || sobOMouse)
            {
                using (var caminho = Formas.Arredondado(areaDaCapa, RaioDoCard))
                    g.DrawPath(selecionado || marcado ? tinta.Selecao : tinta.Hover, caminho);
            }

            if (marcado) DesenharMarca(g, tinta, areaDaCapa);
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

        /// <summary>O selo de "este vai junto na próxima ação em lote".</summary>
        private static void DesenharMarca(Graphics g, TintaDaGrade tinta, Rectangle areaDaCapa)
        {
            var selo = new Rectangle(areaDaCapa.X + 6, areaDaCapa.Y + 6, 20, 20);

            g.FillEllipse(tinta.Sombra, selo.X + 1, selo.Y + 1, selo.Width, selo.Height);
            g.FillEllipse(tinta.Acento, selo);

            using (var caneta = new Pen(Tema.Fundo, 2f))
            {
                g.DrawLine(caneta, selo.X + 5, selo.Y + 10, selo.X + 9, selo.Y + 14);
                g.DrawLine(caneta, selo.X + 9, selo.Y + 14, selo.X + 15, selo.Y + 6);
            }
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
            if (indice < 0) return;

            var comControle = (ModifierKeys & Keys.Control) == Keys.Control;

            // Clicar fora da marcação desfaz a marcação — é o que o Explorer faz e o que
            // impede um menu de contexto de agir sobre trinta jogos que eu já esqueci que
            // tinha marcado.
            if (!comControle && !_marcados.Contains(_jogos[indice].Id)) LimparMarcacao();

            Selecionar(indice);

            if (comControle)
            {
                AlternarMarcacao(indice);
                _ancora = indice;
            }
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
            if (indice < 0) return;

            Selecionar(indice);
            PedirDetalhes();
        }

        /// <summary>
        /// O que o duplo clique faz. Existe como método, e não solto dentro do evento do
        /// mouse, para o teste conseguir provar o que mudou na fase 11: isto pede a tela de
        /// detalhes e <b>não</b> lança o jogo.
        /// </summary>
        public void PedirDetalhes()
        {
            if (JogoSelecionado is { } jogo) DetalhesPedidos?.Invoke(this, jogo);
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
        /// Navegação por teclado. Traduz a tecla e entrega a <see cref="TratarComando"/>:
        /// desde a fase 9, o trabalho de verdade acontece em cima de comando, não de tecla.
        /// Continua público porque a janela repassa as teclas enquanto o foco está na busca.
        /// </summary>
        public bool TratarTecla(Keys chave) => TratarComando(RoteadorDeTeclas.Comando(chave));

        /// <summary>
        /// Navegação, dita no único vocabulário que a grade conhece. Teclado e gamepad
        /// chegam aqui pelo mesmo caminho — é isso que impede as duas formas de navegar
        /// de sairem de sincronia quando uma delas ganha algo novo.
        /// </summary>
        public bool TratarComando(ComandoDeNavegacao comando)
        {
            var porPagina = Math.Max(1, ClientSize.Height / Math.Max(1, _layout.AlturaDaCelula)) * _layout.Colunas;

            switch (comando)
            {
                case ComandoDeNavegacao.Esquerda: MoverSelecao(-1, 0); return true;
                case ComandoDeNavegacao.Direita: MoverSelecao(+1, 0); return true;
                case ComandoDeNavegacao.Cima: MoverSelecao(0, -1); return true;
                case ComandoDeNavegacao.Baixo: MoverSelecao(0, +1); return true;
                case ComandoDeNavegacao.PaginaAnterior: Selecionar(Math.Max(0, _selecionado - porPagina)); return true;
                case ComandoDeNavegacao.PaginaSeguinte: Selecionar(_selecionado + porPagina); return true;
                case ComandoDeNavegacao.Primeiro: Selecionar(0); return true;
                case ComandoDeNavegacao.Ultimo: Selecionar(_jogos.Count - 1); return true;
                case ComandoDeNavegacao.Confirmar: AcionarSelecionado(); return true;
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
