using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Mochila.Capas;
using Mochila.Dados;
using Mochila.Entrada;
using Mochila.Execucao;
using Mochila.Modelo;
using Mochila.Scanner;
using Mochila.UI;
using Mochila.Util;

namespace Mochila
{
    /// <summary>
    /// A janela do launcher: grade de capas, busca, ordenação e o scan.
    ///
    /// Tudo é desenhado num controle só (<see cref="GradeDeCapas"/>), e só as imagens
    /// visíveis ficam na memória — o launcher precisa caber num notebook fraco e não
    /// pode roubar recurso do jogo.
    /// </summary>
    public sealed class FormPrincipal : Form, IContextoDoLauncher
    {
        private readonly CacheDeMiniaturas _miniaturas = new CacheDeMiniaturas();
        private readonly GradeDeCapas _grade;
        private readonly CampoDeTexto _busca;
        private readonly ComboEscuro _ordenacao;
        private readonly ChipAlternavel _somenteFavoritos;
        private readonly ComboEscuro _tamanhoDoCard;
        private readonly Rodape _rodape;

        private readonly LancadorDeJogos _lancador = new LancadorDeJogos();
        private readonly GerenciadorDeCapas _capas;

        /// <summary>
        /// Tudo que é ação sobre um jogo mora aqui desde a fase 9. A janela ficou com
        /// layout, eventos de entrada e orquestração.
        /// </summary>
        private readonly AcoesDoJogo _acoes;

        /// <summary>A tela de detalhes da fase 11: um painel sobre a grade, não uma janela.</summary>
        private readonly PainelDeDetalhes _detalhes;

        /// <summary>Tag, nota, status e remoção sobre a seleção inteira (fase 12).</summary>
        private readonly AcoesEmLote _lote;

        /// <summary>
        /// A faixa de tags clicáveis, embaixo da barra superior (fase 12).
        ///
        /// Construída no corpo do construtor, e NÃO aqui como inicializador de campo: os
        /// inicializadores rodam antes da primeira linha do construtor, ou seja, antes de
        /// o tema da fase 17 estar escolhido. A faixa nascia então com a cor do tema
        /// errado, e ficava sendo a única tarja escura numa janela clara.
        /// </summary>
        private readonly FaixaDeTags _faixaDeTags;

        /// <summary>Onde está escrito o que cada botão do controle faz. Ver fase 8.</summary>
        private readonly RoteadorDeComandos _roteador = new RoteadorDeComandos();

        /// <summary>null quando esta máquina não tem XInput: o launcher segue só no teclado.</summary>
        private GamepadNavegacao? _gamepad;

        /// <summary>
        /// O único timer do launcher. Só existe com a janela na frente — ver
        /// <see cref="IniciarGamepad"/>.
        /// </summary>
        private Timer? _relogioDoGamepad;

        /// <summary>Linha "N jogos selecionados" no topo do menu. Só aparece em lote.</summary>
        private ToolStripMenuItem? _cabecalhoDaSelecao;

        private ToolStripMenuItem? _itemBuscarOnline;
        private ToolStripMenuItem? _itemColar;
        private ToolStripMenuItem? _itemLote;
        private ToolStripMenuItem? _itemLocalizar;

        private Biblioteca _biblioteca = new Biblioteca();
        private Config _config = new Config();

        /// <summary>
        /// O histórico de sessões (fase 13). Nasce nulo e só é lido do disco quando alguém
        /// pergunta — abrir os detalhes, abrir as estatísticas ou terminar uma sessão.
        ///
        /// <b>A abertura do launcher não toca neste arquivo</b>, e isso é regra da spec, não
        /// economia de estilo: a grade tem que aparecer no mesmo tempo de antes da fase 13,
        /// com histórico de cinco anos ou nenhum. O <c>--autoteste</c> prova pelo contador de
        /// leituras do <see cref="ArquivoTexto"/>.
        /// </summary>
        private HistoricoDeSessoes? _sessoes;

        /// <summary>Recado curto no rodapé (jogo que fechou na hora, jogo já aberto).</summary>
        private string _avisoDaSessao = "";

        /// <summary>Liga o SW_SHOWNOACTIVATE na próxima exibição. Ver <see cref="Reaparecer"/>.</summary>
        private bool _mostrarSemAtivar;

        /// <summary>A grade, para o bench de memória rolar a biblioteca. Só diagnóstico.</summary>
        internal GradeDeCapas GradeParaDiagnostico => _grade;

        /// <summary>O cache de miniaturas, para o bench contar imagens vivas. Só diagnóstico.</summary>
        internal CacheDeMiniaturas MiniaturasParaDiagnostico => _miniaturas;

        /// <summary>A biblioteca carregada, para o bench achar o jogo simulado. Só diagnóstico.</summary>
        internal Biblioteca BibliotecaParaDiagnostico => _biblioteca;

        /// <summary>Lança um jogo pelo mesmo caminho do Enter na grade. Só diagnóstico.</summary>
        internal void LancarParaDiagnostico(Jogo jogo) => Jogar(jogo);

        /// <summary>
        /// Abre a tela de detalhes no jogo selecionado, pelo mesmo caminho do "I", e
        /// devolve o painel para a captura poder desenhá-lo por cima.
        /// </summary>
        internal PainelDeDetalhes AbrirDetalhesParaDiagnostico()
        {
            AbrirDetalhes(_grade.JogoSelecionado);
            return _detalhes;
        }

        internal void FecharDetalhesParaDiagnostico() => FecharDetalhes();

        /// <summary>As ações sobre um jogo, para a captura montar a tela de integridade.</summary>
        internal AcoesDoJogo AcoesParaDiagnostico => _acoes;

        internal PainelDeDetalhes PainelDeDetalhesParaDiagnostico => _detalhes;

        internal bool DetalhesAbertosParaDiagnostico => _detalhes.Visible;

        /// <summary>
        /// Manda um comando pelo mesmo caminho do gamepad e do teclado. É assim que o teste
        /// exercita a navegação de verdade em vez de chamar o método interno de cada tela.
        /// </summary>
        internal bool DespacharParaDiagnostico(ComandoDeNavegacao comando) => _roteador.Despachar(comando);

        internal ContextoDeNavegacao ContextoParaDiagnostico => _roteador.Contexto;

        /// <summary>Digita na barra de busca, com tudo que isso dispara. Só diagnóstico.</summary>
        internal void BuscarParaDiagnostico(string texto) => _busca.Text = texto;

        /// <summary>Liga e desliga a seção "Continuar jogando", como as configurações fariam.</summary>
        internal void DefinirContinuarJogandoParaDiagnostico(bool mostrar)
        {
            _config.MostrarContinuarJogando = mostrar;
            AplicarFiltros();
        }

        /// <summary>Refaz a lista visível. Só diagnóstico.</summary>
        internal void ReaplicarFiltrosParaDiagnostico() => AplicarFiltros();

        /// <summary>O caminho do F5, para o teste provar o que ele lê (e o que não lê).</summary>
        internal void RecarregarParaDiagnostico()
        {
            FecharDetalhes();
            Carregar();
        }

        /// <summary>As ações em lote ligadas nesta janela, para o teste usar a mesma instância.</summary>
        internal AcoesEmLote LoteParaDiagnostico => _lote;

        /// <summary>Shift+setas e Ctrl+A pelo mesmo caminho do ProcessCmdKey.</summary>
        internal bool TratarSelecaoMultiplaParaDiagnostico(Keys chave, Keys modificadores)
            => TratarSelecaoMultipla(chave, modificadores);

        // ---- O que as ações precisam da janela (fase 9) -----------------------------------------
        //
        // Implementação explícita de propósito: isto é contrato com AcoesDoJogo, não API
        // pública da janela.

        Form IContextoDoLauncher.Janela => this;

        Biblioteca IContextoDoLauncher.Biblioteca => _biblioteca;

        Config IContextoDoLauncher.Config => _config;

        HistoricoDeSessoes IContextoDoLauncher.Sessoes() => ObterSessoes();

        Jogo? IContextoDoLauncher.JogoSelecionado => _grade.JogoSelecionado;

        bool IContextoDoLauncher.JogoRodando => _lancador.JogoRodando;

        void IContextoDoLauncher.SalvarBiblioteca() => SalvarBiblioteca();

        void IContextoDoLauncher.Avisar(string texto) => MostrarAviso(texto);

        void IContextoDoLauncher.RedesenharGrade()
        {
            _grade.Invalidate();

            // Trocar a capa pela tela de detalhes tem que trocar a arte grande também.
            // Fechado, o painel ignora a chamada.
            _detalhes.Atualizar();
        }

        void IContextoDoLauncher.ReaplicarFiltros() => AplicarFiltros();

        public FormPrincipal()
        {
            Text = "Mochila Launcher";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 700);
            MinimumSize = new Size(700, 460);
            DoubleBuffered = true;
            KeyPreview = true;

            // A PALETA VEM ANTES DE TUDO (fase 17). Cada controle copia BackColor e
            // ForeColor do tema no próprio construtor, então escolher o tema depois de
            // montar a janela não repintaria ninguém.
            //
            // É a única razão de o config.json ser lido duas vezes na abertura: aqui, só
            // para o tema, e no Carregar() logo abaixo, junto com a biblioteca. São algumas
            // centenas de bytes, e a alternativa seria a janela nascer com a paleta errada.
            _config = LerConfig();
            Tema.Aplicar(_config);

            _faixaDeTags = new FaixaDeTags();

            // Cores, ícone da mochila e barra de título na cor do tema, tudo do mesmo lugar.
            Tema.AplicarNaJanela(this);

