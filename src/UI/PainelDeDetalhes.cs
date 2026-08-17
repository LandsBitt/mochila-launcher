using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using Mochila.Modelo;
using Mochila.Util;

namespace Mochila.UI
{
    /// <summary>
    /// A tela de detalhes de um jogo: arte grande à esquerda, ficha à direita.
    ///
    /// <b>Não é um Form em cima da janela</b>, e isso é regra da spec, não estilo: uma
    /// janela por jogo é um PictureBox por outro nome, e a memória do launcher é medida
    /// justamente por não fazer isso. Aqui é um painel desenhado à mão sobre a grade, com
    /// exatamente UMA imagem viva — a arte do jogo aberto —, liberada no
    /// <see cref="Fechar"/>. Abrir e fechar cinquenta vezes tem que devolver o working set
    /// ao mesmo lugar; é o que o <c>--bench-detalhes</c> mede.
    ///
    /// Os únicos controles de verdade aqui são o campo de argumentos e os botões: texto
    /// editável e clique com foco são coisas que desenhar à mão custaria mais do que vale.
    /// </summary>
    public sealed class PainelDeDetalhes : Panel
    {
        private const int Margem = 28;
        private const int AlturaDoBotao = 30;
        private const int VaoDoBotao = 8;

        /// <summary>Faixa da linha de ajuda no rodapé do painel.</summary>
        private const int AlturaDaDica = 26;

        private readonly IContextoDoLauncher _contexto;
        private readonly AcoesDoJogo _acoes;
        private readonly AcoesEmLote _lote;

        private readonly CampoDeTexto _argumentos;
        private readonly Label _rotuloDosArgumentos;
        private readonly List<Button> _botoes = new List<Button>();

        /// <summary>
        /// As áreas clicáveis desenhadas à mão (estrelas da nota, chips de status e de
        /// tag), montadas durante a pintura.
        ///
        /// Desenhar em vez de instanciar controle é a mesma decisão da grade: um jogo com
        /// oito tags viraria oito janelas nativas criadas e destruídas a cada abertura.
        /// </summary>
        private readonly List<Zona> _zonas = new List<Zona>();

        private int _zonaSobOMouse = -1;

        /// <summary>Até onde <see cref="DesenharChipsPendentes"/> já pintou nesta pintura.</summary>
        private int _zonasDesenhadas;

        /// <summary>Uma área clicável do desenho: onde fica, o que escreve e o que faz.</summary>
        private sealed class Zona
        {
            public Rectangle Area;
            public string Texto = "";
            public bool Ativa;
            public Action Acao = () => { };
        }

        private Jogo? _jogo;

        /// <summary>
        /// As últimas sessões do jogo aberto e o total do mês (fase 13).
        ///
        /// Calculados no <see cref="Abrir"/> e guardados: agregar o histórico a cada
        /// pintura seria varrer milhares de registros por hover do mouse.
        /// </summary>
        private readonly List<Sessao> _ultimasSessoes = new List<Sessao>();

        private int _segundosNoMes;
        private int _sessoesDoJogo;

        /// <summary>
        /// A arte em resolução cheia. É a única imagem que este painel segura, e ela morre
        /// no <see cref="Fechar"/>.
        /// </summary>
        private Bitmap? _arte;

        private Rectangle _areaDaArte;

        public PainelDeDetalhes(IContextoDoLauncher contexto, AcoesDoJogo acoes, AcoesEmLote lote)
        {
            _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
            _acoes = acoes ?? throw new ArgumentNullException(nameof(acoes));
            _lote = lote ?? throw new ArgumentNullException(nameof(lote));

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);

            DoubleBuffered = true;
            BackColor = Tema.Fundo;
            Visible = false;
            TabStop = true;

            _rotuloDosArgumentos = new Label
            {
                Text = "Argumentos de linha de comando",
                ForeColor = Tema.TextoFraco,
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 18
            };

            _argumentos = new CampoDeTexto { Height = 24 };
            _argumentos.Leave += (_, _) => GravarArgumentos();

