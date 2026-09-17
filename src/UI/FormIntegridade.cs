using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// O relatório de integridade do acervo (fase 17).
    ///
    /// A tela é uma lista e três botões de ação. O trabalho de verdade — que inclui um
    /// scan do acervo inteiro — está em <see cref="RelatorioDeIntegridade"/> e roda numa
    /// thread, com botão de cancelar: numa pasta grande de HD externo isso leva segundos,
    /// e uma janela congelada seria pior que não ter o relatório.
    ///
    /// <b>Contar não conserta.</b> Abrir esta janela varre e mostra; consertar é sempre um
    /// clique meu, item por item. É o que torna seguro rodá-la só para ver.
    /// </summary>
    public sealed class FormIntegridade : Form
    {
        private readonly IContextoDoLauncher _contexto;
        private readonly AcoesDoJogo _acoes;

        private readonly Label _situacao;
        private readonly ListBox _lista;
        private readonly Button _principal;
        private readonly Button _secundario;
        private readonly Button _fechar;

        private readonly CancellationTokenSource _cancelamento = new CancellationTokenSource();

        private RelatorioDeIntegridade? _relatorio;
        private bool _terminou;

        /// <summary>
        /// true quando eu pedi para escanear a partir daqui. A janela principal roda o F6
        /// DEPOIS que esta fecha — modal em cima de modal para acompanhar um scan é a
        /// receita de duas janelas fechadas na ordem errada.
        /// </summary>
        public bool PediuEscanear { get; private set; }

        public FormIntegridade(IContextoDoLauncher contexto, AcoesDoJogo acoes)
        {
            _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
            _acoes = acoes ?? throw new ArgumentNullException(nameof(acoes));

            Text = "Integridade do acervo";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 420);
            MinimumSize = new Size(600, 340);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            Tema.AplicarNaJanela(this);

            _situacao = new Label
            {
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(16, 10, 16, 0),
                ForeColor = Tema.Texto,
                Text = "Conferindo o acervo..."
            };

            _lista = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                HorizontalScrollbar = true
            };
            _lista.SelectedIndexChanged += (_, _) => AjustarBotoes();
            _lista.DoubleClick += (_, _) => Agir(principal: true);

            _principal = Botoes.CriarPrincipal("", Point.Empty, 190);
            _principal.Click += (_, _) => Agir(principal: true);

            _secundario = Botoes.Criar("", Point.Empty, 170);
            _secundario.Click += (_, _) => Agir(principal: false);

            _fechar = Botoes.Criar("Cancelar", Point.Empty, 110);
            _fechar.Click += (_, _) => AoClicarNoFechar();

            var corpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 8) };
            corpo.Controls.Add(_lista);

            Controls.Add(corpo);
            Controls.Add(_situacao);
            Controls.Add(MontarRodape());

            CancelButton = _fechar;
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) AoClicarNoFechar(); };

            AjustarBotoes();
            Shown += async (_, _) => await Conferir().ConfigureAwait(true);
        }

        // ---- A varredura --------------------------------------------------------------------

        /// <summary>
        /// Roda o relatório fora da thread da interface e volta para preencher a lista.
        ///
        /// <c>Task.Run</c> e não <c>async</c> puro porque aqui não há espera de rede: é
        /// disco e CPU do começo ao fim, e isso não sai da thread da UI sozinho.
        /// </summary>
        private async Task Conferir()
        {
            var biblioteca = _contexto.Biblioteca;
            var config = _contexto.Config;

            var progresso = new Progress<string>(texto =>
            {
                if (!IsDisposed) _situacao.Text = texto;
            });

            try
            {
                _relatorio = await Task.Run(
                    () => RelatorioDeIntegridade.Montar(biblioteca, config, null, progresso, _cancelamento.Token),
                    _cancelamento.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed) return;

                _situacao.Text = "Cancelado — nada foi verificado até o fim, e nada foi alterado.";
                Terminar();
                return;
            }
            catch (Exception erro)
            {
                if (IsDisposed) return;

                _situacao.Text = $"Não consegui conferir o acervo: {erro.Message}";
                Terminar();
                return;
            }

            if (IsDisposed) return;

            Preencher();
            Terminar();
        }

        private void Preencher()
        {
            if (_relatorio is not { } relatorio) return;

            var selecionado = _lista.SelectedIndex;

            _lista.BeginUpdate();
            _lista.Items.Clear();

            foreach (var problema in relatorio.Problemas) _lista.Items.Add(problema);

            _lista.EndUpdate();

            _situacao.Text = relatorio.Resumo();

            if (_lista.Items.Count > 0)
                _lista.SelectedIndex = Math.Min(Math.Max(0, selecionado), _lista.Items.Count - 1);

            AjustarBotoes();
        }

        private void Terminar()
        {
            _terminou = true;
            _fechar.Text = "Fechar";
            AjustarBotoes();
        }

        // ---- As ações diretas ---------------------------------------------------------------

        private ProblemaDeIntegridade? Selecionado => _lista.SelectedItem as ProblemaDeIntegridade;

        /// <summary>
        /// Os botões mudam de nome conforme o item selecionado. Um par de botões que se
        /// renomeia é melhor que seis botões fixos, cinco deles apagados o tempo todo.
        /// </summary>
        private void AjustarBotoes()
        {
            if (!_terminou || Selecionado is not { } problema)
            {
                _principal.Text = "";
                _secundario.Text = "";
                _principal.Enabled = false;
                _secundario.Enabled = false;
                _principal.Visible = _terminou;
                _secundario.Visible = _terminou;
                return;
            }

            _principal.Visible = true;
            _secundario.Visible = true;
            _principal.Enabled = true;
            _secundario.Enabled = true;

            switch (problema.Tipo)
            {
                case TipoDeProblema.JogoNaoEncontrado:
                    _principal.Text = "Localizar executável...";

                    // A religação da fase 10 mora no scan: é a janela de revisão que
                    // reconhece o jogo pela impressão digital e oferece o Ctrl+R. Daí o
                    // caminho daqui para lá ser o próprio F6, e não uma segunda religação.
                    _secundario.Text = "Escanear e religar (F6)";
                    break;

                case TipoDeProblema.ArteQuebrada:
                    _principal.Text = "Esquecer essa arte";
                    _secundario.Text = "Abrir a pasta do jogo";
                    break;

                default:
                    _principal.Text = "Escanear agora (F6)";
                    _secundario.Text = "Abrir a pasta";
                    break;
            }
        }

        private void Agir(bool principal)
        {
            if (!_terminou || Selecionado is not { } problema) return;

            switch (problema.Tipo)
            {
                case TipoDeProblema.JogoNaoEncontrado when problema.Jogo is { } jogo:
                    if (principal)
                    {
                        _acoes.OferecerLocalizarExecutavel(jogo,
                            $"\"{jogo.Titulo}\" não está mais em {problema.Alvo}.");
                        Reconferir();
                    }
                    else PedirScan();
                    break;

                case TipoDeProblema.ArteQuebrada when problema.Jogo is { } jogo:
                    if (principal)
                    {
                        _acoes.EsquecerArteQuebrada(jogo, problema.Alvo);
                        Reconferir();
                    }
                    else _acoes.AbrirPastaDoJogo(jogo);
                    break;

                case TipoDeProblema.PastaNova:
                    if (principal) PedirScan();
                    else AbrirNoExplorer(problema.Alvo);
                    break;
            }
        }

        private void PedirScan()
        {
            PediuEscanear = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AbrirNoExplorer(string relativo)
        {
            var pasta = Dados.Caminhos.ParaAbsolutoOuNulo(relativo);
            if (pasta is null) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pasta,
                    UseShellExecute = true
                })?.Dispose();
            }
            catch (Exception erro)
            {
                MessageBox.Show(this, $"Não consegui abrir a pasta: {erro.Message}",
                    "Integridade", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Refaz a lista depois de eu consertar alguma coisa — só a parte barata.
        ///
        /// Reconferir os jogos custa nada; procurar pasta nova de novo custaria um scan
        /// inteiro a cada clique. Item consertado sai da lista na hora; pasta nova só
        /// desaparece na próxima abertura do relatório, que é depois do F6 mesmo.
        /// </summary>
        private void Reconferir()
        {
            if (_relatorio is null) return;

            var sobraram = new List<ProblemaDeIntegridade>();

            foreach (var item in _lista.Items)
            {
                if (item is not ProblemaDeIntegridade problema) continue;
                if (AindaVale(problema)) sobraram.Add(problema);
            }

            var selecionado = _lista.SelectedIndex;

            _lista.BeginUpdate();
            _lista.Items.Clear();
            foreach (var problema in sobraram) _lista.Items.Add(problema);
            _lista.EndUpdate();

            if (_lista.Items.Count > 0)
                _lista.SelectedIndex = Math.Min(Math.Max(0, selecionado), _lista.Items.Count - 1);
            else
                _situacao.Text = "Não sobrou nada para consertar nesta lista.";

            AjustarBotoes();
        }

        private static bool AindaVale(ProblemaDeIntegridade problema)
        {
            if (problema.Jogo is not { } jogo) return true;    // pasta nova: só o F6 resolve

            return problema.Tipo switch
            {
                TipoDeProblema.JogoNaoEncontrado => !jogo.ExecutavelExiste(),
                TipoDeProblema.ArteQuebrada => string.Equals(jogo.CapaArquivo, problema.Alvo, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(jogo.HeroArquivo, problema.Alvo, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(jogo.LogoArquivo, problema.Alvo, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        // ---- Montagem e fechamento ------------------------------------------------------------

        private Control MontarRodape()
        {
            var painel = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(16, 10, 16, 10) };

            var esquerda = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            esquerda.Controls.Add(_principal);
            esquerda.Controls.Add(_secundario);

            var direita = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            direita.Controls.Add(_fechar);

            painel.Controls.Add(esquerda);
            painel.Controls.Add(direita);

            return painel;
        }

        /// <summary>Um botão só: cancela enquanto varre, fecha depois.</summary>
        private void AoClicarNoFechar()
        {
            if (!_terminou)
            {
                _cancelamento.Cancel();
                _situacao.Text = "Cancelando...";
                return;
            }

            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cancelamento.Cancel();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cancelamento.Dispose();
            base.Dispose(disposing);
        }
    }
}
