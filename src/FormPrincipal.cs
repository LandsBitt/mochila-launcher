using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
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
        private readonly TextBox _busca;
        private readonly ComboBox _ordenacao;
        private readonly CheckBox _somenteFavoritos;
        private readonly ComboBox _tamanhoDoCard;
        private readonly Label _rodape;

        private readonly LancadorDeJogos _lancador = new LancadorDeJogos();

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
            BackColor = Cores.Fundo;
            ForeColor = Cores.Texto;
            DoubleBuffered = true;
            KeyPreview = true;

            _grade = new GradeDeCapas(_miniaturas) { Dock = DockStyle.Fill };
            _grade.JogoAcionado += (_, jogo) => Jogar(jogo);
            // Qualquer navegação minha limpa o recado da sessão anterior.
            _grade.SelecaoMudou += (_, _) => { _avisoDaSessao = ""; AtualizarRodape(); };

            _lancador.SessaoTerminada += AoTerminarSessao;
            _lancador.ProcessoFilhoAdotado += AoAdotarProcessoFilho;

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

        private Control CriarBarraSuperior()
        {
            var painel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Cores.FundoPainel,
                Padding = new Padding(12, 10, 12, 10)
            };

            var escanear = Botoes.Criar("Escanear jogos (F6)", new Point(0, 0), 160, 30);
            escanear.Dock = DockStyle.Right;
            escanear.Click += (_, _) => EscanearJogos();

            painel.Controls.Add(_busca);
            painel.Controls.Add(_ordenacao);
            painel.Controls.Add(_somenteFavoritos);
            painel.Controls.Add(_tamanhoDoCard);
            painel.Controls.Add(escanear);

            return painel;
        }

        private TextBox CriarBusca()
        {
            var busca = new TextBox
            {
                Location = new Point(12, 14),
                Width = 320,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };

            busca.TextChanged += (_, _) => AplicarFiltros();
            return busca;
        }

        private ComboBox CriarOrdenacao()
        {
            var combo = new ComboBox
            {
                Location = new Point(344, 14),
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto
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

        private CheckBox CriarFiltroDeFavoritos()
        {
            var caixa = new CheckBox
            {
                Text = "★ Só favoritos",
                Location = new Point(548, 16),
                Width = 120,
                ForeColor = Cores.Texto,
                FlatStyle = FlatStyle.Flat
            };

            caixa.CheckedChanged += (_, _) =>
            {
                _config.SomenteFavoritos = caixa.Checked;
                SalvarConfig();
                AplicarFiltros();
            };
            return caixa;
        }

        private ComboBox CriarTamanhoDoCard()
        {
            var combo = new ComboBox
            {
                Location = new Point(676, 14),
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto
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

        private static Label CriarRodape() => new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
            BackColor = Cores.FundoPainel,
            ForeColor = Cores.TextoFraco
        };

        // ---- Carga e filtros ------------------------------------------------------------------

        private void Carregar()
        {
            // Primeiro uso num HD novo: cria _launcher\, a biblioteca e o config padrão.
            Caminhos.GarantirEstrutura();
            if (!ArquivoTexto.Existe(Caminhos.ArquivoBiblioteca)) new Biblioteca().Salvar();
            if (!ArquivoTexto.Existe(Caminhos.ArquivoConfig)) new Config().Salvar();

            _config = Config.Carregar();

            try
            {
                _biblioteca = Biblioteca.Carregar();
            }
            catch (DadosCorrompidosException ex)
            {
                _biblioteca = new Biblioteca();
                MessageBox.Show(this, ex.Message, "Biblioteca", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            _ordenacao.SelectedIndex = (int)_config.Ordenacao;
            _somenteFavoritos.Checked = _config.SomenteFavoritos;
            _tamanhoDoCard.SelectedIndex = (int)_config.TamanhoCard;
            _grade.TamanhoDoCard = _config.TamanhoCard;

            AplicarFiltros();
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
            var detalhe = _avisoDaSessao.Length > 0
                ? _avisoDaSessao
                : selecionado is null
                    ? "Setas para navegar, Enter para jogar, F para favoritar, F6 para escanear."
                    : DescreverJogo(selecionado);

            _rodape.ForeColor = _avisoDaSessao.Length > 0 ? Cores.TextoBaixaConfianca : Cores.TextoFraco;
            _rodape.Text = $"{total}  |  {detalhe}";
        }

        private static string DescreverJogo(Jogo jogo)
        {
            var tempo = TempoDeJogo.Descrever(jogo.SegundosJogados);

            var quando = jogo.UltimaVezJogado is { } data
                ? $", última vez em {data.ToLocalTime():dd/MM/yyyy}"
                : "";

            var faltando = jogo.ExecutavelExiste() ? "" : "  [EXECUTÁVEL NÃO ENCONTRADO]";

            return $"{jogo.Titulo} — {tempo}{quando}{faltando}";
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