            // A moldura do campo é desenhada por este painel (mesma ideia da barra
            // superior), então a cor da borda só muda se alguém repintar.
            _argumentos.GotFocus += (_, _) => Invalidate(MolduraDoCampo());
            _argumentos.LostFocus += (_, _) => Invalidate(MolduraDoCampo());

            Controls.Add(_rotuloDosArgumentos);
            Controls.Add(_argumentos);

            MontarBotoes();
        }

        /// <summary>Cliquei em "Jogar". Quem esconde a janela é quem tem janela.</summary>
        public event EventHandler<Jogo>? JogoPedido;

        /// <summary>O jogo mostrado agora, ou null com o painel fechado.</summary>
        public Jogo? JogoAtual => _jogo;

        /// <summary>
        /// true quando o foco está no campo de argumentos. A janela consulta isto antes de
        /// tratar tecla: com o cursor no campo, "i" é a letra i, não "abrir detalhes".
        /// </summary>
        public bool EditandoTexto => _argumentos.Focused;

        /// <summary>
        /// Se a arte grande está viva na memória agora. É a única coisa que este painel
        /// segura, e o teste da fase 11 é literalmente "depois de fechar, isto é false".
        /// </summary>
        internal bool ArteCarregadaParaDiagnostico => _arte != null;

        /// <summary>Quantas sessões a ficha carregou do histórico. Só diagnóstico.</summary>
        internal int SessoesCarregadasParaDiagnostico => _ultimasSessoes.Count;

        /// <summary>Total do mês mostrado na ficha, em segundos. Só diagnóstico.</summary>
        internal int SegundosNoMesParaDiagnostico => _segundosNoMes;

        /// <summary>Digita no campo de argumentos e sai dele, como eu faria com o mouse.</summary>
        internal void EditarArgumentosParaDiagnostico(string texto)
        {
            _argumentos.Text = texto;
            GravarArgumentos();
        }

        // ---- Abrir e fechar ------------------------------------------------------------------

        public void Abrir(Jogo jogo)
        {
            if (jogo is null) return;

            _jogo = jogo;
            _argumentos.Text = jogo.Argumentos ?? "";

            RecarregarHistorico();
            CarregarArte();
            PosicionarTudo();

            Visible = true;
            BringToFront();
            Focus();
            Invalidate();
        }

        public void Fechar()
        {
            if (!Visible && _arte is null) return;

            GravarArgumentos();

            Visible = false;
            _jogo = null;
            _ultimasSessoes.Clear();
            _segundosNoMes = 0;
            _sessoesDoJogo = 0;

            // O motivo de o painel existir em vez de um Form: aqui dá para garantir, numa
            // linha, que nada da tela anterior continua na memória.
            LiberarArte();
        }

        /// <summary>
        /// Relê as sessões do jogo aberto. É aqui que <c>sessoes.json</c> chega ao disco
        /// pela primeira vez numa execução — e só porque eu abri esta tela.
        /// </summary>
        private void RecarregarHistorico()
        {
            _ultimasSessoes.Clear();
            _segundosNoMes = 0;
            _sessoesDoJogo = 0;

            if (_jogo is not { } jogo) return;

            try
            {
                var estatisticas = new EstatisticasDeSessoes(_contexto.Sessoes(), _contexto.Biblioteca);

                _ultimasSessoes.AddRange(estatisticas.UltimasDoJogo(jogo.Id, MaximoDeSessoes));
                _segundosNoMes = estatisticas.SegundosDoJogoNoMes(jogo.Id, DateTime.Now);
                _sessoesDoJogo = estatisticas.SessoesDoJogo(jogo.Id);
            }
            catch (Exception)
            {
                // Histórico é acessório: a ficha do jogo abre sem ele.
            }
        }

        /// <summary>Quantas sessões a tela mostra. A spec pede as 10 últimas.</summary>
        private const int MaximoDeSessoes = 10;

        /// <summary>
        /// Relê o jogo do zero (capa trocada, título editado). Fechado, não faz nada — é
        /// chamada pelo mesmo caminho que redesenha a grade.
        /// </summary>
        public void Atualizar()
        {
            if (!Visible || _jogo is null) return;

            CarregarArte();
            Invalidate();
        }

