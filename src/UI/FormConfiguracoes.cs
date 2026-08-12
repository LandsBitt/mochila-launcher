using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Launcher.Dados;
using Launcher.Modelo;

namespace Launcher.UI
{
    /// <summary>
    /// Configurações do launcher.
    ///
    /// Quatro coisas: a chave do SteamGridDB, as pastas que o F6 escaneia, o tamanho
    /// padrão do card e o botão de limpar o cache de miniaturas.
    ///
    /// A chave aparece mascarada. Não é teatro de segurança: eu mexo nisso com o
    /// launcher aberto na TV da sala, e chave de API em texto grande na tela é como ela
    /// vaza para uma foto. O botão "Mostrar" existe para quando eu precisar conferir.
    /// </summary>
    public sealed class FormConfiguracoes : Form
    {
        private readonly Config _config;
        private readonly Biblioteca _biblioteca;

        private readonly TextBox _chave;
        private readonly CheckBox _mostrarChave;
        private readonly ListBox _pastas;
        private readonly ComboBox _tamanho;
        private readonly Label _situacaoDoCache;

        /// <summary>true quando algo mudou e a janela principal precisa recarregar.</summary>
        public bool Mudou { get; private set; }

        /// <summary>true quando o cache foi limpo — a grade precisa redesenhar.</summary>
        public bool CacheLimpo { get; private set; }

        public FormConfiguracoes(Config config, Biblioteca biblioteca)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _biblioteca = biblioteca ?? throw new ArgumentNullException(nameof(biblioteca));

            Text = "Configurações";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 480);
            MinimumSize = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Cores.Fundo;
            ForeColor = Cores.Texto;
            KeyPreview = true;

            _chave = new TextBox
            {
                Text = config.SteamGridDbApiKey,
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto,
                BorderStyle = BorderStyle.FixedSingle
            };

            _mostrarChave = new CheckBox
            {
                Text = "Mostrar",
                Dock = DockStyle.Right,
                Width = 90,
                ForeColor = Cores.Texto,
                FlatStyle = FlatStyle.Flat
            };
            _mostrarChave.CheckedChanged += (_, _) => _chave.UseSystemPasswordChar = !_mostrarChave.Checked;