            _grade = new GradeDeCapas(_miniaturas) { Dock = DockStyle.Fill };
            _grade.JogoAcionado += (_, jogo) => Jogar(jogo);
            // Qualquer navegação minha limpa o recado da sessão anterior.
            _grade.SelecaoMudou += (_, _) => { _avisoDaSessao = ""; AtualizarRodape(); };

            _lancador.SessaoTerminada += AoTerminarSessao;
            _lancador.ProcessoFilhoAdotado += AoAdotarProcessoFilho;
            _lancador.AvisoDeScript += AoAvisarDeScript;

            _capas = new GerenciadorDeCapas(_miniaturas);
            _acoes = new AcoesDoJogo(this, _capas, _miniaturas, _lancador);

            _lote = new AcoesEmLote(this, _acoes);

            _detalhes = new PainelDeDetalhes(this, _acoes, _lote);
            _detalhes.JogoPedido += (_, jogo) => { FecharDetalhes(); Jogar(jogo); };

            _grade.DetalhesPedidos += (_, jogo) => AbrirDetalhes(jogo);
            _grade.MarcacaoMudou += (_, _) => AtualizarRodape();

            // Clicar numa tag da faixa liga e desliga o "#tag" na busca — o mesmo filtro
            // que eu digitaria, para não existirem dois estados de filtro para sincronizar.
            _faixaDeTags.TagAcionada += (_, tag) => AlternarFiltroDeTag(tag);

            // O painel acompanha a área da grade. Ouvir o Resize da grade, e não o da
            // janela, evita depender da ordem em que os controles ancorados se acomodam.
            _grade.Resize += (_, _) => { if (_detalhes.Visible) _detalhes.Bounds = _grade.Bounds; };

            _grade.ContextMenuStrip = CriarMenuDoCard();
            PrepararArrastarESoltar();

            _busca = CriarBusca();
            _ordenacao = CriarOrdenacao();
            _somenteFavoritos = CriarFiltroDeFavoritos();
            _tamanhoDoCard = CriarTamanhoDoCard();
            _rodape = CriarRodape();

            // A ordem importa: o WinForms ancora do último filho para o primeiro, então a
            // barra superior precisa entrar DEPOIS da faixa de tags para ficar acima dela.
            Controls.Add(_grade);
            Controls.Add(_detalhes);
            Controls.Add(_faixaDeTags);
            Controls.Add(CriarBarraSuperior());
            Controls.Add(_rodape);

            KeyDown += AoTeclar;
            PrepararEntrada();

            Carregar();
            _busca.Select();
        }

        // ---- Barra superior ------------------------------------------------------------------

        /// <summary>
        /// A barra superior. Os botões acompanham a borda direita e os controles de filtro
        /// ficam à esquerda — nada de Dock, que empilharia os botões grudados e ignoraria a
        /// margem entre eles.
        ///
        /// <b>O que ela não pode mais fazer é sobrepor.</b> Até a fase 13 os controles da
        /// esquerda tinham posição FIXA (o combo de tamanho terminava em x=720) e os botões
        /// da direita eram calculados a partir da borda: com a janela no tamanho padrão
        /// (1100 de área cliente) o "Surpresa" começava em x=656 e era desenhado POR BAIXO
        /// do combo de tamanho — visível só por um "(R)" sobrando ao lado dele, e
        /// impossível de clicar. No tamanho mínimo, metade da barra sumia por baixo da outra
        /// metade.
        ///
        /// A régua agora é <see cref="AjustarBarra"/>, e a saída para a janela estreita é
        /// <b>quebrar em duas linhas</b>, não esconder controle: ordenação, favoritos e
        /// tamanho do card não têm outro caminho de mouse, e um filtro que some conforme a
        /// janela encolhe é pior que uma barra mais alta.
        /// </summary>
        private Control CriarBarraSuperior()
        {
            var painel = new PainelDeControles
            {
                Dock = DockStyle.Top,
                Height = AlturaDaBarra,
                LinhaEmBaixo = true
            };

            var escanear = Botoes.CriarPrincipal("Escanear jogos (F6)", new Point(0, 14), 158, 30);
            escanear.Click += (_, _) => EscanearJogos();

            var configurar = Botoes.Criar("Configurações (F10)", new Point(0, 14), 150, 30);
            configurar.Click += (_, _) => AbrirConfiguracoes();

            // A roleta: 200 capas na tela é bom para olhar e ruim para decidir.
            var surpresa = Botoes.Criar("Surpresa (R)", new Point(0, 14), 108, 30);
            surpresa.Click += (_, _) => SortearJogo();

            painel.Controls.Add(surpresa);
            painel.Controls.Add(_busca);
            painel.Controls.Add(_ordenacao);
            painel.Controls.Add(_somenteFavoritos);
            painel.Controls.Add(_tamanhoDoCard);
            painel.Controls.Add(configurar);
            painel.Controls.Add(escanear);

            painel.Emoldurar(_busca, comLupa: true);

            painel.Resize += (_, _) => AjustarBarra(painel, escanear, configurar, surpresa);
            AjustarBarra(painel, escanear, configurar, surpresa);

            return painel;
        }

        /// <summary>Altura da barra superior com tudo numa linha só.</summary>
        private const int AlturaDaBarra = 58;

        /// <summary>Altura quando os filtros descem para a segunda linha.</summary>
        private const int AlturaDaBarraEmDuasLinhas = 96;

        /// <summary>Vão padrão entre controles da barra.</summary>
        private const int VaoDaBarra = 8;

        /// <summary>Recuo da esquerda do campo de busca — o espaço onde o painel desenha a lupa.</summary>
        private const int RecuoDaBusca = 44;

        /// <summary>Abaixo disto a busca deixa de ser campo e vira caixinha inútil.</summary>
        private const int LarguraMinimaDaBusca = 150;

        /// <summary>
        /// Põe cada controle da barra no lugar, para a largura que a janela tem agora.
        ///
        /// Uma linha enquanto couber; duas quando não couber. O campo de busca é quem
        /// estica e encolhe (ele é o único cujo tamanho não muda o que dá para fazer), e os
        /// três botões da direita nunca mudam de largura — texto de botão cortado é pior que
        /// botão pequeno.
        /// </summary>
        private void AjustarBarra(Control painel, Control escanear, Control configurar, Control surpresa)
        {
            var largura = painel.ClientSize.Width;

            // Os botões da direita, sempre colados na borda e sempre na primeira linha.
            escanear.Left = largura - 12 - escanear.Width;
            configurar.Left = escanear.Left - VaoDaBarra - configurar.Width;
            surpresa.Left = configurar.Left - VaoDaBarra - surpresa.Width;

            var filtros = new Control[] { _ordenacao, _somenteFavoritos, _tamanhoDoCard };

            var larguraDosFiltros = 0;
            foreach (var filtro in filtros) larguraDosFiltros += filtro.Width + VaoDaBarra;

            // Cabe tudo numa linha só? A conta é a busca no mínimo, mais os filtros, contra
            // o espaço que sobrou à esquerda do primeiro botão.
            var espacoNaPrimeiraLinha = surpresa.Left - VaoDaBarra - RecuoDaBusca;
            var umaLinha = espacoNaPrimeiraLinha >= LarguraMinimaDaBusca + larguraDosFiltros;

            painel.Height = umaLinha ? AlturaDaBarra : AlturaDaBarraEmDuasLinhas;

            if (umaLinha)
            {
                _busca.SetBounds(RecuoDaBusca, 20, espacoNaPrimeiraLinha - larguraDosFiltros, _busca.Height);

                var cursor = _busca.Right + (2 * VaoDaBarra);
                foreach (var filtro in filtros)
                {
                    filtro.Top = 15;
                    filtro.Left = cursor;
                    cursor += filtro.Width + VaoDaBarra;
                }
            }
            else
            {
                // Linha 1: busca (o que sobrar) + os botões. Linha 2: os filtros, alinhados
                // com a busca para a barra continuar tendo uma coluna só de referência.
                _busca.SetBounds(RecuoDaBusca, 20,
                                 Math.Max(LarguraMinimaDaBusca, espacoNaPrimeiraLinha), _busca.Height);

                var cursor = RecuoDaBusca;
                foreach (var filtro in filtros)
                {
                    filtro.Top = 56;
                    filtro.Left = cursor;
                    cursor += filtro.Width + VaoDaBarra;
                }
            }

            // A moldura arredondada e a lupa são desenhadas pelo painel a partir dos limites
            // do campo: mover o campo sem repintar deixaria a moldura para trás.
            painel.Invalidate();
        }

        private CampoDeTexto CriarBusca()
        {
            var busca = new CampoDeTexto
            {
                // O x já conta a lupa que o painel desenha à esquerda do campo.
                Location = new Point(44, 20),
                Width = 234,
                Dica = "Buscar jogo...",
                Font = new Font("Segoe UI", 9.75f)
            };

            busca.TextChanged += (_, _) => AplicarFiltros();
            return busca;
        }

        private ComboEscuro CriarOrdenacao()
        {
            var combo = new ComboEscuro
            {
                Location = new Point(302, 15),
                Size = new Size(178, 28)
            };

            combo.Items.Add("Ordem alfabética");
            combo.Items.Add("Mais jogados");
            combo.Items.Add("Jogados recentemente");
            combo.SelectedIndex = 0;

            combo.SelectedIndexChanged += (_, _) =>
            {
                _config.Ordenacao = (OrdenacaoBiblioteca)combo.SelectedIndex;
                SalvarConfig();
                AplicarFiltros();
            };
            return combo;
        }

        private ChipAlternavel CriarFiltroDeFavoritos()
        {
            var caixa = new ChipAlternavel
            {
                Text = "★  Favoritos",
                Location = new Point(492, 15),
                Size = new Size(112, 28)
            };

            caixa.CheckedChanged += (_, _) =>
            {
                _config.SomenteFavoritos = caixa.Checked;
                SalvarConfig();
                AplicarFiltros();
            };
            return caixa;
        }