        private void CarregarArte()
        {
            LiberarArte();
            if (_jogo is null) return;

            var capa = _jogo.CaminhoCapa();

            try
            {
                if (!string.IsNullOrEmpty(capa) && File.Exists(capa))
                {
                    _arte = GeradorDeCapa.AbrirSemTravarArquivo(capa!);
                    return;
                }
            }
            catch (Exception)
            {
                // Capa ilegível cai no card desenhado, igual à grade.
            }

            // Sem capa, o mesmo card gerado da grade — em tamanho de tela de detalhes.
            _arte = GeradorDeCapa.Gerar(_jogo.Titulo, 400, (int)Math.Round(400 * LayoutDaGrade.ProporcaoDaCapa));
        }

        private void LiberarArte()
        {
            _arte?.Dispose();
            _arte = null;
        }

        // ---- Botões e layout -----------------------------------------------------------------

        private void MontarBotoes()
        {
            // "Jogar" existe porque o duplo clique agora abre esta tela: sem ele, o mouse
            // perderia o caminho de lançar o jogo que acabou de abrir.
            var jogar = Botoes.CriarPrincipal("Jogar", Point.Empty, 88);
            jogar.Click += (_, _) => { if (_jogo is { } jogo) JogoPedido?.Invoke(this, jogo); };

            var doArquivo = Botoes.Criar("Capa do arquivo...", Point.Empty, 138);
            doArquivo.Click += (_, _) => _acoes.EscolherCapaDeArquivo();

            var online = Botoes.Criar("Buscar online...", Point.Empty, 126);
            online.Click += (_, _) => _acoes.BuscarCapaOnline();

            var colar = Botoes.Criar("Colar capa", Point.Empty, 96);
            colar.Click += (_, _) => _acoes.ColarCapa();

            var daPasta = Botoes.Criar("Arte da pasta", Point.Empty, 112);
            daPasta.Click += (_, _) => _acoes.UsarArteLocal();

            var remover = Botoes.Criar("Remover capa", Point.Empty, 112);
            remover.Click += (_, _) => _acoes.RemoverCapa();

            _botoes.AddRange(new[] { jogar, doArquivo, online, colar, daPasta, remover });

            foreach (var botao in _botoes)
            {
                botao.Height = AlturaDoBotao;
                Controls.Add(botao);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Visible) PosicionarTudo();
        }

        /// <summary>
        /// A arte manda no layout: ela ocupa a altura útil inteira, respeitando 2:3, e
        /// nunca mais que um terço da largura — numa janela larga, uma capa gigante
        /// deixaria a ficha espremida num canto.
        /// </summary>
        private void PosicionarTudo()
        {
            var alturaUtil = Math.Max(120, ClientSize.Height - (2 * Margem) - AlturaDaDica);
            var largura = (int)Math.Round(alturaUtil / LayoutDaGrade.ProporcaoDaCapa);

            var tetoDeLargura = Math.Max(90, (int)(ClientSize.Width * 0.32));
            if (largura > tetoDeLargura)
            {
                largura = tetoDeLargura;
                alturaUtil = (int)Math.Round(largura * LayoutDaGrade.ProporcaoDaCapa);
            }

            _areaDaArte = new Rectangle(Margem, Margem, largura, alturaUtil);

            var x = _areaDaArte.Right + 32;
            var larguraDaColuna = Math.Max(160, ClientSize.Width - x - Margem);

            var topoDosBotoes = PosicionarBotoes(x, larguraDaColuna, _areaDaArte.Bottom);

            _argumentos.SetBounds(x + 8, topoDosBotoes - 46, Math.Max(120, larguraDaColuna - 16), 24);
            _rotuloDosArgumentos.SetBounds(x, topoDosBotoes - 70, larguraDaColuna, 18);
        }

