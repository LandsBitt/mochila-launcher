using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Launcher.Capas;
using Launcher.Dados;
using Launcher.Execucao;
using Launcher.Modelo;
using Launcher.Scanner;
using Launcher.UI;
using Launcher.Util;

namespace Launcher
{
    /// <summary>
    /// A janela do launcher: grade de capas, busca, ordenação e o scan.
    ///
    /// Tudo é desenhado num controle só (<see cref="GradeDeCapas"/>), e só as imagens
    /// visíveis ficam na memória — o launcher precisa caber num notebook fraco e não
    /// pode roubar recurso do jogo.
    /// </summary>
    public sealed class FormPrincipal : Form
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

        private ToolStripMenuItem? _itemBuscarOnline;
        private ToolStripMenuItem? _itemColar;
        private ToolStripMenuItem? _itemLote;

        private Biblioteca _biblioteca = new Biblioteca();
        private Config _config = new Config();

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

        public FormPrincipal()
        {
            Text = "Launcher de jogos";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 700);
            MinimumSize = new Size(700, 460);
            DoubleBuffered = true;
            KeyPreview = true;

            // Cores, ícone do gamepad e barra de título escura, tudo do mesmo lugar.
            Tema.AplicarNaJanela(this);

            _grade = new GradeDeCapas(_miniaturas) { Dock = DockStyle.Fill };
            _grade.JogoAcionado += (_, jogo) => Jogar(jogo);
            // Qualquer navegação minha limpa o recado da sessão anterior.
            _grade.SelecaoMudou += (_, _) => { _avisoDaSessao = ""; AtualizarRodape(); };

            _lancador.SessaoTerminada += AoTerminarSessao;
            _lancador.ProcessoFilhoAdotado += AoAdotarProcessoFilho;

            _capas = new GerenciadorDeCapas(_miniaturas);
            _grade.ContextMenuStrip = CriarMenuDoCard();
            PrepararArrastarESoltar();

            _busca = CriarBusca();
            _ordenacao = CriarOrdenacao();
            _somenteFavoritos = CriarFiltroDeFavoritos();
            _tamanhoDoCard = CriarTamanhoDoCard();
            _rodape = CriarRodape();

            Controls.Add(_grade);
            Controls.Add(CriarBarraSuperior());
            Controls.Add(_rodape);

            KeyDown += AoTeclar;