        private ComboEscuro CriarTamanhoDoCard()
        {
            var combo = new ComboEscuro
            {
                Location = new Point(616, 15),
                Size = new Size(104, 28)
            };

            combo.Items.Add("Card P");
            combo.Items.Add("Card M");
            combo.Items.Add("Card G");
            combo.SelectedIndex = 1;

            combo.SelectedIndexChanged += (_, _) =>
            {
                _config.TamanhoCard = (TamanhoCard)combo.SelectedIndex;
                _grade.TamanhoDoCard = _config.TamanhoCard;
                SalvarConfig();
            };
            return combo;
        }

        private static Rodape CriarRodape() => new Rodape
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(12, 0, 12, 0)
        };

        // ---- Carga e filtros ------------------------------------------------------------------

        /// <summary>
        /// Carrega tudo tolerando disco ruim.
        ///
        /// Três coisas dão errado de verdade aqui, e nenhuma pode virar tela de erro do
        /// .NET: o HD sumiu no meio do uso, o biblioteca.json está corrompido ou vazio, e
        /// a pasta está somente-leitura. Em todas, o launcher abre — com o que dá.
        /// </summary>
        private void Carregar()
        {
            try
            {
                // Primeiro uso num HD novo: cria _mochila\, a biblioteca e o config padrão.
                Caminhos.GarantirEstrutura();
                if (!ArquivoTexto.Existe(Caminhos.ArquivoBiblioteca)) new Biblioteca().Salvar();
                if (!ArquivoTexto.Existe(Caminhos.ArquivoConfig)) new Config().Salvar();
            }
            catch (Exception erro)
            {
                // Sem poder escrever, o launcher ainda serve para abrir jogo.
                MessageBox.Show(this,
                    $"Não consegui preparar a pasta \"{Caminhos.NomePastaEstado}\".{Environment.NewLine}{Environment.NewLine}" +
                    $"{erro.Message}{Environment.NewLine}{Environment.NewLine}" +
                    "O launcher abre mesmo assim, mas nada será gravado até isso se resolver.",
                    "Mochila Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _config = LerConfig();
            _biblioteca = CarregarBiblioteca();

            _ordenacao.SelectedIndex = (int)_config.Ordenacao;
            _somenteFavoritos.Checked = _config.SomenteFavoritos;
            _tamanhoDoCard.SelectedIndex = (int)_config.TamanhoCard;
            _grade.TamanhoDoCard = _config.TamanhoCard;

            AplicarFiltros();

            // O tema já está no ar desde o construtor; o que chega aqui é o recado de uma
            // cor de acento que não deu para entender (fase 17). Rodapé, nunca caixa de
            // diálogo — preferência ilegível não pode virar obstáculo entre eu e a grade.
            //
            // DEPOIS do AplicarFiltros, e não antes: montar a lista mexe na seleção, e
            // trocar de card limpa o recado do rodapé. Escrito antes, ele sumiria sozinho.
            if (Tema.Aviso is { } avisoDoTema) MostrarAviso(avisoDoTema);
        }

        /// <summary>
        /// Lê o <c>config.json</c>. Preferência ilegível não impede o launcher de abrir —
        /// os padrões servem, e a próxima gravação conserta o arquivo.
        /// </summary>
        private static Config LerConfig()
        {
            try
            {
                return Config.Carregar();
            }
            catch (Exception)
            {
                return new Config();
            }
        }

        /// <summary>
        /// Lê a biblioteca. Arquivo corrompido ou vazio é posto de lado com nome datado
        /// em vez de sobrescrito: se eu tiver 200 jogos catalogados e o JSON quebrar por
        /// queda de energia, abrir o launcher NÃO pode ser o que apaga o histórico de vez.
        /// </summary>
        private Biblioteca CarregarBiblioteca()
        {
            try
            {
                return Biblioteca.Carregar();
            }
            catch (DadosCorrompidosException erro)
            {
                var salvo = PorDeLado(erro.Caminho);

                MessageBox.Show(this,
                    $"{erro.Message}{Environment.NewLine}{Environment.NewLine}" +
                    (salvo is null
                        ? "Começando com uma biblioteca vazia. O arquivo antigo continua onde estava."
                        : $"Guardei o arquivo com problema como \"{salvo}\" e comecei uma biblioteca vazia." +
                          Environment.NewLine +
                          "Se ele ainda tiver dados bons, dá para recuperar à mão — ou é só escanear de novo (F6)."),
                    "Biblioteca", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return new Biblioteca();
            }
            catch (Exception erro)
            {
                // HD desconectado, permissão negada, caminho longo demais...
                MessageBox.Show(this,
                    $"Não consegui ler a biblioteca.{Environment.NewLine}{Environment.NewLine}" +
                    $"{erro.Message}{Environment.NewLine}{Environment.NewLine}" +
                    "O HD ainda está conectado?",
                    "Biblioteca", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return new Biblioteca();
            }
        }

        /// <summary>Renomeia o arquivo problemático. Devolve o nome novo, ou null se não deu.</summary>
        private static string? PorDeLado(string caminho)
        {
            try
            {
                if (!File.Exists(caminho)) return null;

                // Os apóstrofos não são enfeite: sem eles, "m", "d" e "s" de "corrompido"
                // valem como minuto, dia e segundo, e a biblioteca posta de lado sai com
                // nome de lixo. Bug encontrado na fase 13, no arquivo de sessões — a linha
                // era a mesma nos dois lugares.
                var nome = Path.GetFileName(caminho) +
                           DateTime.Now.ToString("'.corrompido-'yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

                File.Move(caminho, Path.Combine(Path.GetDirectoryName(caminho)!, nome));
                return nome;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// O histórico de sessões, lido do disco na primeira chamada e reaproveitado depois.
        ///
        /// A leitura é preguiçosa de propósito (ver <see cref="_sessoes"/>), e a corrupção do
        /// arquivo vira aviso de rodapé — nunca caixa de diálogo e nunca exceção: quem pediu
        /// isto queria ver a ficha de um jogo, não resolver um problema de arquivo.
        /// </summary>
        private HistoricoDeSessoes ObterSessoes()
        {
            if (_sessoes is { } carregado) return carregado;

            _sessoes = HistoricoDeSessoes.Carregar();

            if (_sessoes.Corrompido)
            {
                MostrarAviso("Não consegui ler o histórico de sessões — ele começa de novo, e o " +
                             "arquivo velho é guardado com nome datado. O tempo total dos jogos está a salvo " +
                             "na biblioteca.");
            }
            else if (_sessoes.SomenteLeitura)
            {
                MostrarAviso($"O histórico de sessões é da versão {_sessoes.Versao}, mais nova que este " +
                             "launcher: dá para olhar, mas sessão nova não será gravada nele.");
            }

            return _sessoes;
        }

        /// <summary>
        /// Quantos jogos a seção "Continuar jogando" mostra. Cinco: é a fileira de cima da
        /// grade em qualquer tamanho de card, e "os últimos que eu joguei" com vinte nomes
        /// não responde nada.
        /// </summary>
        private const int JogosEmContinuarJogando = 5;

        private void AplicarFiltros()
        {
            var consulta = ConsultaDeBusca.Analisar(_busca.Text);

            var visiveis = FiltroDaBiblioteca.Aplicar(
                _biblioteca.Jogos, consulta, _config.Ordenacao, _config.SomenteFavoritos);

            // Busca que não casou com nada é resultado, não erro — e o recado da tela vazia
            // precisa dizer qual dos dois vazios é este.
            _grade.TextoDeVazio = _biblioteca.Jogos.Count == 0
                ? "Nenhum jogo catalogado ainda.\r\nAperte F6 para escanear, ou arraste um .exe para cá."
                : "Nada casou com esse filtro.\r\nApague a busca (Esc) ou tente outra tag.";

            var recentes = SepararContinuarJogando(visiveis);

            _grade.DefinirJogos(visiveis, recentes,
                                "Continuar jogando", "Todos os jogos");

            // A faixa sai do acervo INTEIRO, não do filtrado: uma faixa que encolhe
            // conforme eu filtro esconderia justamente a tag que eu quero desligar.
            _faixaDeTags.Definir(_biblioteca.Jogos, consulta);

            AtualizarRodape();
        }

        /// <summary>
        /// Reordena <paramref name="visiveis"/> in loco, subindo para o começo os jogados
        /// mais recentemente. Devolve quantos subiram — 0 quando a seção não deve aparecer.
        ///
        /// Três condições desligam a seção, e nenhuma delas é preferência de estilo:
        ///
        /// <list type="number">
        /// <item>a opção está desligada nas configurações;</item>
        /// <item>não há histórico (<c>ultimaVezJogado</c> nulo em todo mundo) — a spec pede
        /// que ela se esconda sozinha nesse caso, e uma seção vazia com título é pior que
        /// seção nenhuma;</item>
        /// <item><b>há busca digitada.</b> Filtro é uma pergunta com resposta exata; dividir
        /// o resultado de "#corrida" em duas seções esconde metade dele acima da dobra e faz
        /// o primeiro card deixar de ser o melhor casamento.</item>
        /// </list>
        ///
        /// A ordenação "jogados recentemente" continua tendo a seção: ali ela é redundante
        /// mas coerente, e some sozinha quando há menos de seis jogos — com cinco ou menos,
        /// a segunda seção ficaria vazia e a lista inteira viraria "continuar jogando".
        ///
        /// <b>Reordena em vez de duplicar.</b> Ver <c>GradeDeCapas.DefinirJogos</c>: card
        /// repetido brigaria com a marcação por id, com a seleção por id e com a contagem
        /// do rodapé.
        /// </summary>
        private int SepararContinuarJogando(List<Jogo> visiveis)
        {
            if (!_config.MostrarContinuarJogando) return 0;
            if (visiveis.Count <= JogosEmContinuarJogando) return 0;
            if (_busca.TextLength > 0) return 0;

            var recentes = new List<Jogo>();
            foreach (var jogo in visiveis)
            {
                if (jogo.UltimaVezJogado is not null) recentes.Add(jogo);
            }

            if (recentes.Count == 0) return 0;

            recentes.Sort((a, b) => b.UltimaVezJogado!.Value.CompareTo(a.UltimaVezJogado!.Value));
            if (recentes.Count > JogosEmContinuarJogando)
                recentes.RemoveRange(JogosEmContinuarJogando, recentes.Count - JogosEmContinuarJogando);

            // Todo mundo é recente: não há segunda seção para separar de nada.
            if (recentes.Count >= visiveis.Count) return 0;

            var escolhidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var jogo in recentes) escolhidos.Add(jogo.Id);

            var resto = new List<Jogo>(visiveis.Count - recentes.Count);
            foreach (var jogo in visiveis)
            {
                if (!escolhidos.Contains(jogo.Id)) resto.Add(jogo);
            }

            visiveis.Clear();
            visiveis.AddRange(recentes);
            visiveis.AddRange(resto);

            return recentes.Count;
        }

        /// <summary>
        /// Liga e desliga <c>#tag</c> na barra de busca. Clicar na faixa e digitar têm que
        /// dar no mesmo lugar: o filtro é um só, e é o texto da busca.
        /// </summary>
        private void AlternarFiltroDeTag(string tag)
        {
            var alvo = "#" + tag;
            var partes = new List<string>();
            var removeu = false;

            foreach (var pedaco in _busca.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(pedaco, alvo, StringComparison.OrdinalIgnoreCase)) { removeu = true; continue; }
                partes.Add(pedaco);
            }

            if (!removeu) partes.Add(alvo);

            _busca.Text = string.Join(" ", partes.ToArray());
            _busca.SelectionStart = _busca.TextLength;
        }

        private void AtualizarRodape()
        {
            var selecionado = _grade.JogoSelecionado;
            var total = $"{_grade.Jogos.Count} de {_biblioteca.Jogos.Count} jogo(s)";

            // Antes de tudo: um arquivo mais novo que o binário muda o significado da
            // sessão inteira, e eu preciso ler isso ANTES de jogar três horas.
            if (_biblioteca.SomenteLeitura)
            {
                _rodape.Definir(total, "SOMENTE LEITURA",
                    $" — biblioteca em versão {_biblioteca.Versao}, mais nova que este launcher: " +
                    "nada é gravado e o tempo jogado NÃO será contado. Atualize o Mochila.exe.",
                    alerta: true);
                return;
            }

            // O aviso da última sessão vem em seguida: é a única coisa que eu preciso ler
            // no instante em que a janela reaparece.
            if (_avisoDaSessao.Length > 0)
            {
                _rodape.Definir(total, "", _avisoDaSessao, alerta: true);
                return;
            }

            if (_grade.QuantidadeMarcada > 1)
            {
                _rodape.Definir(total, $"{_grade.QuantidadeMarcada} jogos selecionados",
                    " — botão direito para etiquetar, dar nota ou definir o status de todos.", alerta: false);
                return;
            }

            if (selecionado is null)
            {
                _rodape.Definir(total, "",
                    "Setas para navegar, Enter para jogar, I para detalhes, F para favoritar, F6 para escanear.",
                    alerta: false);
                return;
            }

            // O nome do jogo vai separado para o rodapé poder destacá-lo do resto da frase.
            _rodape.Definir(total, selecionado.Titulo, DescreverJogo(selecionado), alerta: false);
        }

        private static string DescreverJogo(Jogo jogo)
        {
            var tempo = TempoDeJogo.Descrever(jogo.SegundosJogados);

            var quando = jogo.UltimaVezJogado is { } data
                ? $", última vez em {data.ToLocalTime():dd/MM/yyyy}"
                : "";

            var faltando = jogo.ExecutavelExiste() ? "" : "  [EXECUTÁVEL NÃO ENCONTRADO]";

            return $" — {tempo}{quando}{faltando}";
        }

        private void SalvarConfig()
        {
            try
            {
                _config.Salvar();
            }
            catch (Exception)
            {
                // Preferência é preferência: não vale interromper o uso por causa disso.
            }
        }

        // ---- Teclado ---------------------------------------------------------------------------

        private void AoTeclar(object? remetente, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.F5:
                    // Recarregar troca os objetos Jogo por outros lidos do disco: o painel
                    // de detalhes ficaria olhando para uma instância órfã.
                    FecharDetalhes();
                    Carregar();
                    e.Handled = true;
                    return;

                case Keys.F6:
                    EscanearJogos();
                    e.Handled = true;
                    return;

                // O relatório de integridade da fase 17. Mesma regra do F9 abaixo: sem
                // botão de controle, então fora do roteador.
                case Keys.F8:
                    AbrirIntegridade();
                    e.Handled = true;
                    return;

                // As estatísticas da fase 13. Fora do roteador porque não há botão de
                // controle para elas nesta fase — inventar comando abstrato para algo que
                // só o teclado faz seria vocabulário morto (mesma regra da multi-seleção).
                case Keys.F9:
                    AbrirEstatisticas();
                    e.Handled = true;
                    return;

                // Daqui para baixo, tudo que o controle também sabe fazer vai pelo roteador
                // — mesmo comando, mesma implementação, um lugar só.
                case Keys.F10:
                    _roteador.Despachar(ComandoDeNavegacao.Menu);
                    e.Handled = true;
                    return;

                case Keys.Escape:
                    // Esc fecha os detalhes, senão limpa a busca; com a busca já vazia, sai.
                    // O "sai" é a única coisa que o B do controle não faz — ver o comentário
                    // em TratarComando.
                    if (_detalhes.Visible || _busca.Text.Length > 0)
                        _roteador.Despachar(ComandoDeNavegacao.Voltar);
                    else Close();
                    e.Handled = true;
                    return;

                // A tecla dos detalhes (fase 11). Com o cursor num campo de texto ela é a
                // letra "i" e mais nada — daí as duas guardas.
                case Keys.I when !_busca.Focused && !_detalhes.EditandoTexto:
                    _roteador.Despachar(ComandoDeNavegacao.Detalhes);
                    e.Handled = true;
                    return;

                case Keys.F when !_busca.Focused && !_detalhes.EditandoTexto:
                    _roteador.Despachar(ComandoDeNavegacao.Favoritar);
                    e.Handled = true;
                    return;

                case Keys.R when !_busca.Focused && !_detalhes.EditandoTexto:
                    SortearJogo();
                    e.Handled = true;
                    return;
            }

            // As teclas de navegação não passam por aqui: uma TextBox come as setas antes
            // do KeyDown da janela chegar. Quem trata é o ProcessCmdKey abaixo.
        }

        /// <summary>
        /// Roteia as teclas de navegação antes de qualquer controle vê-las.
        ///
        /// Tem que ser aqui: a caixa de busca consome ← → (e Home/End) antes do KeyDown
        /// da janela, então tratar no KeyDown daria à busca a última palavra justamente
        /// nas teclas que a grade precisa.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message mensagem, Keys combinacao)
        {
            var chave = combinacao & Keys.KeyCode;
            var modificadores = combinacao & (Keys.Control | Keys.Alt | Keys.Shift);

            // Com os detalhes abertos a navegação é outra (← → folheiam os jogos), e quem
            // decide continua sendo o roteador — só o contexto mudou. Enter fica de fora
            // de propósito: com o foco num botão do painel, Enter é o clique daquele botão,
            // e roubá-lo aqui lançaria o jogo no lugar de "Remover capa".
            if (_detalhes.Visible)
            {
                if (!_detalhes.EditandoTexto && modificadores == Keys.None && chave != Keys.Enter &&
                    _roteador.Despachar(RoteadorDeTeclas.Comando(chave)))
                {
                    return true;
                }

                return base.ProcessCmdKey(ref mensagem, combinacao);
            }

            // Ctrl+←, Shift+End e afins são edição de texto: nunca viram navegação.
            // E só roteamos o que vem da busca ou da própria grade — um combo aberto
            // precisa das setas dele.
            var origemRoteavel = ActiveControl == _busca || ActiveControl == _grade || ActiveControl is null;

            // Shift+setas e Ctrl+A (fase 12). Fora do roteador de propósito: não há botão
            // de controle para multi-seleção nesta fase, e criar comando abstrato para algo
            // que só o teclado faz seria vocabulário morto.
            if (origemRoteavel && TratarSelecaoMultipla(chave, modificadores)) return true;

            // Duas perguntas diferentes, e as duas continuam existindo: "esta tecla é da
            // grade ou da busca?" (RoteadorDeTeclas.VaiParaGrade) e "que comando ela é?"
            // (RoteadorDeTeclas.Comando). Só a segunda é nova — e é ela que faz o teclado
            // desembocar no mesmo roteador do gamepad.
            if (modificadores == Keys.None && origemRoteavel &&
                RoteadorDeTeclas.VaiParaGrade(chave, ActiveControl == _busca, _busca.TextLength > 0) &&
                _roteador.Despachar(RoteadorDeTeclas.Comando(chave)))
            {
                return true;
            }

            return base.ProcessCmdKey(ref mensagem, combinacao);
        }

        /// <summary>
        /// Shift+setas estende a marcação; Ctrl+A marca tudo que está à vista.
        ///
        /// <b>Ctrl+A vale também com o cursor na busca</b>, e isso é escolha, não descuido:
        /// o fluxo desta feature é digitar o filtro e marcar o que sobrou, e exigir um
        /// clique na grade no meio disso mataria o atalho. O que se perde — selecionar o
        /// texto da busca — não faz falta aqui, porque Esc já limpa o campo inteiro.
        ///
        /// Nas setas a regra é a mesma da navegação: com texto na busca, ← → são do cursor.
        /// </summary>
        private bool TratarSelecaoMultipla(Keys chave, Keys modificadores)
        {
            if (modificadores == Keys.Control && chave == Keys.A)
                return _grade.TratarTeclaDeSelecao(chave, modificadores);

            if (modificadores != Keys.Shift) return false;

            return RoteadorDeTeclas.VaiParaGrade(chave, ActiveControl == _busca, _busca.TextLength > 0) &&
                   _grade.TratarTeclaDeSelecao(chave, modificadores);
        }

        // ---- Entrada: teclado e gamepad (fases 8 e 9) -------------------------------------------

        /// <summary>
        /// Monta a entrada. O roteador atende as duas fontes desde a fase 9; o gamepad é a
        /// parte opcional — máquina sem XInput não ganha nada disto e também não perde
        /// nada: <see cref="_gamepad"/> fica nulo e o timer nunca é criado.
        /// </summary>
        private void PrepararEntrada()
        {
            _roteador.Registrar(ContextoDeNavegacao.Grade, TratarComando);
            _roteador.Registrar(ContextoDeNavegacao.Detalhes, TratarComandoNosDetalhes);

            if (EntradaXInput.Criar() is { } fonte)
                _gamepad = new GamepadNavegacao(fonte, _roteador.Traduzir);
        }

        /// <summary>
        /// O que cada comando faz na grade. Repare que aqui não existe botão de Xbox nem
        /// tecla: chega comando, venha de onde vier.
        /// </summary>
        private bool TratarComando(ComandoDeNavegacao comando)
        {
            switch (comando)
            {
                // Navegação pura: a grade resolve sozinha, e é literalmente o mesmo
                // caminho que o teclado percorre desde a fase 9.
                case ComandoDeNavegacao.Cima:
                case ComandoDeNavegacao.Baixo:
                case ComandoDeNavegacao.Esquerda:
                case ComandoDeNavegacao.Direita:
                case ComandoDeNavegacao.PaginaAnterior:
                case ComandoDeNavegacao.PaginaSeguinte:
                case ComandoDeNavegacao.Primeiro:
                case ComandoDeNavegacao.Ultimo:
                case ComandoDeNavegacao.Confirmar:
                    return _grade.TratarComando(comando);

                case ComandoDeNavegacao.Favoritar:
                    AlternarFavorito();
                    return true;

                case ComandoDeNavegacao.Menu:
                    AbrirConfiguracoes();
                    return true;

                case ComandoDeNavegacao.TrocarOrdenacao:
                    if (_ordenacao.Items.Count > 0)
                        _ordenacao.SelectedIndex = (_ordenacao.SelectedIndex + 1) % _ordenacao.Items.Count;
                    return true;

                // Limpa a busca, e com a busca vazia não faz nada. O Esc do teclado faz
                // mais que isso (fecha o launcher com a busca vazia), e essa diferença é
                // de propósito: fechar sem querer com o polegar é irreversível demais para
                // valer a conveniência. Por isso o "fechar" mora no tratamento do Esc, não
                // aqui — o comando em si é o mesmo para as duas fontes.
                case ComandoDeNavegacao.Voltar:
                    if (_busca.TextLength > 0) _busca.Clear();
                    return true;

                case ComandoDeNavegacao.Detalhes:
                    AbrirDetalhes(_grade.JogoSelecionado);
                    return true;

                // Chega na fase 18. Até lá o botão existe e não faz nada — o que é melhor
                // que ele fazer outra coisa e ter que ser reaprendido depois.
                case ComandoDeNavegacao.SaltoAlfabetico:
                default:
                    return false;
            }
        }

        // ---- Tela de detalhes (fase 11) ---------------------------------------------------------

        /// <summary>
        /// O que cada comando faz com os detalhes abertos. É o mesmo roteador da grade, só
        /// que noutro contexto: o B do controle e o Esc do teclado chegam aqui como
        /// <see cref="ComandoDeNavegacao.Voltar"/> sem nenhum "if" espalhado pela janela.
        /// </summary>
        private bool TratarComandoNosDetalhes(ComandoDeNavegacao comando)
        {
            switch (comando)
            {
                // O X do controle e o I do teclado abrem e fecham: a mesma tecla desfaz o
                // que ela fez, que é o que a mão espera de uma tela que se sobrepõe.
                case ComandoDeNavegacao.Voltar:
                case ComandoDeNavegacao.Detalhes:
                    FecharDetalhes();
                    return true;

                case ComandoDeNavegacao.Confirmar:
                    if (_detalhes.JogoAtual is { } paraJogar)
                    {
                        FecharDetalhes();
                        Jogar(paraJogar);
                    }
                    return true;

                case ComandoDeNavegacao.Favoritar:
                    // Aqui é sempre o jogo aberto, e nunca a marcação da grade: o que está
                    // na tela é um jogo só, e favoritar trinta sem ver seria surpresa.
                    if (_detalhes.JogoAtual is { } favorito) _lote.AlternarFavorito(new List<Jogo> { favorito });
                    _detalhes.Invalidate();
                    return true;

                // Folhear sem fechar. Ficar entrando e saindo para comparar dois jogos é o
                // tipo de atrito que faz a tela de detalhes não ser usada.
                case ComandoDeNavegacao.Esquerda:
                    FolhearDetalhes(-1);
                    return true;

                case ComandoDeNavegacao.Direita:
                    FolhearDetalhes(+1);
                    return true;

                // Engolidos de propósito: rolar a grade por trás de uma tela que ocupa
                // tudo não muda nada na tela, e deixar passar acabaria mexendo na seleção
                // sem eu ver.
                case ComandoDeNavegacao.Cima:
                case ComandoDeNavegacao.Baixo:
                case ComandoDeNavegacao.PaginaAnterior:
                case ComandoDeNavegacao.PaginaSeguinte:
                case ComandoDeNavegacao.Primeiro:
                case ComandoDeNavegacao.Ultimo:
                    return true;

                case ComandoDeNavegacao.Menu:
                    AbrirConfiguracoes();
                    return true;

                default:
                    return false;
            }
        }

        private void AbrirDetalhes(Jogo? jogo)
        {
            if (jogo is null) return;

            _detalhes.Bounds = _grade.Bounds;
            _detalhes.Abrir(jogo);

            _roteador.Contexto = ContextoDeNavegacao.Detalhes;
        }

        private void FecharDetalhes()
        {
            if (!_detalhes.Visible) return;

            _detalhes.Fechar();
            _roteador.Contexto = ContextoDeNavegacao.Grade;

            _grade.Focus();
            _grade.Invalidate();
            AtualizarRodape();
        }

        /// <summary>Passa para o jogo anterior ou seguinte da lista visível, sem fechar.</summary>
        private void FolhearDetalhes(int passo)
        {
            var destino = _grade.IndiceSelecionado + passo;
            if (destino < 0 || destino >= _grade.Jogos.Count) return;

            _grade.Selecionar(destino);
            if (_grade.JogoSelecionado is { } jogo) _detalhes.Abrir(jogo);
        }

        /// <summary>
        /// Liga o polling. É chamado só quando a janela está na frente — o launcher
        /// dormindo enquanto o jogo roda é regra da spec, e um timer de 60 ms em segundo
        /// plano a quebraria sozinho.
        ///
        /// Consequência aceita: janela modal (configurações, revisão do scan) dispara
        /// Deactivate e desliga o controle até eu voltar para a janela principal.
        /// </summary>
        private void IniciarGamepad()
        {
            if (_gamepad is null || IsDisposed) return;

            if (_relogioDoGamepad is null)
            {
                _relogioDoGamepad = new Timer { Interval = GamepadNavegacao.IntervaloDeVarreduraEmMs };
                _relogioDoGamepad.Tick += AoBaterOGamepad;
            }

            _relogioDoGamepad.Start();
        }

        private void PararGamepad()
        {
            _relogioDoGamepad?.Stop();

            // Esquece o que estava pressionado: voltar para o launcher não pode herdar
            // uma direção segurada antes do alt-tab e sair repetindo sozinho.
            _gamepad?.Reiniciar();
        }

        private void DescartarGamepad()
        {
            PararGamepad();

            if (_relogioDoGamepad is null) return;

            _relogioDoGamepad.Tick -= AoBaterOGamepad;
            _relogioDoGamepad.Dispose();
            _relogioDoGamepad = null;
        }

        private void AoBaterOGamepad(object? remetente, EventArgs e)
        {
            if (_gamepad is null || _relogioDoGamepad is null) return;

            var comandos = _gamepad.Ler(DateTime.UtcNow);

            // for com índice: a lista é reaproveitada entre leituras e não vale alocar
            // um enumerador 16 vezes por segundo para quase sempre percorrer zero item.
            for (var i = 0; i < comandos.Count; i++) _roteador.Despachar(comandos[i]);

            // Achou controle: passa a ler um slot só, de 60 em 60 ms. Perdeu: volta para a
            // varredura de 2 s. É isto que mantém o custo perto de zero com nada plugado.
            _relogioDoGamepad.Interval = _gamepad.IntervaloSugeridoEmMs;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            IniciarGamepad();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            IniciarGamepad();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            PararGamepad();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) PararGamepad();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            DescartarGamepad();
        }

        // ---- Diagnóstico do gamepad (só o --autoteste usa) --------------------------------------

        internal bool GamepadPollandoParaDiagnostico => _relogioDoGamepad is { Enabled: true };

        /// <summary>Troca o XInput real por uma fonte falsa, para o teste rodar sem hardware.</summary>
        internal void UsarFonteDeGamepadParaDiagnostico(IEstadoBrutoDeGamepad fonte)
        {
            DescartarGamepad();
            _gamepad = new GamepadNavegacao(fonte, _roteador.Traduzir);
        }

        internal void IniciarGamepadParaDiagnostico() => IniciarGamepad();

        internal void SimularDesativacaoParaDiagnostico() => OnDeactivate(EventArgs.Empty);

        internal void SairDaFrenteDoJogoParaDiagnostico() => SairDaFrenteDoJogo();

        /// <summary>
        /// O F do teclado e o Y do controle. Desde a fase 12 vale para a seleção inteira —
        /// com nada marcado, a "seleção" é o card selecionado e nada muda em relação a antes.
        /// </summary>
        private void AlternarFavorito() => _lote.AlternarFavorito(_grade.SelecaoParaAcao());

        // ---- Configurações (fase 7) ------------------------------------------------------------

        private void AbrirConfiguracoes()
        {
            var trocouDeTema = false;

            using (var janela = new FormConfiguracoes(_config, _biblioteca))
            {
                janela.ShowDialog(this);
                _pedirEstatisticas = janela.PediuEstatisticas;
                _pedirIntegridade = janela.PediuIntegridade;

                if (janela.CacheLimpo)
                {
                    // As miniaturas em memória apontam para arquivos que não existem mais.
                    _grade.LiberarImagens();
                    _grade.Invalidate();
                }

                // Sair sem salvar NÃO pode cair fora do método: os botões "Estatísticas" e
                // "Integridade" fecham a janela sem passar pelo Salvar, e um return aqui
                // engoliria os dois — o clique fecharia as configurações e não abriria nada.
                if (janela.Mudou)
                {
                    SalvarConfig();
                    SalvarBiblioteca();

                    _tamanhoDoCard.SelectedIndex = (int)_config.TamanhoCard;
                    _grade.TamanhoDoCard = _config.TamanhoCard;

                    AplicarFiltros();
                    MostrarAviso("Configurações salvas.");

                    trocouDeTema = janela.TemaMudou;
                }
            }

            if (trocouDeTema) OferecerReabrirPeloTema();

            // O botão "Estatísticas" das configurações abre a tela DEPOIS de a janela de
            // configurações fechar, e não como um modal em cima de outro modal: duas janelas
            // empilhadas para ver um gráfico é o tipo de tela que ninguém fecha na ordem certa.
            if (_pedirEstatisticas)
            {
                _pedirEstatisticas = false;
                AbrirEstatisticas();
            }

            if (_pedirIntegridade)
            {
                _pedirIntegridade = false;
                AbrirIntegridade();
            }
        }

        /// <summary>Marcado pela janela de configurações; consumido logo depois que ela fecha.</summary>
        private bool _pedirEstatisticas;

        /// <summary>O mesmo, para o relatório de integridade da fase 17.</summary>
        private bool _pedirIntegridade;

        /// <summary>
        /// Tema novo escolhido: oferece reabrir o launcher, porque é só reabrindo que ele
        /// aparece (fase 17).
        ///
        /// <b>Dizer a verdade em vez de repintar meia janela.</b> Cada controle copia as
        /// cores do tema no próprio construtor — reaplicar a paleta agora trocaria o fundo
        /// dos painéis desenhados à mão e deixaria botão, combo e campo de busca com as
        /// cores antigas. Uma tela metade escura e metade clara é pior que uma pergunta.
        ///
        /// Reabrir aqui é seguro: a configuração e a biblioteca acabaram de ir para o
        /// disco, e com jogo aberto a pergunta nem é feita.
        /// </summary>
        private void OferecerReabrirPeloTema()
        {
            if (_lancador.JogoRodando)
            {
                MostrarAviso("O tema novo aparece quando o launcher for reaberto.");
                return;
            }

            var resposta = MessageBox.Show(this,
                "O tema novo aparece quando o launcher reabrir." + Environment.NewLine + Environment.NewLine +
                "Reabrir agora? Já está tudo salvo.",
                "Tema", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (resposta != DialogResult.Yes)
            {
                MostrarAviso("O tema novo aparece na próxima vez que o launcher abrir.");
                return;
            }

            try
            {
                Application.Restart();
            }
            catch (Exception erro)
            {
                MostrarAviso($"Não consegui reabrir sozinho ({erro.Message}). " +
                             "Feche e abra o launcher para ver o tema novo.");
            }
        }

        // ---- Estatísticas (fase 13) --------------------------------------------------------------

        /// <summary>
        /// A tela de estatísticas. É aqui (e na tela de detalhes) que <c>sessoes.json</c>
        /// chega a ser lido — nunca na abertura.
        /// </summary>
        private void AbrirEstatisticas()
        {
            // Com um jogo aberto o launcher está escondido e dormindo. Abrir janela agora
            // seria acordar por cima do jogo em tela cheia.
            if (_lancador.JogoRodando) return;

            FecharDetalhes();

            using (var janela = new FormEstatisticas(ObterSessoes(), _biblioteca))
                janela.ShowDialog(this);
        }

        // ---- Integridade do acervo (fase 17) -------------------------------------------------

        /// <summary>
        /// O relatório de integridade. Varre em thread, conserta só o que eu mandar, e o
        /// caminho para a religação da fase 10 é o próprio F6 — ver <c>FormIntegridade</c>.
        /// </summary>
        private void AbrirIntegridade()
        {
            if (_lancador.JogoRodando) return;

            FecharDetalhes();

            var escanear = false;

            using (var janela = new FormIntegridade(this, _acoes))
            {
                janela.ShowDialog(this);
                escanear = janela.PediuEscanear;
            }

            // O scan abre a própria janela de progresso e, no fim, a de revisão. Fazer isso
            // por cima do relatório seria a terceira janela modal empilhada.
            if (escanear) EscanearJogos();
        }

        // ---- Capas (fase 6) ------------------------------------------------------------------

        /// <summary>
        /// Menu do botão direito no card. As três entradas manuais da spec mais o lote.
        /// Montado uma vez e reaproveitado; o que muda por jogo é o estado dos itens.
        /// </summary>
        private ContextMenuStrip CriarMenuDoCard()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Tema.Superficie,
                ForeColor = Tema.Texto,
                ShowImageMargin = false,
                Renderer = new RenderizadorEscuro()
            };

            // Ações do jogo (fase 10).
            var editar = new ToolStripMenuItem("Editar título / argumentos...", null,
                (_, _) => ComJogoSelecionado(_acoes.EditarJogo));
            var abrirPasta = new ToolStripMenuItem("Abrir pasta do jogo", null,
                (_, _) => ComJogoSelecionado(_acoes.AbrirPastaDoJogo));
            var atalho = new ToolStripMenuItem("Criar atalho na área de trabalho...", null,
                (_, _) => ComJogoSelecionado(_acoes.CriarAtalhoNaAreaDeTrabalho));
            var fixar = new ToolStripMenuItem("Definir como executável principal", null,
                (_, _) => ComJogoSelecionado(_acoes.DefinirComoExecutavelPrincipal));
            _itemLocalizar = new ToolStripMenuItem("Localizar executável...", null,
                (_, _) => ComJogoSelecionado(jogo => _acoes.OferecerLocalizarExecutavel(
                    jogo, $"\"{jogo.Titulo}\" está marcado como não encontrado.")));

            // Capas (fase 6).
            _itemBuscarOnline = new ToolStripMenuItem("Buscar capa online...", null, (_, _) => _acoes.BuscarCapaOnline());
            var doArquivo = new ToolStripMenuItem("Escolher capa do arquivo...", null, (_, _) => _acoes.EscolherCapaDeArquivo());
            _itemColar = new ToolStripMenuItem("Colar capa da área de transferência", null, (_, _) => _acoes.ColarCapa());
            var doJogo = new ToolStripMenuItem("Usar arte da pasta do jogo", null, (_, _) => _acoes.UsarArteLocal());
            var removerCapa = new ToolStripMenuItem("Remover capa", null, (_, _) => _acoes.RemoverCapa());
            _itemLote = new ToolStripMenuItem("Baixar capas que faltam...", null, (_, _) => _acoes.BaixarCapasEmLote());

            var removerJogo = new ToolStripMenuItem("Remover da biblioteca...", null,
                (_, _) => _lote.Remover(_grade.SelecaoParaAcao()));

            // ---- Ações que valem para a seleção inteira (fase 12) ----
            //
            // Os mesmos itens servem para um jogo e para trinta: quem resolve o "quantos"
            // é SelecaoParaAcao, e não uma segunda versão de cada item de menu.
            _cabecalhoDaSelecao = new ToolStripMenuItem("") { Enabled = false };

            var aplicarTag = new ToolStripMenuItem("Aplicar tag...", null,
                (_, _) => _lote.AplicarTag(_grade.SelecaoParaAcao()));
            var removerTag = new ToolStripMenuItem("Remover tag...", null,
                (_, _) => _lote.RemoverTag(_grade.SelecaoParaAcao()));

            var favoritar = new ToolStripMenuItem("Favoritar / desfavoritar (F)", null,
                (_, _) => _lote.AlternarFavorito(_grade.SelecaoParaAcao()));

            var nota = new ToolStripMenuItem("Nota");
            for (var estrelas = 0; estrelas <= 5; estrelas++)
            {
                var valor = estrelas;
                nota.DropDownItems.Add(new ToolStripMenuItem(
                    valor == 0 ? "Sem nota" : new string('★', valor), null,
                    (_, _) => _lote.DefinirNota(_grade.SelecaoParaAcao(), valor)));
            }

            var status = new ToolStripMenuItem("Status");
            foreach (var valor in Estados.Todos)
            {
                var escolhido = valor;
                status.DropDownItems.Add(new ToolStripMenuItem(Estados.Descrever(escolhido), null,
                    (_, _) => _lote.DefinirStatus(_grade.SelecaoParaAcao(), escolhido)));
            }

            menu.Items.Add(_cabecalhoDaSelecao);
            menu.Items.Add(editar);
            menu.Items.Add(abrirPasta);
            menu.Items.Add(atalho);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(aplicarTag);
            menu.Items.Add(removerTag);
            menu.Items.Add(nota);
            menu.Items.Add(status);
            menu.Items.Add(favoritar);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(fixar);
            menu.Items.Add(_itemLocalizar);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_itemBuscarOnline);
            menu.Items.Add(doArquivo);
            menu.Items.Add(_itemColar);
            menu.Items.Add(doJogo);
            menu.Items.Add(removerCapa);
            menu.Items.Add(_itemLote);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(removerJogo);

            var deUmJogoSo = new ToolStripItem[]
            {
                editar, abrirPasta, atalho, fixar, doArquivo, doJogo, removerCapa
            };

            menu.Opening += (_, e) =>
            {
                if (_grade.JogoSelecionado is not { } jogo) { e.Cancel = true; return; }

                var comChave = _config.TemChaveSteamGridDb();
                _itemBuscarOnline!.Enabled = comChave;
                _itemBuscarOnline.ToolTipText = comChave
                    ? ""
                    : "Configure a chave do SteamGridDB em F10 para habilitar a busca online.";

                _itemLote!.Enabled = comChave;
                _itemColar!.Enabled = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();

                // "Localizar executável..." é o caminho de religar um card marcado como não
                // encontrado. Com o jogo no lugar, não há o que localizar.
                _itemLocalizar!.Visible = !jogo.ExecutavelExiste();

                // Com vários marcados, some tudo que só faz sentido para um: editar título
                // de trinta jogos ou abrir trinta pastas do Explorer não é ação em lote, é
                // acidente.
                var marcados = _grade.QuantidadeMarcada;
                var emLote = marcados > 1;

                _cabecalhoDaSelecao!.Visible = emLote;
                _cabecalhoDaSelecao.Text = $"{marcados} jogos selecionados";

                foreach (var item in deUmJogoSo) item.Visible = !emLote;

                removerJogo.Text = emLote
                    ? $"Remover {marcados} da biblioteca..."
                    : "Remover da biblioteca...";
            };

            return menu;
        }

        /// <summary>
        /// Roda a ação no card selecionado. Todo item do menu passa por aqui: o menu só
        /// abre com um jogo selecionado, mas a verificação vale o custo de uma linha.
        /// </summary>
        private void ComJogoSelecionado(Action<Jogo> acao)
        {
            if (_grade.JogoSelecionado is { } jogo) acao(jogo);
        }

        /// <summary>
        /// Arrastar imagem para cima de um card define a capa dele. O card sob o cursor
        /// manda, não o selecionado — é o que a mão espera.
        /// </summary>
        private void PrepararArrastarESoltar()
        {
            _grade.AllowDrop = true;

            _grade.DragEnter += (_, e) => e.Effect = EfeitoDoArrasto(e, sobreCard: true);
            _grade.DragOver += (_, e) =>
            {
                var ponto = _grade.PointToClient(new Point(e.X, e.Y));
                e.Effect = EfeitoDoArrasto(e, _grade.IndiceEmPonto(ponto) >= 0);
            };

            _grade.DragDrop += (_, e) =>
            {
                // Imagem sobre um card é capa; imagem fora de card não é nada — sem card,
                // não há a quem aplicar.
                if (AcoesDoJogo.ArquivoDeImagemArrastado(e.Data) is { } imagem)
                {
                    var ponto = _grade.PointToClient(new Point(e.X, e.Y));
                    if (_grade.JogoEmPonto(ponto) is { } jogo) _acoes.AplicarCapaDeArquivo(jogo, imagem);
                    return;
                }

                // Executável em qualquer lugar da janela: jogo novo.
                if (AcoesDoJogo.ExecutavelArrastado(e.Data) is { } executavel)
                {
                    if (_acoes.AdicionarDeArquivo(executavel) is { } novo) _grade.Selecionar(IndiceDe(novo));
                    return;
                }

                // Pasta: escaneia só ela e abre a revisão, em vez de adivinhar qual dos
                // executáveis de dentro é o jogo.
                if (AcoesDoJogo.PastaArrastada(e.Data) is { } pasta) EscanearPastas(new List<string> { pasta });
            };
        }

        /// <summary>
        /// O que o arrasto pode virar. Imagem só vale sobre um card; executável e pasta
        /// valem em qualquer lugar da janela.
        /// </summary>
        private static DragDropEffects EfeitoDoArrasto(DragEventArgs e, bool sobreCard)
        {
            if (sobreCard && AcoesDoJogo.ArquivoDeImagemArrastado(e.Data) != null) return DragDropEffects.Copy;

            return AcoesDoJogo.ExecutavelArrastado(e.Data) != null || AcoesDoJogo.PastaArrastada(e.Data) != null
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private int IndiceDe(Jogo jogo)
        {
            for (var i = 0; i < _grade.Jogos.Count; i++)
            {
                if (ReferenceEquals(_grade.Jogos[i], jogo)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Sorteia um jogo entre os que estão à vista e abre. Existe porque 200 capas na
        /// tela é bom para olhar e ruim para decidir.
        ///
        /// Sorteia só dentro do filtro atual — se eu filtrei por "corrida", o acaso é entre
        /// corridas — e nunca escolhe um card marcado como não encontrado.
        /// </summary>
        private void SortearJogo()
        {
            if (SorteioDaGrade.Escolher(_grade.Jogos, _sorteio) is not { } escolhido)
            {
                MostrarAviso("Não há jogo disponível para sortear no filtro atual.");
                return;
            }

            _grade.Selecionar(IndiceDe(escolhido));
            MostrarAviso($"A sorte escolheu \"{escolhido.Titulo}\".");
            Jogar(escolhido);
        }

        private readonly Random _sorteio = new Random();

        // ---- Lançar o jogo (fase 5) --------------------------------------------------------

        /// <summary>
        /// Lançar e sumir da frente. A regra do lançamento (salvar antes, tratar executável
        /// sumido, recusar dois jogos ao mesmo tempo) mora em <see cref="AcoesDoJogo"/>;
        /// esconder a janela continua sendo assunto de quem tem janela.
        /// </summary>
        private void Jogar(Jogo jogo)
        {
            if (!_acoes.Lancar(jogo)) return;

            _grade.IdEmExecucao = jogo.Id;
            SairDaFrenteDoJogo();
        }

        /// <summary>
        /// Esconde a janela e devolve a RAM ao sistema. Enquanto o jogo roda, o launcher
        /// não tem timer, thread ativa nem nada para desenhar — só um handle de processo
        /// esperando o evento de saída.
        /// </summary>
        private void SairDaFrenteDoJogo()
        {
            // A arte grande dos detalhes é a maior imagem que o launcher segura. Ela sai
            // junto com as miniaturas: com o jogo aberto, nada nosso fica em memória.
            FecharDetalhes();

            Hide();

            // O timer do gamepad é a única coisa neste processo capaz de acordar sozinha.
            // Ele para aqui, explicitamente, e não só pelo Deactivate: "enquanto o jogo
            // roda, o launcher dorme" não pode depender de qual evento o Windows manda.
            PararGamepad();

            // Os bitmaps da grade são o grosso da memória. Escondido, nenhum deles está
            // à vista: soltar todos antes de coletar é o que faz o working set despencar.
            _grade.LiberarImagens();
            Memoria.DevolverRamAoSistema();
        }

        /// <summary>
        /// Fim da sessão. Chega pelo evento Process.Exited, numa thread qualquer — daí o
        /// salto para a thread da UI antes de mexer em janela.
        /// </summary>
        private void AoTerminarSessao(object? remetente, SessaoTerminadaEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((Action)(() => VoltarDoJogo(e)));
            }
            catch (Exception)
            {
                // Launcher fechando enquanto o jogo terminava.
            }
        }

        private void VoltarDoJogo(SessaoTerminadaEventArgs sessao)
        {
            if (IsDisposed) return;

            // Biblioteca de versão mais nova: a sessão não é contabilizada nem gravada — e
            // é o rodapé que avisou disso antes de eu abrir o jogo. Creditar em memória
            // seria pior: o tempo apareceria na tela e sumiria no primeiro F5.
            var resultado = _biblioteca.SomenteLeitura
                ? null
                : ContabilizadorDeTempo.Contabilizar(sessao.Jogo, sessao.Duracao, DateTime.UtcNow);

            if (resultado is not null) SalvarBiblioteca();

            // O histórico da fase 13 vem depois da biblioteca e pela mesma condição: em
            // modo somente-leitura `resultado` é nulo, ContabilizadorDeTempo.ParaHistorico
            // devolve nulo, e nada é gravado nem aqui nem lá — que é o que o rodapé avisou
            // antes de eu abrir o jogo. Sessão curta (o caso do processo-filho) também
            // devolve nulo: a regra dos 5 segundos tem um dono só.
            RegistrarSessao(sessao, resultado);

            _grade.IdEmExecucao = null;
            Reaparecer(semRoubarFoco: resultado is { SaidaImediata: true });

            AplicarFiltros();   // a ordenação "jogados recentemente" acabou de mudar

            // Aviso discreto: vai para o rodapé, não para uma caixa de diálogo. Jogo que
            // abre um launcher próprio e morre na hora é normal, não é erro.
            _avisoDaSessao = resultado is { SaidaImediata: true }
                ? $"\"{sessao.Jogo.Titulo}\" fechou em {sessao.Duracao.TotalSeconds:F0} s e não achei " +
                  "outro processo dele. Se o jogo abriu mesmo assim, é ele rodando por fora — não contei o tempo."
                : "";

            AtualizarRodape();
        }

        /// <summary>
        /// Grava a sessão em <c>_mochila\sessoes.json</c>, na hora.
        ///
        /// "Na hora" é regra: o arquivo é a memória do acervo e não vale a pena arriscá-lo a
        /// uma queda de energia para economizar uma escrita de 300 KB. E a gravação carrega o
        /// histórico do disco se ele ainda não estava carregado — o que é aceitável aqui e
        /// não seria na abertura: neste ponto o jogo já terminou e ninguém está esperando a
        /// grade aparecer.
        /// </summary>
        private void RegistrarSessao(SessaoTerminadaEventArgs sessao, ResultadoDaSessao? resultado)
        {
            if (ContabilizadorDeTempo.ParaHistorico(sessao.Jogo, sessao.InicioUtc, resultado) is not { } registro)
                return;

            try
            {
                ObterSessoes().Registrar(registro);
            }
            catch (Exception)
            {
                // Estatística nunca atrapalha o caminho de jogar: o acumulado do jogo já foi
                // para a biblioteca, e é ele que ordena a grade.
            }
        }

        /// <summary>
        /// Volta para a tela. Depois de uma saída rápida, volta SEM roubar o foco: esse
        /// caso existe justamente porque o jogo pode estar subindo em tela cheia, e
        /// aparecer na frente dele é pior que o problema que eu estava resolvendo.
        /// </summary>
        private void Reaparecer(bool semRoubarFoco)
        {
            _mostrarSemAtivar = semRoubarFoco;

            try
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                if (!semRoubarFoco) Activate();
            }
            finally
            {
                _mostrarSemAtivar = false;
            }
        }

        /// <summary>
        /// O WinForms consulta isto ao exibir a janela e usa SW_SHOWNOACTIVATE quando é
        /// true. Fica em campo mutável porque só a volta de uma saída rápida quer isso.
        /// </summary>
        protected override bool ShowWithoutActivation => _mostrarSemAtivar;

        /// <summary>
        /// O launcher próprio morreu, mas o jogo de verdade foi encontrado e está rodando.
        /// Nada a fazer além de continuar escondido — e o card segue travado.
        /// </summary>
        /// <summary>
        /// Um gancho da fase 15 deu errado sem impedir nada. Vai para o rodapé, e nunca
        /// para uma caixa de diálogo: o jogo já está abrindo (ou já fechou), e um modal
        /// aqui seria uma janela pedindo OK por cima de um jogo em tela cheia.
        ///
        /// O aviso do "antes" chega na própria thread da UI, dentro do clique que lançou o
        /// jogo; o do "depois" vem da thread do <c>Process.Exited</c>. Daí o teste de
        /// <see cref="Control.InvokeRequired"/> em vez de um BeginInvoke incondicional.
        /// </summary>
        private void AoAvisarDeScript(object? remetente, string aviso)
        {
            if (IsDisposed || string.IsNullOrEmpty(aviso)) return;

            if (!IsHandleCreated || !InvokeRequired)
            {
                MostrarAviso(aviso);
                return;
            }

            try
            {
                BeginInvoke((Action)(() => { if (!IsDisposed) MostrarAviso(aviso); }));
            }
            catch (Exception)
            {
                // Launcher fechando enquanto o script terminava.
            }
        }

        private void AoAdotarProcessoFilho(object? remetente, Jogo jogo)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed) _grade.IdEmExecucao = jogo.Id;
                }));
            }
            catch (Exception)
            {
                // Launcher fechando.
            }
        }

        private void MostrarAviso(string texto)
        {
            _avisoDaSessao = texto;
            AtualizarRodape();
        }

        private void SalvarBiblioteca()
        {
            try
            {
                _biblioteca.Salvar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não consegui gravar a biblioteca: {ex.Message}",
                    "Biblioteca", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- Scan (fase 3) ---------------------------------------------------------------------

        /// <summary>
        /// Escolher pasta -> escanear em thread separada -> revisar -> gravar.
        /// Cada etapa pode ser abortada, e nada é gravado antes da confirmação.
        /// </summary>
        private void EscanearJogos() => EscanearPastas(EscolherPastasParaEscanear(_biblioteca));

        /// <summary>
        /// O scan propriamente dito, já com as pastas decididas. Separado do
        /// <see cref="EscanearJogos"/> porque arrastar uma pasta para a janela (fase 10)
        /// entra por aqui, sem passar pela escolha de pasta.
        /// </summary>
        private void EscanearPastas(List<string> raizes)
        {
            if (raizes.Count == 0) return;

            // A mesclagem pode trocar o executável ou remover a entrada que o painel está
            // mostrando. Fechar antes é mais honesto que redesenhar dado velho.
            FecharDetalhes();

            List<JogoDetectado> detectados;
            IReadOnlyList<PastaDescartada> descartes;

            using (var progresso = new FormProgressoDoScan(raizes, FiltroDeExclusao.De(_config)))
            {
                var resultado = progresso.ShowDialog(this);

                if (progresso.Falha is { } falha)
                {
                    MessageBox.Show(this, $"O scan falhou: {falha.Message}", "Escanear jogos",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (resultado != DialogResult.OK || progresso.Cancelado) return;

                detectados = progresso.Jogos;
                descartes = progresso.Descartes;
            }

            if (detectados.Count == 0)
            {
                MessageBox.Show(this,
                    $"Nenhum jogo encontrado.{Environment.NewLine}" +
                    $"{descartes.Count} pasta(s) foram olhadas e descartadas.",
                    "Escanear jogos", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var revisao = new FormRevisaoDoScan(detectados, descartes, _biblioteca))
            {
                if (revisao.ShowDialog(this) != DialogResult.OK) return;

                GravarRevisao(revisao.Confirmados(), raizes);
            }
        }

        /// <summary>
        /// Pastas já escaneadas antes viram o padrão; senão eu escolho na hora. A pasta
        /// precisa estar no mesmo HD: jogo de outro drive não teria caminho relativo e
        /// quebraria no próximo PC.
        /// </summary>
        private List<string> EscolherPastasParaEscanear(Biblioteca biblioteca)
        {
            var conhecidas = new List<string>();
            foreach (var relativa in biblioteca.PastasEscaneadas)
            {
                var absoluta = Caminhos.ParaAbsolutoOuNulo(relativa);
                if (absoluta != null && Directory.Exists(absoluta)) conhecidas.Add(absoluta);
            }

            if (conhecidas.Count > 0)
            {
                var pergunta = MessageBox.Show(this,
                    $"Reescanear as pastas já cadastradas?{Environment.NewLine}{Environment.NewLine}" +
                    string.Join(Environment.NewLine, conhecidas.ToArray()) + Environment.NewLine + Environment.NewLine +
                    "Sim reescaneia essas. Não deixa você escolher outra pasta.",
                    "Escanear jogos", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (pergunta == DialogResult.Cancel) return new List<string>();
                if (pergunta == DialogResult.Yes) return conhecidas;
            }

            using (var dialogo = new FolderBrowserDialog
                   {
                       Description = "Escolha a pasta com os jogos (qualquer pasta do mesmo HD do launcher).",
                       SelectedPath = Caminhos.PastaBase,
                       ShowNewFolderButton = false
                   })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK) return new List<string>();

                if (!Caminhos.TentarParaRelativo(dialogo.SelectedPath, out _))
                {
                    MessageBox.Show(this,
                        $"A pasta \"{dialogo.SelectedPath}\" está em outro drive.{Environment.NewLine}" +
                        $"O launcher está em \"{Caminhos.PastaBase}\".{Environment.NewLine}{Environment.NewLine}" +
                        "Só dá para catalogar jogos do mesmo HD — caminho de outro drive viraria " +
                        "letra fixa no JSON e quebraria no próximo PC. Pasta irmã do launcher " +
                        "(por exemplo \"Jogos\" ao lado dele) funciona normalmente.",
                        "Escanear jogos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new List<string>();
                }

                return new List<string> { dialogo.SelectedPath };
            }
        }

        private void GravarRevisao(List<JogoRevisado> confirmados, List<string> raizes)
        {
            var resumo = MescladorDeBiblioteca.Mesclar(_biblioteca, confirmados);
            MescladorDeBiblioteca.RegistrarPastasEscaneadas(_biblioteca, raizes);

            try
            {
                _biblioteca.Salvar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não consegui gravar a biblioteca: {ex.Message}",
                    "Escanear jogos", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AplicarFiltros();

            var detalhe = resumo.ForaDaRaiz.Count > 0
                ? Environment.NewLine + Environment.NewLine +
                  "Fora da pasta do launcher (não gravados): " + string.Join(", ", resumo.ForaDaRaiz.ToArray())
                : "";

            MessageBox.Show(this, resumo.Resumir() + detalhe, "Biblioteca atualizada",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DescartarGamepad();
                _lancador.SessaoTerminada -= AoTerminarSessao;
                _lancador.ProcessoFilhoAdotado -= AoAdotarProcessoFilho;
                _lancador.AvisoDeScript -= AoAvisarDeScript;
                _lancador.Dispose();        // solta o handle; o jogo aberto continua vivo
                _miniaturas.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