        /// <summary>
        /// Distribui os botões em linhas dentro da largura disponível e os encosta no pé da
        /// arte. Devolve o y da primeira linha, que é onde o campo de argumentos termina.
        /// </summary>
        private int PosicionarBotoes(int x, int largura, int baseY)
        {
            var linhas = new List<List<Button>>();
            var linha = new List<Button>();
            var usado = 0;

            foreach (var botao in _botoes)
            {
                if (linha.Count > 0 && usado + VaoDoBotao + botao.Width > largura)
                {
                    linhas.Add(linha);
                    linha = new List<Button>();
                    usado = 0;
                }

                usado += (linha.Count > 0 ? VaoDoBotao : 0) + botao.Width;
                linha.Add(botao);
            }
            if (linha.Count > 0) linhas.Add(linha);

            var altura = (linhas.Count * AlturaDoBotao) + ((linhas.Count - 1) * VaoDoBotao);
            var y = baseY - altura;
            var primeira = y;

            foreach (var atual in linhas)
            {
                var cursor = x;
                foreach (var botao in atual)
                {
                    botao.Location = new Point(cursor, y);
                    cursor += botao.Width + VaoDoBotao;
                }
                y += AlturaDoBotao + VaoDoBotao;
            }

            return primeira;
        }

        // ---- Desenho -------------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.Fundo);

            if (_jogo is not { } jogo) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DesenharArte(g);
            DesenharMolduraDoCampo(g);

            var x = _areaDaArte.Right + 32;
            var larguraTotal = Math.Max(160, ClientSize.Width - x - Margem);

            // A lista de sessões (fase 13) mora numa segunda coluna, e só quando existe
            // largura para ela. Embaixo da ficha não caberia: aquele espaço já é dos botões
            // de capa e do campo de argumentos, que se encostam no pé da arte. Numa janela
            // estreita a lista some e o "este mês" continua — o número que eu leio primeiro
            // não pode depender do tamanho da janela.
            var mostrarSessoes = _ultimasSessoes.Count > 0 &&
                                 larguraTotal >= LarguraMinimaParaSessoes;

            var largura = mostrarSessoes ? larguraTotal - LarguraDaColunaDeSessoes - 28 : larguraTotal;

            using (var titulo = new Font("Segoe UI", 19f, FontStyle.Bold))
            using (var rotulo = new Font("Segoe UI", 8.5f))
            using (var valor = new Font("Segoe UI", 9.75f))
            {
                var area = new Rectangle(x, Margem - 4, largura, 44);
                TextRenderer.DrawText(g, jogo.Titulo, titulo, area, Tema.TextoForte,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding);

                using (var caneta = new Pen(Tema.Borda))
                    g.DrawLine(caneta, x, Margem + 46, x + largura, Margem + 46);

                var y = Margem + 60;
                y = Ficha(g, rotulo, valor, x, y, largura, "Tempo jogado",
                          TempoDeJogo.Descrever(jogo.SegundosJogados), Tema.TextoForte);

                y = Ficha(g, rotulo, valor, x, y, largura, "Última vez jogado",
                          jogo.UltimaVezJogado is { } data
                              ? data.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm")
                              : "nunca",
                          Tema.Texto);

                // Total do mês (fase 13). Sai do histórico, então um jogo de biblioteca
                // migrada mostra "—" aqui e o tempo total intacto acima: são coisas
                // diferentes, e a tela não finge que a segunda vale pela primeira.
                y = Ficha(g, rotulo, valor, x, y, largura, "Jogado este mês",
                          _segundosNoMes > 0 ? TempoDeJogo.Descrever(_segundosNoMes) : "—",
                          _segundosNoMes > 0 ? Tema.Texto : Tema.TextoFraco);

                var existe = jogo.ExecutavelExiste();
                y = Ficha(g, rotulo, valor, x, y, largura, "Executável",
                          jogo.ExecutavelRelativo + (existe ? "" : "   [NÃO ENCONTRADO]"),
                          existe ? Tema.Texto : Tema.Erro);

                // Nota, status e tags (fase 12). Ficam depois do que a fase 11 já mostrava
                // porque o que eu leio primeiro é "quanto tempo joguei", não "que nota dei".
                _zonas.Clear();
                _zonasDesenhadas = 0;

                y = DesenharNota(g, rotulo, jogo, x, y, largura);
                y = DesenharStatus(g, rotulo, jogo, x, y, largura);
                DesenharTags(g, rotulo, jogo, x, y, largura);

                if (mostrarSessoes)
                {
                    DesenharSessoes(g, rotulo, valor,
                                    x + largura + 28, Margem + 60, LarguraDaColunaDeSessoes);
                }
            }