            Carregar();
            _busca.Select();
        }

        // ---- Barra superior ------------------------------------------------------------------

        /// <summary>
        /// A barra superior. Os controles ficam em posição fixa à esquerda e os botões
        /// acompanham a borda direita — nada de Dock, que empilharia os dois botões
        /// grudados e ignoraria a margem entre eles.
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

            painel.Controls.Add(_busca);
            painel.Controls.Add(_ordenacao);
            painel.Controls.Add(_somenteFavoritos);
            painel.Controls.Add(_tamanhoDoCard);
            painel.Controls.Add(configurar);
            painel.Controls.Add(escanear);

            painel.Emoldurar(_busca, comLupa: true);

            void AlinharADireita()
            {
                escanear.Left = painel.ClientSize.Width - 12 - escanear.Width;
                configurar.Left = escanear.Left - 8 - configurar.Width;
            }

            painel.Resize += (_, _) => AlinharADireita();
            AlinharADireita();

            return painel;
        }

        /// <summary>Altura da barra superior, e a régua vertical de tudo que mora nela.</summary>
        private const int AlturaDaBarra = 58;

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
                // Primeiro uso num HD novo: cria _launcher\, a biblioteca e o config padrão.
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
                    "Launcher de jogos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            try
            {
                _config = Config.Carregar();
            }
            catch (Exception)
            {
                _config = new Config();   // preferência ilegível não impede de abrir
            }

            _biblioteca = CarregarBiblioteca();

            _ordenacao.SelectedIndex = (int)_config.Ordenacao;
            _somenteFavoritos.Checked = _config.SomenteFavoritos;
            _tamanhoDoCard.SelectedIndex = (int)_config.TamanhoCard;
            _grade.TamanhoDoCard = _config.TamanhoCard;

            AplicarFiltros();
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

                var nome = Path.GetFileName(caminho) +
                           DateTime.Now.ToString(".corrompido-yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

                File.Move(caminho, Path.Combine(Path.GetDirectoryName(caminho)!, nome));
                return nome;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void AplicarFiltros()
        {
            var visiveis = FiltroDaBiblioteca.Aplicar(
                _biblioteca.Jogos, _busca.Text, _config.Ordenacao, _config.SomenteFavoritos);

            _grade.DefinirJogos(visiveis);
            AtualizarRodape();
        }

        private void AtualizarRodape()
        {
            var selecionado = _grade.JogoSelecionado;
            var total = $"{_grade.Jogos.Count} de {_biblioteca.Jogos.Count} jogo(s)";

            // O aviso da última sessão tem prioridade: é a única coisa que eu preciso ler
            // no instante em que a janela reaparece.
            if (_avisoDaSessao.Length > 0)
            {
                _rodape.Definir(total, "", _avisoDaSessao, alerta: true);
                return;
            }

            if (selecionado is null)
            {
                _rodape.Definir(total, "",
                    "Setas para navegar, Enter para jogar, F para favoritar, F6 para escanear.", alerta: false);
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
                    Carregar();
                    e.Handled = true;
                    return;

                case Keys.F6:
                    EscanearJogos();
                    e.Handled = true;
                    return;

                case Keys.F10:
                    AbrirConfiguracoes();
                    e.Handled = true;
                    return;

                case Keys.Escape:
                    // Esc limpa a busca; com a busca já vazia, sai.
                    if (_busca.Text.Length > 0) _busca.Clear();
                    else Close();
                    e.Handled = true;
                    return;

                case Keys.F when !_busca.Focused:
                    AlternarFavorito();
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

            // Ctrl+←, Shift+End e afins são edição de texto: nunca viram navegação.
            // E só roteamos o que vem da busca ou da própria grade — um combo aberto
            // precisa das setas dele.
            var origemRoteavel = ActiveControl == _busca || ActiveControl == _grade || ActiveControl is null;

            if (modificadores == Keys.None && origemRoteavel &&
                RoteadorDeTeclas.VaiParaGrade(chave, ActiveControl == _busca, _busca.TextLength > 0) &&
                _grade.TratarTecla(chave))
            {
                return true;
            }

            return base.ProcessCmdKey(ref mensagem, combinacao);
        }

        private void AlternarFavorito()
        {
            if (_grade.JogoSelecionado is not { } jogo) return;

            jogo.Favorito = !jogo.Favorito;
            SalvarBiblioteca();

            if (_config.SomenteFavoritos) AplicarFiltros();
            else _grade.Invalidate();

            AtualizarRodape();
        }

        // ---- Configurações (fase 7) ------------------------------------------------------------

        private void AbrirConfiguracoes()
        {
            using (var janela = new FormConfiguracoes(_config, _biblioteca))
            {
                janela.ShowDialog(this);

                if (janela.CacheLimpo)
                {
                    // As miniaturas em memória apontam para arquivos que não existem mais.
                    _grade.LiberarImagens();
                    _grade.Invalidate();
                }

                if (!janela.Mudou) return;

                SalvarConfig();
                SalvarBiblioteca();

                _tamanhoDoCard.SelectedIndex = (int)_config.TamanhoCard;
                _grade.TamanhoDoCard = _config.TamanhoCard;

                AplicarFiltros();
                MostrarAviso("Configurações salvas.");
            }
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

            _itemBuscarOnline = new ToolStripMenuItem("Buscar capa online...", null, (_, _) => BuscarCapaOnline());
            var doArquivo = new ToolStripMenuItem("Escolher capa do arquivo...", null, (_, _) => EscolherCapaDeArquivo());
            _itemColar = new ToolStripMenuItem("Colar capa da área de transferência", null, (_, _) => ColarCapa());
            var doJogo = new ToolStripMenuItem("Usar arte da pasta do jogo", null, (_, _) => UsarArteLocal());
            var remover = new ToolStripMenuItem("Remover capa", null, (_, _) => RemoverCapa());
            _itemLote = new ToolStripMenuItem("Baixar capas que faltam...", null, (_, _) => BaixarCapasEmLote());

            menu.Items.Add(_itemBuscarOnline);
            menu.Items.Add(doArquivo);
            menu.Items.Add(_itemColar);
            menu.Items.Add(doJogo);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(remover);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_itemLote);

            menu.Opening += (_, e) =>
            {
                if (_grade.JogoSelecionado is null) { e.Cancel = true; return; }

                var comChave = _config.TemChaveSteamGridDb();
                _itemBuscarOnline!.Enabled = comChave;
                _itemBuscarOnline.ToolTipText = comChave
                    ? ""
                    : "Configure a chave do SteamGridDB para habilitar a busca online.";

                _itemLote!.Enabled = comChave;
                _itemColar!.Enabled = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();
            };

            return menu;
        }

        /// <summary>
        /// Arrastar imagem para cima de um card define a capa dele. O card sob o cursor
        /// manda, não o selecionado — é o que a mão espera.
        /// </summary>
        private void PrepararArrastarESoltar()
        {
            _grade.AllowDrop = true;

            _grade.DragEnter += (_, e) =>
                e.Effect = ArquivoDeImagemArrastado(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;

            _grade.DragOver += (_, e) =>
            {
                var ponto = _grade.PointToClient(new Point(e.X, e.Y));
                var sobreCard = _grade.IndiceEmPonto(ponto) >= 0;

                e.Effect = sobreCard && ArquivoDeImagemArrastado(e.Data) != null
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            };

            _grade.DragDrop += (_, e) =>
            {
                var arquivo = ArquivoDeImagemArrastado(e.Data);
                if (arquivo is null) return;

                var ponto = _grade.PointToClient(new Point(e.X, e.Y));
                if (_grade.JogoEmPonto(ponto) is not { } jogo) return;

                AplicarCapaDeArquivo(jogo, arquivo);
            };
        }

        private static readonly string[] ExtensoesAceitas = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".ico" };

        private static string? ArquivoDeImagemArrastado(IDataObject? dados)
        {
            if (dados?.GetData(DataFormats.FileDrop) is not string[] arquivos) return null;

            foreach (var arquivo in arquivos)
            {
                var extensao = Path.GetExtension(arquivo).ToLowerInvariant();

                foreach (var aceita in ExtensoesAceitas)
                {
                    if (extensao == aceita) return arquivo;
                }
            }
            return null;
        }

        private void AplicarCapaDeArquivo(Jogo jogo, string caminho)
        {
            try
            {
                _capas.AplicarDeArquivo(jogo, caminho);
                SalvarBiblioteca();
                _grade.Invalidate();
                MostrarAviso($"Capa de \"{jogo.Titulo}\" atualizada.");
            }
            catch (Exception erro)
            {
                MessageBox.Show(this, erro.Message, "Capa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EscolherCapaDeArquivo()
        {
            if (_grade.JogoSelecionado is not { } jogo) return;

            using (var dialogo = new OpenFileDialog
                   {
                       Title = $"Capa de {jogo.Titulo}",
                       Filter = "Imagens|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.ico|Todos os arquivos|*.*",
                       CheckFileExists = true
                   })
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK) AplicarCapaDeArquivo(jogo, dialogo.FileName);
            }
        }

        private void ColarCapa()
        {
            if (_grade.JogoSelecionado is not { } jogo) return;

            try
            {
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } imagem)
                {
                    using (imagem)
                    {
                        _capas.AplicarImagem(jogo, imagem);
                        SalvarBiblioteca();
                        _grade.Invalidate();
                        MostrarAviso($"Capa de \"{jogo.Titulo}\" colada da área de transferência.");
                    }
                    return;
                }

                // Copiar um arquivo no Explorer também é "colar uma capa".
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (var arquivo in Clipboard.GetFileDropList())
                    {
                        if (arquivo is null) continue;

                        var extensao = Path.GetExtension(arquivo).ToLowerInvariant();
                        foreach (var aceita in ExtensoesAceitas)
                        {
                            if (extensao != aceita) continue;

                            AplicarCapaDeArquivo(jogo, arquivo);
                            return;
                        }
                    }
                }

                MostrarAviso("Não há imagem na área de transferência.");
            }
            catch (Exception erro)
            {
                MessageBox.Show(this, $"Não consegui usar o que está na área de transferência: {erro.Message}",
                    "Capa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Fallback da spec sob demanda: arte solta na pasta, senão ícone do exe.</summary>
        private void UsarArteLocal()
        {
            if (_grade.JogoSelecionado is not { } jogo) return;

            var origem = _capas.AplicarFallbackLocal(jogo);

            if (origem == OrigemDaCapa.Nenhuma)
            {
                MostrarAviso($"Não achei arte na pasta de \"{jogo.Titulo}\" nem ícone no executável.");
                return;
            }

            SalvarBiblioteca();
            _grade.Invalidate();

            MostrarAviso(origem == OrigemDaCapa.PastaDoJogo
                ? $"Capa de \"{jogo.Titulo}\" veio de um arquivo da pasta do jogo."
                : $"Capa de \"{jogo.Titulo}\" veio do ícone do executável.");
        }

        private void RemoverCapa()
        {
            if (_grade.JogoSelecionado is not { } jogo) return;

            try
            {
                var capa = jogo.CaminhoCapa();
                if (capa != null && File.Exists(capa)) File.Delete(capa);

                var thumb = jogo.CaminhoThumbnail();
                if (File.Exists(thumb)) File.Delete(thumb);
            }
            catch (Exception)
            {
                // Arquivo preso: o importante é a biblioteca deixar de apontar para ele.
            }

            jogo.CapaArquivo = null;
            _miniaturas.Invalidar(jogo.Id);

            SalvarBiblioteca();
            _grade.Invalidate();
            MostrarAviso($"Capa de \"{jogo.Titulo}\" removida — o card volta a ser desenhado.");
        }

        private void BuscarCapaOnline()
        {
            if (_grade.JogoSelecionado is not { } jogo) return;
            if (!ExigirChave()) return;

            using (var provedor = new SteamGridDbProvider(_config.SteamGridDbApiKey))
            using (var janela = new FormBuscaDeCapa(jogo, provedor))
            {
                if (janela.ShowDialog(this) != DialogResult.OK || janela.Escolhida is null) return;

                try
                {
                    using (var memoria = new MemoryStream(janela.Escolhida.Bytes))
                    using (var imagem = Image.FromStream(memoria))
                    {
                        _capas.AplicarImagem(jogo, imagem);
                    }

                    jogo.SteamGridDbId = janela.IdEscolhido;
                    SalvarBiblioteca();
                    _grade.Invalidate();

                    MostrarAviso($"Capa de \"{jogo.Titulo}\" baixada.");
                }
                catch (Exception erro)
                {
                    MessageBox.Show(this, $"Baixou, mas não consegui gravar: {erro.Message}",
                        "Capa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// Lote das capas que faltam. Nunca com jogo aberto: a spec é explícita, e roubar
        /// banda e CPU no meio de uma partida é o oposto do que este launcher promete.
        /// </summary>
        private void BaixarCapasEmLote()
        {
            if (_lancador.JogoRodando)
            {
                MostrarAviso("Tem jogo aberto — o lote de capas fica para depois.");
                return;
            }

            if (!ExigirChave()) return;

            var faltando = new List<Jogo>();
            foreach (var jogo in _biblioteca.Jogos)
            {
                if (string.IsNullOrEmpty(jogo.CapaArquivo) || !File.Exists(jogo.CaminhoCapa()!))
                    faltando.Add(jogo);
            }

            if (faltando.Count == 0)
            {
                MostrarAviso("Todos os jogos já têm capa.");
                return;
            }

            var pergunta = MessageBox.Show(this,
                $"Buscar capa para {faltando.Count} jogo(s) sem capa?{Environment.NewLine}{Environment.NewLine}" +
                "Vai um pedido a cada meio segundo, e dá para cancelar no meio.",
                "Baixar capas", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (pergunta != DialogResult.OK) return;

            using (var provedor = new SteamGridDbProvider(_config.SteamGridDbApiKey))
            using (var janela = new FormCapasEmLote(faltando, provedor, _capas))
            {
                janela.ShowDialog(this);

                SalvarBiblioteca();
                _grade.Invalidate();

                MostrarAviso(janela.ChaveRecusada
                    ? "A chave do SteamGridDB foi recusada — confira em Configurações."
                    : $"{janela.Baixadas} capa(s) baixada(s), {janela.SemCapa} sem capa no acervo, " +
                      $"{janela.ComFalha} com erro.");
            }
        }

        /// <summary>
        /// Chave vazia não é erro: é o caminho manual. Aviso curto e nenhuma tentativa de
        /// rede — a spec proíbe conexão que eu não pedi.
        /// </summary>
        private bool ExigirChave()
        {
            if (_config.TemChaveSteamGridDb()) return true;

            MostrarAviso("Sem chave do SteamGridDB: use \"Escolher capa do arquivo...\" ou arraste uma imagem no card.");
            return false;
        }

        // ---- Lançar o jogo (fase 5) --------------------------------------------------------

        /// <summary>
        /// Salvar, lançar, sumir da frente. A ordem importa: a biblioteca vai para o disco
        /// ANTES do jogo abrir, porque se o jogo travar o PC eu não posso perder o que já
        /// estava catalogado.
        /// </summary>
        private void Jogar(Jogo jogo)
        {
            // Trava contra dois Enter seguidos. Vale para o card e para a biblioteca
            // inteira: dois jogos antigos ao mesmo tempo num notebook fraco também não é
            // o que eu quero, e enquanto um roda a janela nem está visível.
            if (_lancador.JogoRodando)
            {
                var emExecucao = _lancador.JogoAtual;
                MostrarAviso(emExecucao is not null && emExecucao.Id == jogo.Id
                    ? $"\"{jogo.Titulo}\" já está aberto."
                    : $"\"{emExecucao?.Titulo}\" ainda está aberto — feche antes de abrir outro.");
                return;
            }

            SalvarBiblioteca();

            try
            {
                _lancador.Lancar(jogo);
            }
            catch (ExecutavelIndisponivelException ex)
            {
                OferecerLocalizarExecutavel(jogo, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não consegui abrir \"{jogo.Titulo}\": {ex.Message}",
                    "Jogar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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
            Hide();

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

            var resultado = ContabilizadorDeTempo.Contabilizar(sessao.Jogo, sessao.Duracao, DateTime.UtcNow);
            SalvarBiblioteca();

            _grade.IdEmExecucao = null;
            Reaparecer(semRoubarFoco: resultado.SaidaImediata);

            AplicarFiltros();   // a ordenação "jogados recentemente" acabou de mudar

            // Aviso discreto: vai para o rodapé, não para uma caixa de diálogo. Jogo que
            // abre um launcher próprio e morre na hora é normal, não é erro.
            _avisoDaSessao = resultado.SaidaImediata
                ? $"\"{sessao.Jogo.Titulo}\" fechou em {sessao.Duracao.TotalSeconds:F0} s e não achei " +
                  "outro processo dele. Se o jogo abriu mesmo assim, é ele rodando por fora — não contei o tempo."
                : "";

            AtualizarRodape();
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

        /// <summary>
        /// Executável sumido (HD reorganizado, pasta renomeada): em vez de crashar, deixa
        /// eu apontar onde ele foi parar. A correção manual vira
        /// <see cref="Jogo.ExecutavelFixadoPeloUsuario"/>, e rescan nenhum a desfaz.
        /// </summary>
        private void OferecerLocalizarExecutavel(Jogo jogo, string motivo)
        {
            var resposta = MessageBox.Show(this,
                $"{motivo}{Environment.NewLine}{Environment.NewLine}Quer localizar o executável agora?",
                "Executável não encontrado", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (resposta != DialogResult.Yes) return;

            using (var dialogo = new OpenFileDialog
                   {
                       Title = $"Executável de {jogo.Titulo}",
                       Filter = "Executáveis (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|Todos os arquivos (*.*)|*.*",
                       InitialDirectory = PastaInicialPara(jogo),
                       CheckFileExists = true
                   })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK) return;

                if (!Caminhos.TentarParaRelativo(dialogo.FileName, out var relativo))
                {
                    MessageBox.Show(this,
                        $"\"{dialogo.FileName}\" está fora da pasta do launcher.{Environment.NewLine}" +
                        "Um caminho de fora viraria letra de drive na biblioteca e quebraria em outro PC.",
                        "Executável não encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                jogo.ExecutavelRelativo = relativo;
                jogo.ExecutavelFixadoPeloUsuario = true;
                SalvarBiblioteca();

                _grade.Invalidate();
                AtualizarRodape();
            }
        }

        /// <summary>Abre o diálogo na pasta do jogo se ela existir; senão, na raiz do HD.</summary>
        private static string PastaInicialPara(Jogo jogo)
        {
            var pasta = jogo.PastaDoJogo();
            return !string.IsNullOrEmpty(pasta) && Directory.Exists(pasta) ? pasta! : Caminhos.PastaBase;
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
        private void EscanearJogos()
        {
            var raizes = EscolherPastasParaEscanear(_biblioteca);
            if (raizes.Count == 0) return;

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
                _lancador.SessaoTerminada -= AoTerminarSessao;
                _lancador.ProcessoFilhoAdotado -= AoAdotarProcessoFilho;
                _lancador.Dispose();        // solta o handle; o jogo aberto continua vivo
                _miniaturas.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