            _pastas = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false
            };
            foreach (var pasta in biblioteca.PastasEscaneadas) _pastas.Items.Add(pasta);

            _tamanho = new ComboBox
            {
                Dock = DockStyle.Left,
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto
            };
            _tamanho.Items.AddRange(new object[] { "Card P", "Card M", "Card G" });
            _tamanho.SelectedIndex = (int)config.TamanhoCard;

            _situacaoDoCache = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Cores.TextoFraco
            };
            AtualizarSituacaoDoCache();

            Controls.Add(MontarCorpo());
            Controls.Add(MontarRodape());

            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // ---- Montagem ---------------------------------------------------------------------

        private Control MontarCorpo()
        {
            var corpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };

            corpo.Controls.Add(BlocoDoCache());
            corpo.Controls.Add(BlocoDoTamanho());
            corpo.Controls.Add(BlocoDasPastas());
            corpo.Controls.Add(BlocoDaChave());

            return corpo;
        }

        private Control BlocoDaChave()
        {
            var bloco = new Panel { Dock = DockStyle.Top, Height = 88 };

            var linha = new Panel { Dock = DockStyle.Top, Height = 26 };
            linha.Controls.Add(_chave);
            linha.Controls.Add(_mostrarChave);

            bloco.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                ForeColor = Cores.TextoFraco,
                Text = "Sem chave, a busca online fica desligada e as capas continuam pelo caminho manual\r\n" +
                       "(arrastar imagem no card, colar, ou usar a arte da pasta do jogo)."
            });

            bloco.Controls.Add(linha);
            bloco.Controls.Add(Titulo("Chave do SteamGridDB"));

            return bloco;
        }

        private Control BlocoDasPastas()
        {
            var bloco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };

            var botoes = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 8, 0, 0) };

            var adicionar = Botoes.Criar("Adicionar pasta...", Point.Empty, 150);
            adicionar.Dock = DockStyle.Left;
            adicionar.Click += (_, _) => AdicionarPasta();

            var remover = Botoes.Criar("Remover", new Point(158, 0), 110);
            remover.Click += (_, _) => RemoverPasta();

            botoes.Controls.Add(remover);
            botoes.Controls.Add(adicionar);

            bloco.Controls.Add(_pastas);
            bloco.Controls.Add(botoes);
            bloco.Controls.Add(Titulo("Pastas escaneadas (F6)"));

            return bloco;
        }

        private Control BlocoDoTamanho()
        {
            var bloco = new Panel { Dock = DockStyle.Bottom, Height = 52 };

            var linha = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            linha.Controls.Add(_tamanho);

            bloco.Controls.Add(linha);
            bloco.Controls.Add(Titulo("Tamanho padrão do card"));

            return bloco;
        }

        private Control BlocoDoCache()
        {
            var bloco = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(0, 8, 0, 0) };

            var limpar = Botoes.Criar("Limpar cache de miniaturas", Point.Empty, 210);
            limpar.Dock = DockStyle.Left;
            limpar.Click += (_, _) => LimparCache();

            var texto = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
            texto.Controls.Add(_situacaoDoCache);

            bloco.Controls.Add(texto);
            bloco.Controls.Add(limpar);

            return bloco;
        }

        private static Label Titulo(string texto) => new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = texto,
            ForeColor = Cores.Texto,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        private Control MontarRodape()
        {
            var painel = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(16, 10, 16, 10) };

            var cancelar = Botoes.Criar("Cancelar", Point.Empty, 110);
            cancelar.Click += (_, _) => Close();

            var salvar = Botoes.Criar("Salvar", Point.Empty, 110);
            salvar.Click += (_, _) => Salvar();

            var direita = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            direita.Controls.Add(cancelar);
            direita.Controls.Add(salvar);

            painel.Controls.Add(direita);

            AcceptButton = salvar;
            CancelButton = cancelar;

            return painel;
        }

        // ---- Ações ------------------------------------------------------------------------

        private void AdicionarPasta()
        {
            using (var dialogo = new FolderBrowserDialog
                   {
                       Description = "Pasta com os jogos (qualquer pasta do mesmo HD do launcher).",
                       SelectedPath = Caminhos.PastaBase,
                       ShowNewFolderButton = false
                   })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK) return;

                if (!Caminhos.TentarParaRelativo(dialogo.SelectedPath, out var relativa))
                {
                    MessageBox.Show(this,
                        "Essa pasta está em outro drive." + Environment.NewLine +
                        "Só dá para catalogar jogos do mesmo HD, senão o caminho quebraria em outro PC.",
                        "Pastas escaneadas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (var item in _pastas.Items)
                {
                    if (string.Equals(item as string, relativa, StringComparison.OrdinalIgnoreCase)) return;
                }

                _pastas.Items.Add(relativa);
            }
        }

        private void RemoverPasta()
        {
            if (_pastas.SelectedIndex >= 0) _pastas.Items.RemoveAt(_pastas.SelectedIndex);
        }

        /// <summary>
        /// Apaga _launcher\cache. É seguro: miniatura se refaz sozinha a partir da capa.
        /// Serve para quando eu troco capas por fora e quero forçar tudo a recarregar.
        /// </summary>
        private void LimparCache()
        {
            var apagados = 0;
            long bytes = 0;

            try
            {
                if (Directory.Exists(Caminhos.PastaCache))
                {
                    foreach (var arquivo in Directory.GetFiles(Caminhos.PastaCache))
                    {
                        try
                        {
                            bytes += new FileInfo(arquivo).Length;
                            File.Delete(arquivo);
                            apagados++;
                        }
                        catch (Exception)
                        {
                            // Miniatura em uso agora: fica para a próxima limpeza.
                        }
                    }
                }
            }
            catch (Exception erro)
            {
                MessageBox.Show(this, $"Não consegui limpar o cache: {erro.Message}",
                    "Cache", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CacheLimpo = true;
            AtualizarSituacaoDoCache();

            _situacaoDoCache.Text = apagados == 0
                ? "Nada para limpar."
                : $"{apagados} miniatura(s) apagada(s), {Formatar(bytes)} liberado(s). Elas se refazem sozinhas.";
        }

        private void AtualizarSituacaoDoCache()
        {
            try
            {
                if (!Directory.Exists(Caminhos.PastaCache))
                {
                    _situacaoDoCache.Text = "Cache vazio.";
                    return;
                }

                var arquivos = Directory.GetFiles(Caminhos.PastaCache);
                long bytes = 0;

                foreach (var arquivo in arquivos)
                {
                    try { bytes += new FileInfo(arquivo).Length; } catch (Exception) { }
                }

                _situacaoDoCache.Text = arquivos.Length == 0
                    ? "Cache vazio."
                    : $"{arquivos.Length} miniatura(s), {Formatar(bytes)}.";
            }
            catch (Exception)
            {
                _situacaoDoCache.Text = "Não consegui ler o cache (o HD ainda está conectado?).";
            }
        }

        private static string Formatar(long bytes)
            => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB" : $"{bytes / 1024.0:F0} KB";

        private void Salvar()
        {
            _config.SteamGridDbApiKey = _chave.Text.Trim();
            _config.TamanhoCard = (TamanhoCard)Math.Max(0, _tamanho.SelectedIndex);

            _biblioteca.PastasEscaneadas.Clear();
            foreach (var item in _pastas.Items)
            {
                if (item is string pasta && pasta.Length > 0) _biblioteca.PastasEscaneadas.Add(pasta);
            }

            Mudou = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
