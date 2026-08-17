using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mochila.Capas;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// Baixa capa de vários jogos de uma vez, com freio e botão de cancelar.
    ///
    /// O freio não é enfeite: a API é de graça e mantida pela comunidade, e disparar 200
    /// requisições em rajada é como se perde o acesso (429). Um pedido a cada 500 ms
    /// mantém os ~2/s que a spec pede.
    ///
    /// Roda com async/await na própria thread da interface: sem thread extra, sem timer,
    /// e o cancelamento é imediato porque o token vai até dentro do HttpClient.
    /// </summary>
    public sealed class FormCapasEmLote : Form
    {
        /// <summary>Intervalo entre pedidos. ~2/s, como a spec manda.</summary>
        private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(500);

        private readonly IReadOnlyList<Jogo> _jogos;
        private readonly ICapaProvider _provedor;
        private readonly GerenciadorDeCapas _gerenciador;

        private readonly ProgressBar _barra;
        private readonly Label _situacao;
        private readonly ListBox _registro;
        private readonly Button _fechar;

        private readonly CancellationTokenSource _cancelamento = new CancellationTokenSource();

        private bool _terminou;

        public int Baixadas { get; private set; }

        public int SemCapa { get; private set; }

        public int ComFalha { get; private set; }

        /// <summary>true quando alguma resposta indicou chave recusada — vale parar tudo.</summary>
        public bool ChaveRecusada { get; private set; }

        public FormCapasEmLote(IReadOnlyList<Jogo> jogos, ICapaProvider provedor, GerenciadorDeCapas gerenciador)
        {
            _jogos = jogos ?? throw new ArgumentNullException(nameof(jogos));
            _provedor = provedor ?? throw new ArgumentNullException(nameof(provedor));
            _gerenciador = gerenciador ?? throw new ArgumentNullException(nameof(gerenciador));

            Text = "Baixando capas";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 340);
            MinimumSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;

            _barra = new ProgressBar { Dock = DockStyle.Top, Height = 20, Maximum = Math.Max(1, jogos.Count) };
            _situacao = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Tema.TextoFraco
            };

            _registro = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false
            };

            _fechar = Botoes.Criar("Cancelar", Point.Empty, 120);
            _fechar.Click += (_, _) => AoClicarNoBotao();

            var rodape = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 10, 12, 10) };
            var direita = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            direita.Controls.Add(_fechar);
            rodape.Controls.Add(direita);

            var corpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
            corpo.Controls.Add(_registro);
            corpo.Controls.Add(_situacao);
            corpo.Controls.Add(_barra);

            Controls.Add(corpo);
            Controls.Add(rodape);

            CancelButton = _fechar;
            Shown += async (_, _) => await Rodar().ConfigureAwait(true);
        }

        private async Task Rodar()
        {
            var feitos = 0;

            foreach (var jogo in _jogos)
            {
                if (_cancelamento.IsCancellationRequested) break;

                _situacao.Text = $"{feitos + 1} de {_jogos.Count}: {jogo.Titulo}";

                var resultado = await _gerenciador.BaixarPara(jogo, _provedor, _cancelamento.Token)
                                                  .ConfigureAwait(true);

                if (IsDisposed) return;

                Registrar(jogo, resultado);

                // Chave recusada não melhora na próxima tentativa: parar aqui evita 200
                // erros iguais e um banimento por insistência.
                if (resultado.Falha == FalhaDeCapa.ChaveInvalida)
                {
                    ChaveRecusada = true;
                    _registro.Items.Add("Parei: a chave da API foi recusada. Confira em Configurações.");
                    break;
                }

                if (resultado.Falha == FalhaDeCapa.Cancelado) break;

                feitos++;
                _barra.Value = Math.Min(_barra.Maximum, feitos);

                // O freio. Não vale esperar depois do último.
                if (feitos < _jogos.Count && !_cancelamento.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(Intervalo, _cancelamento.Token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            Terminar();
        }

        private void Registrar(Jogo jogo, ResultadoDeCapa<string> resultado)
        {
            if (resultado.DeuCerto)
            {
                Baixadas++;
                _registro.Items.Add($"✓ {jogo.Titulo}");
            }
            else if (resultado.Falha == FalhaDeCapa.NaoEncontrado)
            {
                SemCapa++;
                _registro.Items.Add($"— {jogo.Titulo}: sem capa no acervo");
            }
            else if (resultado.Falha != FalhaDeCapa.Cancelado)
            {
                ComFalha++;
                _registro.Items.Add($"! {jogo.Titulo}: {resultado.Mensagem}");
            }

            if (_registro.Items.Count > 0) _registro.TopIndex = _registro.Items.Count - 1;
        }

        private void Terminar()
        {
            if (IsDisposed) return;

            _situacao.Text = _cancelamento.IsCancellationRequested
                ? $"Cancelado. {Baixadas} capa(s) baixada(s)."
                : $"Pronto: {Baixadas} baixada(s), {SemCapa} sem capa, {ComFalha} com erro.";

            _terminou = true;
            _fechar.Text = "Fechar";
        }

        /// <summary>
        /// O mesmo botão faz as duas coisas: cancela enquanto roda, fecha depois. Um
        /// handler só, decidindo pelo estado — trocar o evento em tempo de execução é a
        /// receita para acumular assinatura duplicada.
        /// </summary>
        private void AoClicarNoBotao()
        {
            if (_terminou)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (!_cancelamento.IsCancellationRequested)
            {
                _cancelamento.Cancel();
                _situacao.Text = "Cancelando...";
            }
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
