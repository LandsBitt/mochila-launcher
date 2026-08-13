using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Launcher.Scanner;

namespace Launcher.UI
{
    /// <summary>
    /// Roda o scan numa thread separada, com progresso e botão cancelar.
    ///
    /// A UI nunca pode travar durante o scan: num HD externo com cache frio, varrer o
    /// acervo leva segundos, e uma janela congelada parece um programa quebrado.
    /// </summary>
    public sealed class FormProgressoDoScan : Form
    {
        /// <summary>Intervalo mínimo entre atualizações do texto. O scanner reporta pasta
        /// por pasta — repintar a cada uma seria mais caro que o próprio scan.</summary>
        private const int IntervaloDeAtualizacaoMs = 100;

        private readonly IReadOnlyList<string> _raizes;
        private readonly FiltroDeExclusao _filtro;
        private readonly CancellationTokenSource _cancelamento = new CancellationTokenSource();

        private readonly Label _rotuloPasta;
        private readonly Label _rotuloContagem;
        private readonly Button _botaoCancelar;

        private Thread? _thread;
        private int _ultimaAtualizacao;

        public FormProgressoDoScan(IReadOnlyList<string> raizes, FiltroDeExclusao filtro)
        {
            _raizes = raizes;
            _filtro = filtro;

            Text = "Escaneando...";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 150);
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;

            var barra = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,   // não dá para saber o total sem varrer antes
                MarqueeAnimationSpeed = 30,
                Location = new Point(16, 60),
                Size = new Size(528, 18)
            };

            _rotuloPasta = new Label
            {
                Location = new Point(16, 16),
                Size = new Size(528, 36),
                ForeColor = Tema.TextoFraco,
                AutoEllipsis = true,
                Text = "Preparando..."
            };

            _rotuloContagem = new Label
            {
                Location = new Point(16, 90),
                Size = new Size(320, 20),
                ForeColor = Tema.TextoFraco,
                Text = "0 pasta(s), 0 jogo(s)"
            };

            _botaoCancelar = Botoes.Criar("Cancelar", new Point(444, 86), 100);
            _botaoCancelar.Click += (_, _) => Cancelar();

            Controls.Add(_rotuloPasta);
            Controls.Add(barra);
            Controls.Add(_rotuloContagem);
            Controls.Add(_botaoCancelar);

            CancelButton = _botaoCancelar;
        }

        /// <summary>Jogos encontrados. Vazio se o scan foi cancelado ou deu erro.</summary>
        public List<JogoDetectado> Jogos { get; private set; } = new List<JogoDetectado>();

        public IReadOnlyList<PastaDescartada> Descartes { get; private set; } = Array.Empty<PastaDescartada>();

        public int PastasVisitadas { get; private set; }

        public bool Cancelado { get; private set; }

        /// <summary>Erro inesperado durante o scan, se houve.</summary>
        public Exception? Falha { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            IniciarScan();
        }

        private void IniciarScan()
        {
            // Progress<T> criado aqui na thread da UI: ele mesmo faz o marshalling de volta.
            var progresso = new Progress<ProgressoScan>(AtualizarTexto);
            var token = _cancelamento.Token;

            _thread = new Thread(() =>
            {
                var scanner = new ScannerDeJogos(new SistemaDeArquivosReal(), _filtro);
                List<JogoDetectado>? jogos = null;
                Exception? falha = null;
                var cancelou = false;

                try
                {
                    jogos = scanner.Escanear(_raizes, progresso, token);
                }
                catch (OperationCanceledException)
                {
                    cancelou = true;
                }
                catch (Exception ex)
                {
                    falha = ex;
                }

                Concluir(jogos, scanner, cancelou, falha);
            })
            {
                IsBackground = true,          // fechar o launcher não pode deixar thread presa
                Name = "scan-de-jogos"
            };

            _thread.Start();
        }

        private void Concluir(List<JogoDetectado>? jogos, ScannerDeJogos scanner, bool cancelou, Exception? falha)
        {
            if (IsDisposed) return;

            void Finalizar()
            {
                Jogos = jogos ?? new List<JogoDetectado>();
                Descartes = scanner.Descartes;
                PastasVisitadas = scanner.PastasVisitadas;
                Cancelado = cancelou;
                Falha = falha;

                DialogResult = cancelou ? DialogResult.Cancel : DialogResult.OK;
                Close();
            }

            try
            {
                if (InvokeRequired) BeginInvoke((Action)Finalizar);
                else Finalizar();
            }
            catch (ObjectDisposedException)
            {
                // Janela fechada antes da thread terminar: não há mais nada para atualizar.
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void AtualizarTexto(ProgressoScan progresso)
        {
            var agora = Environment.TickCount;
            if (unchecked(agora - _ultimaAtualizacao) < IntervaloDeAtualizacaoMs) return;
            _ultimaAtualizacao = agora;

            _rotuloPasta.Text = progresso.PastaAtual;
            _rotuloContagem.Text = $"{progresso.PastasVisitadas} pasta(s), {progresso.JogosEncontrados} jogo(s)";
        }

        private void Cancelar()
        {
            _botaoCancelar.Enabled = false;
            _botaoCancelar.Text = "Cancelando...";
            _cancelamento.Cancel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Fechar no X vale como cancelar.
            if (e.CloseReason == CloseReason.UserClosing && DialogResult == DialogResult.None)
            {
                Cancelar();
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cancelamento.Dispose();
            base.Dispose(disposing);
        }
    }
}
