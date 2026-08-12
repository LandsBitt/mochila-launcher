using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Launcher.Capas;
using Launcher.Modelo;

namespace Launcher.UI
{
    /// <summary>
    /// Seletor de capa de um jogo.
    ///
    /// Existe porque "Need for Speed: Carbon" e "Need for Speed: Carbon - Own the City"
    /// são jogos diferentes com o mesmo começo de nome. Quando a busca traz candidatos
    /// divergentes, eu escolho olhando a arte — o programa chutar o primeiro é como o
    /// acervo ganha capa errada sem ninguém perceber.
    /// </summary>
    public sealed class FormBuscaDeCapa : Form
    {
        private readonly ICapaProvider _provedor;
        private readonly Jogo _jogo;

        private readonly TextBox _termo;
        private readonly ListBox _candidatos;
        private readonly PictureBox _previa;
        private readonly Label _situacao;
        private readonly Button _usar;

        private CancellationTokenSource? _emAndamento;

        /// <summary>Capa escolhida, já baixada. null quando eu cancelo.</summary>
        public CapaBaixada? Escolhida { get; private set; }

        /// <summary>Id do jogo no provedor, para gravar e não repetir a busca.</summary>
        public int? IdEscolhido { get; private set; }

        public FormBuscaDeCapa(Jogo jogo, ICapaProvider provedor)
        {
            _jogo = jogo ?? throw new ArgumentNullException(nameof(jogo));
            _provedor = provedor ?? throw new ArgumentNullException(nameof(provedor));

            Text = $"Buscar capa — {jogo.Titulo}";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 460);
            MinimumSize = new Size(620, 420);
            BackColor = Cores.Fundo;
            ForeColor = Cores.Texto;
            KeyPreview = true;

            _termo = new TextBox
            {
                Text = jogo.Titulo,
                Dock = DockStyle.Fill,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto,
                BorderStyle = BorderStyle.FixedSingle
            };

            _candidatos = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Cores.FundoControle,
                ForeColor = Cores.Texto,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false
            };
            _candidatos.SelectedIndexChanged += (_, _) => MostrarPrevia();

