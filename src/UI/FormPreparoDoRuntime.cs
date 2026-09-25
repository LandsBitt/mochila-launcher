using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Mochila.Execucao;

namespace Mochila.UI
{
    /// <summary>
    /// Baixa e prepara o runtime DirectX numa thread separada, com progresso e cancelar.
    /// Mesmo desenho do <see cref="FormProgressoDoScan"/>: são ~100 MB de download e
    /// ~150 CABs, e janela congelada durante isso parece programa travado.
    /// </summary>
    public sealed class FormPreparoDoRuntime : Form
    {
        private readonly CancellationTokenSource _cancelamento = new CancellationTokenSource();

        private readonly Label _etapa;
        private readonly ProgressBar _barra;
        private readonly Button _botaoCancelar;

        private Thread? _thread;

        public FormPreparoDoRuntime()
        {
            Text = "Runtime DirectX";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 130);
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;

            _etapa = new Label
            {
                Location = new Point(16, 16),
                Size = new Size(528, 36),
                ForeColor = Tema.TextoFraco,
                AutoEllipsis = true,
                Text = "Preparando..."
            };

            _barra = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(16, 56),
                Size = new Size(528, 18),
                Maximum = 100
            };

            _botaoCancelar = Botoes.Criar("Cancelar", new Point(444, 86), 100);
            _botaoCancelar.Click += (_, _) => Cancelar();

            Controls.Add(_etapa);
            Controls.Add(_barra);
            Controls.Add(_botaoCancelar);

            CancelButton = _botaoCancelar;
        }

        public bool Cancelado { get; private set; }

        /// <summary>O motivo, em português, quando a preparação não chegou ao fim.</summary>
        public string? Falha { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            var progresso = new Progress<ProgressoDoRuntime>(Mostrar);
            var token = _cancelamento.Token;

            _thread = new Thread(() =>
            {
                string? falha = null;
                var cancelou = false;

                try
                {
                    InstaladorDoRuntimeDirectX.Instalar(progresso, token);
                }
                catch (OperationCanceledException)
                {
                    cancelou = true;
                }
                catch (FalhaNoRuntimeException erro)
                {
                    falha = erro.Message;
                }
                catch (Exception erro)
                {
                    falha = $"Erro inesperado: {erro.Message}";
                }

                Concluir(cancelou, falha);
            })
            {
                IsBackground = true,
                Name = "runtime-directx"
            };

            _thread.Start();
        }

        private void Mostrar(ProgressoDoRuntime progresso)
        {
            if (IsDisposed) return;

            _etapa.Text = progresso.Etapa;

            if (progresso.Percentual is int percentual)
            {
                _barra.Style = ProgressBarStyle.Continuous;
                _barra.Value = Math.Max(0, Math.Min(100, percentual));
            }
            else
            {
                _barra.Style = ProgressBarStyle.Marquee;
            }
        }

        private void Concluir(bool cancelou, string? falha)
        {
            if (IsDisposed) return;

            void Finalizar()
            {
                Cancelado = cancelou;
                Falha = falha;
                DialogResult = falha is null && !cancelou ? DialogResult.OK : DialogResult.Cancel;
                Close();
            }

            try
            {
                if (InvokeRequired) BeginInvoke((Action)Finalizar);
                else Finalizar();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Cancelar()
        {
            _botaoCancelar.Enabled = false;
            _botaoCancelar.Text = "Cancelando...";
            _cancelamento.Cancel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Fechar no X vale como cancelar: a thread limpa a pasta de preparo antes de sair.
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
