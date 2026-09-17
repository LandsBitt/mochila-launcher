using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Mochila.Dados;
using Mochila.Execucao;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// As opções de execução de um jogo (fase 15): prioridade do processo e os scripts de
    /// antes e depois.
    ///
    /// Para acervo de 2003 a 2015 isto é ouro — dgVoodoo, troca de resolução, x360ce,
    /// mapeamento de joystick, tudo resolvido por um <c>.bat</c> que eu escrevo — mas é
    /// coisa de configurar uma vez por jogo e nunca mais olhar. Daí ser uma janela chamada
    /// pela tela de detalhes, e não mais três controles dentro dela: a tela de detalhes é o
    /// que eu abro para ver quanto joguei, e ela já está cheia.
    ///
    /// A tela de detalhes continua sendo quem MOSTRA o que está configurado — a linha
    /// "Execução" da ficha aparece quando o jogo sai do padrão.
    /// </summary>
    public sealed class FormOpcoesDeExecucao : Form
    {
        private const int Margem = 18;
        private const int Largura = 560;

        private readonly Jogo _jogo;
        private readonly ComboEscuro _prioridade;
        private readonly Label _antes;
        private readonly Label _depois;

        private string? _scriptAntes;
        private string? _scriptDepois;

        public FormOpcoesDeExecucao(Jogo jogo)
        {
            _jogo = jogo ?? throw new ArgumentNullException(nameof(jogo));

            _scriptAntes = jogo.OpcoesDeExecucao.ScriptAntes;
            _scriptDepois = jogo.OpcoesDeExecucao.ScriptDepois;

            Text = $"Execução — {jogo.Titulo}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(Largura, 366);

            Tema.AplicarNaJanela(this);

            _prioridade = new ComboEscuro
            {
                Location = new Point(Margem, 42),
                Size = new Size(220, 24)
            };
            _prioridade.Items.AddRange(new object[] { "Normal", "Acima do normal", "Alta" });
            _prioridade.SelectedIndex = (int)jogo.OpcoesDeExecucao.Prioridade;

            _antes = CaminhoDoScript(112);
            _depois = CaminhoDoScript(216);

            Controls.Add(Rotulo("Prioridade do processo", Margem, 18, negrito: true));
            Controls.Add(_prioridade);
            Controls.Add(Rotulo(
                "\"Alta\" ajuda jogo antigo que divide o PC com outra coisa. Não é tempo real: " +
                "isso tiraria ciclo do próprio mouse.",
                Margem + 232, 46, cor: Tema.TextoFraco, largura: Largura - Margem - 240, altura: 34));

            MontarBloco("Script antes de abrir (.bat ou .cmd)", 88, _antes,
                        () => Escolher(antes: true), () => Definir(antes: true, relativo: null));

            MontarBloco("Script depois de fechar", 192, _depois,
                        () => Escolher(antes: false), () => Definir(antes: false, relativo: null));

            Controls.Add(Rotulo(
                "O script roda na pasta do jogo. O de antes segura o lançamento por até " +
                $"{ScriptsDoJogo.LimiteDoAntes.TotalSeconds:F0} s; passou disso, o jogo abre assim mesmo. " +
                "O launcher nunca escreve nem altera esses arquivos — quem escreve é você.",
                Margem, 274, cor: Tema.TextoFraco, largura: Largura - (2 * Margem), altura: 48));

            var salvar = Botoes.CriarPrincipal("Salvar", new Point(Largura - Margem - 196, 322), 92);
            salvar.DialogResult = DialogResult.OK;

            var cancelar = Botoes.Criar("Cancelar", new Point(Largura - Margem - 96, 322), 96);
            cancelar.DialogResult = DialogResult.Cancel;

            Controls.Add(salvar);
            Controls.Add(cancelar);

            AcceptButton = salvar;
            CancelButton = cancelar;

            AtualizarCaminhos();
        }

        // ---- O que a janela devolve ------------------------------------------------------

        public PrioridadeDoProcesso PrioridadeEscolhida
            => (PrioridadeDoProcesso)Math.Max(0, _prioridade.SelectedIndex);

        public string? ScriptAntesEscolhido => _scriptAntes;

        public string? ScriptDepoisEscolhido => _scriptDepois;

        /// <summary>Grava o que foi escolhido no jogo. Devolve true se alguma coisa mudou.</summary>
        public bool AplicarEm(Jogo jogo)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));

            var opcoes = jogo.OpcoesDeExecucao;

            var mudou = opcoes.Prioridade != PrioridadeEscolhida ||
                        !string.Equals(opcoes.ScriptAntes, _scriptAntes, StringComparison.Ordinal) ||
                        !string.Equals(opcoes.ScriptDepois, _scriptDepois, StringComparison.Ordinal);

            opcoes.Prioridade = PrioridadeEscolhida;
            opcoes.ScriptAntes = _scriptAntes;
            opcoes.ScriptDepois = _scriptDepois;

            return mudou;
        }

        /// <summary>Escolhe um script sem abrir seletor de arquivo. Só diagnóstico.</summary>
        internal bool EscolherParaDiagnostico(bool antes, string caminhoAbsoluto, out string recusa)
        {
            if (!ScriptsDoJogo.TentarAceitar(caminhoAbsoluto, out var relativo, out recusa)) return false;

            Definir(antes, relativo);
            return true;
        }

        // ---- Ações -----------------------------------------------------------------------

        /// <summary>
        /// O seletor só oferece .bat e .cmd, e começa na pasta do jogo — que é onde o
        /// script de um jogo tem qualquer chance de estar. O filtro do diálogo é
        /// conveniência; quem recusa de verdade é <see cref="ScriptsDoJogo.TentarAceitar"/>,
        /// porque "Todos os arquivos" está sempre a um clique de distância.
        /// </summary>
        private void Escolher(bool antes)
        {
            using (var dialogo = new OpenFileDialog
                   {
                       Title = antes ? "Script antes de abrir o jogo" : "Script depois de fechar o jogo",
                       Filter = "Scripts (*.bat;*.cmd)|*.bat;*.cmd|Todos os arquivos (*.*)|*.*",
                       InitialDirectory = PastaInicial(),
                       CheckFileExists = true
                   })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK) return;

                if (!ScriptsDoJogo.TentarAceitar(dialogo.FileName, out var relativo, out var recusa))
                {
                    MessageBox.Show(this, recusa, "Script", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Definir(antes, relativo);
            }
        }

        private void Definir(bool antes, string? relativo)
        {
            if (antes) _scriptAntes = relativo;
            else _scriptDepois = relativo;

            AtualizarCaminhos();
        }

        private string PastaInicial()
        {
            var pasta = _jogo.PastaDoJogo();
            return !string.IsNullOrEmpty(pasta) && Directory.Exists(pasta) ? pasta! : Caminhos.PastaBase;
        }

        /// <summary>
        /// Mostra o relativo, e em vermelho quando o arquivo não está mais lá. Um script
        /// que sumiu não impede nada no lançamento — mas descobrir isso aqui é bem melhor
        /// que descobrir por um recado no rodapé daqui a três meses.
        /// </summary>
        private void AtualizarCaminhos()
        {
            Mostrar(_antes, _scriptAntes);
            Mostrar(_depois, _scriptDepois);
        }

        private static void Mostrar(Label rotulo, string? relativo)
        {
            if (string.IsNullOrEmpty(relativo))
            {
                rotulo.Text = "(nenhum)";
                rotulo.ForeColor = Tema.TextoFraco;
                return;
            }

            var caminho = Caminhos.ParaAbsolutoOuNulo(relativo);
            var existe = caminho is not null && File.Exists(caminho);

            rotulo.Text = relativo + (existe ? "" : "   [NÃO ENCONTRADO]");
            rotulo.ForeColor = existe ? Tema.Texto : Tema.Erro;
        }

        // ---- Montagem ---------------------------------------------------------------------

        private void MontarBloco(string titulo, int y, Label caminho, Action aoEscolher, Action aoLimpar)
        {
            Controls.Add(Rotulo(titulo, Margem, y, negrito: true));
            Controls.Add(caminho);

            var escolher = Botoes.Criar("Escolher...", new Point(Margem, y + 46), 108, 26);
            escolher.Click += (_, _) => aoEscolher();

            var limpar = Botoes.Criar("Limpar", new Point(Margem + 116, y + 46), 90, 26);
            limpar.Click += (_, _) => aoLimpar();

            Controls.Add(escolher);
            Controls.Add(limpar);
        }

        private static Label CaminhoDoScript(int y) => new Label
        {
            Location = new Point(Margem, y),
            Size = new Size(Largura - (2 * Margem), 20),
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };

        private static Label Rotulo(string texto, int x, int y, Color? cor = null,
                                    int largura = 320, int altura = 20, bool negrito = false)
        {
            var rotulo = new Label
            {
                Text = texto,
                Location = new Point(x, y),
                Size = new Size(largura, altura),
                ForeColor = cor ?? Tema.Texto,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };

            if (negrito) rotulo.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            return rotulo;
        }
    }
}