            _previa = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Cores.FundoPainel
            };

            _situacao = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Cores.TextoFraco,
                Padding = new Padding(4, 0, 4, 0)
            };

            _usar = Botoes.Criar("Usar esta capa", Point.Empty, 150);
            _usar.Enabled = false;
            _usar.Click += async (_, _) => await Confirmar().ConfigureAwait(true);

            Controls.Add(MontarCorpo());
            Controls.Add(MontarTopo());
            Controls.Add(MontarRodape());

            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            Shown += async (_, _) => await Procurar().ConfigureAwait(true);
        }

        // ---- Montagem -------------------------------------------------------------------------

        private Control MontarTopo()
        {
            var painel = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 10, 12, 8) };

            var procurar = Botoes.Criar("Procurar", Point.Empty, 100);
            procurar.Dock = DockStyle.Right;
            procurar.Click += async (_, _) => await Procurar().ConfigureAwait(true);

            var caixa = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 3, 8, 0) };
            caixa.Controls.Add(_termo);

            painel.Controls.Add(caixa);
            painel.Controls.Add(procurar);
            return painel;
        }

        private Control MontarCorpo()
        {
            var painel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };

            var previa = new Panel { Dock = DockStyle.Right, Width = 240, Padding = new Padding(8, 0, 0, 0) };
            previa.Controls.Add(_previa);

            var lista = new Panel { Dock = DockStyle.Fill };
            lista.Controls.Add(_candidatos);
            lista.Controls.Add(_situacao);

            painel.Controls.Add(lista);
            painel.Controls.Add(previa);
            return painel;
        }

        private Control MontarRodape()
        {
            var painel = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 10, 12, 10) };

            var cancelar = Botoes.Criar("Cancelar", Point.Empty, 110);
            cancelar.Click += (_, _) => Close();

            var direita = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            direita.Controls.Add(cancelar);
            direita.Controls.Add(_usar);

            painel.Controls.Add(direita);
            CancelButton = cancelar;
            return painel;
        }

        // ---- Busca ----------------------------------------------------------------------------

        private async Task Procurar()
        {
            Cancelar();
            _emAndamento = new CancellationTokenSource();

            _candidatos.Items.Clear();
            _usar.Enabled = false;
            _situacao.Text = "Procurando...";

            var busca = await _provedor.BuscarJogo(_termo.Text, _emAndamento.Token).ConfigureAwait(true);

            if (IsDisposed) return;

            if (!busca.DeuCerto)
            {
                _situacao.Text = busca.Mensagem;
                return;
            }

            foreach (var candidato in busca.Valor!) _candidatos.Items.Add(candidato);

            _situacao.Text = $"{busca.Valor.Count} resultado(s). Escolha o certo antes de usar.";
            if (_candidatos.Items.Count > 0) _candidatos.SelectedIndex = 0;
        }

        private async void MostrarPrevia()
        {
            if (_candidatos.SelectedItem is not JogoDeCapa candidato) return;
            if (_provedor is not SteamGridDbProvider comLista) { _usar.Enabled = true; return; }

            Cancelar();
            _emAndamento = new CancellationTokenSource();

            _usar.Enabled = false;
            _situacao.Text = $"Carregando capa de \"{candidato.Nome}\"...";

            var capas = await comLista.ListarCapas(candidato.Id, _emAndamento.Token).ConfigureAwait(true);
            if (IsDisposed) return;

            if (!capas.DeuCerto)
            {
                _situacao.Text = capas.Mensagem;
                TrocarPrevia(null);
                return;
            }

            var melhor = capas.Valor![0];
            var bytes = await comLista.BaixarBytes(
                melhor.UrlMiniatura.Length > 0 ? melhor.UrlMiniatura : melhor.UrlCheia,
                _emAndamento.Token).ConfigureAwait(true);

            if (IsDisposed) return;

            if (!bytes.DeuCerto)
            {
                _situacao.Text = bytes.Mensagem;
                TrocarPrevia(null);
                return;
            }

            TrocarPrevia(DeBytes(bytes.Valor!.Bytes));
            _situacao.Text = $"{capas.Valor.Count} capa(s) disponível(is) — mostrando a mais votada.";
            _usar.Enabled = true;
        }

        private async Task Confirmar()
        {
            if (_candidatos.SelectedItem is not JogoDeCapa candidato) return;

            Cancelar();
            _emAndamento = new CancellationTokenSource();

            _usar.Enabled = false;
            _situacao.Text = "Baixando...";

            var capa = await _provedor.BaixarCapa(candidato.Id, TamanhoDeCapa.Miniatura, _emAndamento.Token)
                                      .ConfigureAwait(true);

            if (IsDisposed) return;

            if (!capa.DeuCerto)
            {
                _situacao.Text = capa.Mensagem;
                _usar.Enabled = true;
                return;
            }

            Escolhida = capa.Valor;
            IdEscolhido = candidato.Id;

            DialogResult = DialogResult.OK;
            Close();
        }

        private static Image? DeBytes(byte[] bytes)
        {
            try
            {
                using (var memoria = new System.IO.MemoryStream(bytes))
                    return new Bitmap(Image.FromStream(memoria));
            }
            catch (Exception)
            {
                return null;   // imagem corrompida não derruba a janela
            }
        }

        private void TrocarPrevia(Image? nova)
        {
            var antiga = _previa.Image;
            _previa.Image = nova;
            antiga?.Dispose();
        }

        private void Cancelar()
        {
            _emAndamento?.Cancel();
            _emAndamento?.Dispose();
            _emAndamento = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cancelar();
                TrocarPrevia(null);
            }
            base.Dispose(disposing);
        }
    }
}