            DesenharDica(g);
        }

        // ---- Últimas sessões (fase 13) -----------------------------------------------------

        /// <summary>Largura da coluna de sessões, e a partir de quanto ela cabe.</summary>
        private const int LarguraDaColunaDeSessoes = 210;

        private const int LarguraMinimaParaSessoes = 560;

        private const int AlturaDaLinhaDeSessao = 19;

        /// <summary>
        /// As últimas sessões, uma por linha: data e duração.
        ///
        /// Desenha só quantas couberem até o rótulo dos argumentos — a lista encolhe com a
        /// janela em vez de passar por cima do campo. Quando sobra corte, a última linha
        /// diz quantas ficaram de fora, senão a tela mentiria por omissão.
        /// </summary>
        private void DesenharSessoes(Graphics g, Font rotulo, Font valor, int x, int y, int largura)
        {
            Rotulo(g, rotulo, "Últimas sessões", x, y, largura);

            var topo = y + 20;
            var limite = Math.Max(topo, _rotuloDosArgumentos.Top - 12);
            var cabem = Math.Max(0, (limite - topo) / AlturaDaLinhaDeSessao);

            if (cabem == 0) return;

            var mostradas = Math.Min(_ultimasSessoes.Count, cabem);

            // Sobrando uma linha só para o resumo, vale mais mostrar o resumo do que a
            // última sessão da lista.
            var temResto = _sessoesDoJogo > mostradas;
            if (temResto && mostradas == cabem && mostradas > 0) mostradas--;

            for (var i = 0; i < mostradas; i++)
            {
                var sessao = _ultimasSessoes[i];
                var linha = new Rectangle(x, topo + (i * AlturaDaLinhaDeSessao), largura, AlturaDaLinhaDeSessao);

                TextRenderer.DrawText(g, sessao.InicioLocal.ToString("dd/MM/yyyy HH:mm"), valor,
                    linha, Tema.Texto,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, TempoDeJogo.DescreverCurto(sessao.Segundos), valor,
                    linha, Tema.TextoForte,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (!temResto) return;

            var restantes = _sessoesDoJogo - mostradas;
            TextRenderer.DrawText(g,
                $"+ {restantes.ToString(System.Globalization.CultureInfo.InvariantCulture)} sessão(ões) — F9",
                rotulo,
                new Rectangle(x, topo + (mostradas * AlturaDaLinhaDeSessao), largura, AlturaDaLinhaDeSessao),
                Tema.TextoFraco,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ---- Nota, status e tags (fase 12) -----------------------------------------------------

        private const int AlturaDoChip = 22;
        private const int VaoDoChip = 6;
        private const int RecuoDoChip = 10;

        private int DesenharNota(Graphics g, Font rotulo, Jogo jogo, int x, int y, int largura)
        {
            Rotulo(g, rotulo, "Nota", x, y, largura);

            const int Lado = 24;

            using (var fonte = new Font("Segoe UI", 14f))
            {
                for (var estrela = 1; estrela <= 5; estrela++)
                {
                    var area = new Rectangle(x + ((estrela - 1) * Lado), y + 14, Lado, Lado);
                    var cheia = estrela <= jogo.Nota;
                    var valor = estrela;

                    var zona = new Zona
                    {
                        Area = area,
                        Ativa = cheia,
                        // Clicar na estrela que já está acesa apaga a nota: é o único jeito
                        // de voltar para "sem nota" sem um botão só para isso.
                        Acao = () => _lote.DefinirNota(Um(jogo), jogo.Nota == valor ? 0 : valor)
                    };
                    _zonas.Add(zona);

                    var cor = cheia ? Color.FromArgb(255, 214, 102)
                        : _zonaSobOMouse == _zonas.Count - 1 ? Tema.Texto
                        : Tema.Borda;

                    TextRenderer.DrawText(g, cheia ? "★" : "☆", fonte, area, cor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }

            return y + 46;
        }

        private int DesenharStatus(Graphics g, Font rotulo, Jogo jogo, int x, int y, int largura)
        {
            Rotulo(g, rotulo, "Status", x, y, largura);

            var cursor = x;

            foreach (var opcao in Estados.Todos)
            {
                var escolhido = opcao;
                var texto = Estados.Descrever(escolhido);
                var chip = MedirChip(g, texto, cursor, y + 16);

                if (chip.Right > x + largura) break;

                _zonas.Add(new Zona
                {
                    Area = chip,
                    Texto = texto,
                    Ativa = jogo.Status == escolhido,
                    Acao = () => _lote.DefinirStatus(Um(jogo), escolhido)
                });

                cursor = chip.Right + VaoDoChip;
            }

            DesenharChipsPendentes(g);
            return y + 48;
        }

        private void DesenharTags(Graphics g, Font rotulo, Jogo jogo, int x, int y, int largura)
        {
            Rotulo(g, rotulo, "Tags", x, y, largura);

            var cursor = x;

            foreach (var tag in jogo.Tags)
            {
                var texto = "#" + tag + "  ×";
                var chip = MedirChip(g, texto, cursor, y + 16);

                if (chip.Right > x + largura) break;

                var qual = tag;
                _zonas.Add(new Zona
                {
                    Area = chip,
                    Texto = texto,
                    Ativa = true,
                    Acao = () => _lote.RemoverTag(Um(jogo), qual)
                });

                cursor = chip.Right + VaoDoChip;
            }

            var adicionar = MedirChip(g, "+ tag", cursor, y + 16);
            if (adicionar.Right <= x + largura)
            {
                _zonas.Add(new Zona
                {
                    Area = adicionar,
                    Texto = "+ tag",
                    Ativa = false,
                    Acao = () => _lote.AplicarTag(Um(jogo))
                });
            }

            DesenharChipsPendentes(g);
        }

        /// <summary>
        /// Pinta os chips criados desde a última chamada. As estrelas desenham a si mesmas
        /// e são puladas; o índice evita repintar a linha anterior a cada linha nova.
        /// </summary>
        private void DesenharChipsPendentes(Graphics g)
        {
            for (var i = _zonasDesenhadas; i < _zonas.Count; i++)
            {
                var zona = _zonas[i];
                if (zona.Texto.Length == 0) continue;   // estrela

                var aceso = i == _zonaSobOMouse;

                using (var caminho = Formas.Arredondado(zona.Area, AlturaDoChip / 2))
                {
                    using (var pincel = new SolidBrush(zona.Ativa
                               ? Color.FromArgb(46, 62, 92)
                               : aceso ? Tema.ControleAceso : Tema.Controle))
                    {
                        g.FillPath(pincel, caminho);
                    }

                    using (var caneta = new Pen(zona.Ativa ? Tema.Acento : aceso ? Tema.BordaClara : Tema.Borda))
                        g.DrawPath(caneta, caminho);
                }

                using (var fonte = new Font("Segoe UI", 8.5f))
                {
                    TextRenderer.DrawText(g, zona.Texto, fonte, zona.Area,
                        zona.Ativa ? Tema.TextoForte : aceso ? Tema.Texto : Tema.TextoFraco,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }

            _zonasDesenhadas = _zonas.Count;
        }

        private static Rectangle MedirChip(Graphics g, string texto, int x, int y)
        {
            using (var fonte = new Font("Segoe UI", 8.5f))
            {
                var largura = TextRenderer.MeasureText(g, texto, fonte,
                    new Size(int.MaxValue, AlturaDoChip), TextFormatFlags.NoPadding).Width + (2 * RecuoDoChip);

                return new Rectangle(x, y, largura, AlturaDoChip);
            }
        }

        private static void Rotulo(Graphics g, Font fonte, string texto, int x, int y, int largura)
            => TextRenderer.DrawText(g, texto.ToUpperInvariant(), fonte, new Rectangle(x, y, largura, 16),
                Tema.TextoFraco,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        /// <summary>As ações em lote atendem um jogo só sem nenhum caso especial.</summary>
        private static List<Jogo> Um(Jogo jogo) => new List<Jogo> { jogo };

        /// <summary>Uma linha da ficha: rótulo apagado em cima, valor embaixo.</summary>
        private static int Ficha(Graphics g, Font rotulo, Font valor, int x, int y, int largura,
                                 string nome, string texto, Color cor)
        {
            TextRenderer.DrawText(g, nome.ToUpperInvariant(), rotulo, new Rectangle(x, y, largura, 16),
                Tema.TextoFraco,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, texto, valor, new Rectangle(x, y + 16, largura, 22), cor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);

            return y + 48;
        }

        private void DesenharArte(Graphics g)
        {
            if (_areaDaArte.Width <= 0 || _areaDaArte.Height <= 0) return;

            using (var caminho = Formas.Arredondado(_areaDaArte, 10))
            {
                // Um clip só, uma vez por pintura: aqui não existe o custo por card que
                // fez a grade tapar as pontas em vez de recortar.
                var anterior = g.Clip;
                g.SetClip(caminho);

                if (_arte != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(_arte, _areaDaArte);
                }
                else
                {
                    using (var pincel = new SolidBrush(Tema.Superficie))
                        g.FillRectangle(pincel, _areaDaArte);
                }

                g.Clip = anterior;

                using (var caneta = new Pen(Tema.Borda))
                    g.DrawPath(caneta, caminho);
            }
        }

        /// <summary>
        /// A caixa arredondada atrás do campo de argumentos. O <see cref="CampoDeTexto"/>
        /// não tem borda própria de propósito — quem desenha a moldura é sempre o painel
        /// que o hospeda, para não existirem duas formas de desenhar um campo.
        /// </summary>
        private Rectangle MolduraDoCampo()
            => Rectangle.FromLTRB(_argumentos.Left - 8, _argumentos.Top - 5,
                                  _argumentos.Right + 8, _argumentos.Bottom + 5);

        private void DesenharMolduraDoCampo(Graphics g)
        {
            using (var caminho = Formas.Arredondado(MolduraDoCampo(), PinturaPlana.Raio))
            {
                using (var pincel = new SolidBrush(Tema.Controle))
                    g.FillPath(pincel, caminho);

                using (var caneta = new Pen(_argumentos.Focused ? Tema.Acento : Tema.Borda))
                    g.DrawPath(caneta, caminho);
            }
        }

        private void DesenharDica(Graphics g)
        {
            using (var fonte = new Font("Segoe UI", 8.25f))
            {
                var area = new Rectangle(Margem, ClientSize.Height - AlturaDaDica,
                                         ClientSize.Width - (2 * Margem), AlturaDaDica);

                TextRenderer.DrawText(g, TextoDaDica(), fonte, area, Tema.TextoFraco,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding);
            }
        }

        private static string TextoDaDica()
            => "Esc ou B fecha   •   ← → passa para o jogo anterior ou seguinte   •   F favorita   " +
               "•   clique nas estrelas, no status e nas tags para editar";

        // ---- Entrada -------------------------------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var indice = ZonaEm(e.Location);
            if (indice == _zonaSobOMouse) return;

            _zonaSobOMouse = indice;
            Cursor = indice >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_zonaSobOMouse < 0) return;

            _zonaSobOMouse = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            var indice = ZonaEm(e.Location);
            if (indice < 0) return;

            // A ação grava e reaplica os filtros por conta própria; ao painel resta
            // redesenhar com o valor novo.
            _zonas[indice].Acao();
            Invalidate();
        }

        private int ZonaEm(Point ponto)
        {
            for (var i = 0; i < _zonas.Count; i++)
            {
                if (_zonas[i].Area.Contains(ponto)) return i;
            }
            return -1;
        }

        private void GravarArgumentos()
        {
            if (_jogo is not { } jogo) return;

            var novo = _argumentos.Text.Trim();
            if (string.Equals(novo, jogo.Argumentos ?? "", StringComparison.Ordinal)) return;

            jogo.Argumentos = novo;
            _contexto.SalvarBiblioteca();
            _contexto.Avisar($"Argumentos de \"{jogo.Titulo}\" salvos.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) LiberarArte();
            base.Dispose(disposing);
        }
    }
}
