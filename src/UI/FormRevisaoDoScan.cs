using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Mochila.Modelo;
using Mochila.Scanner;

namespace Mochila.UI
{
    /// <summary>
    /// ListView que avisa quando rolou. Sem isso, o combo sobreposto fica boiando na
    /// posição antiga quando a lista se move.
    /// </summary>
    internal sealed class ListaComRolagem : ListView
    {
        private const int WmVScroll = 0x0115;
        private const int WmHScroll = 0x0114;
        private const int WmMouseWheel = 0x020A;

        public event EventHandler? Rolou;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmVScroll || m.Msg == WmHScroll || m.Msg == WmMouseWheel)
                Rolou?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Janela de revisão do scan. Nada é gravado sem passar por aqui.
    ///
    /// Uma linha por jogo: checkbox, título proposto (editável), executável escolhido
    /// (combo com os outros candidatos, ordenados por placar) e o placar. Linha amarela
    /// = placar abaixo de 30, ou seja, o scanner chutou e eu preciso olhar.
    /// </summary>
    public sealed class FormRevisaoDoScan : Form
    {
        private const int ColunaExecutavel = 1;
        private const int ColunaPlacar = 2;
        private const int ColunaFixar = 3;

        private const string MarcaFixado = "🔒 fixado";
        private const string MarcaLivre = "—";

        /// <summary>
        /// Altura da barra de ações e do rodapé, derivadas da fonte em vez de fixas em
        /// pixel: em tela com escala de 125% um número cravado corta o texto pela metade.
        /// </summary>
        private static int AlturaDaBarraDeBotoes => Math.Max(52, SystemFonts.MessageBoxFont.Height + 34);

        private static int AlturaDoRodape => Math.Max(26, SystemFonts.MessageBoxFont.Height + 12);

        /// <summary>Painel de detalhes: o suficiente para o placar, sem comer a lista.</summary>
        private const int AlturaDosDetalhes = 92;

        private readonly List<JogoRevisado> _linhas = new List<JogoRevisado>();
        private readonly ListaComRolagem _lista;
        private readonly ComboBox _comboDeCandidatos;
        private readonly TextBox _detalhes;
        private readonly Label _rodape;

        private Button _botaoAdicionar = null!;
        private Button _botaoCancelar = null!;
        private Panel _painelDeDetalhes = null!;

        private int _linhaEmEdicao = -1;
        private bool _preenchendoCombo;

        // ---- Acesso para o auto-teste ----------------------------------------------------------
        //
        // O bug que motivou isto (botões desenhados fora da janela) passou por 373 testes
        // justamente porque nenhum deles olhava a geometria da tela.

        internal Button BotaoAdicionar => _botaoAdicionar;

        internal Button BotaoCancelar => _botaoCancelar;

        internal Control PainelDeDetalhes => _painelDeDetalhes;

        internal Label Rodape => _rodape;

        /// <param name="biblioteca">
        /// Biblioteca atual, quando existe. Serve para a linha já nascer com o estado real:
        /// jogo que eu tinha fixado aparece com o cadeado marcado e apontando para o
        /// executável fixado, de modo que confirmar um rescan sem mexer em nada não desfaz
        /// a trava — e destravar continua sendo um clique meu.
        /// </param>
        public FormRevisaoDoScan(IEnumerable<JogoDetectado> detectados,
                                 IReadOnlyList<PastaDescartada>? descartes = null,
                                 Biblioteca? biblioteca = null)
        {
            foreach (var detectado in detectados) _linhas.Add(new JogoRevisado(detectado));
            Descartes = descartes ?? Array.Empty<PastaDescartada>();

            if (biblioteca != null) AplicarEstadoDaBiblioteca(biblioteca);

            Text = "Revisão do scan — confirme antes de gravar";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1100, 620);
            MinimumSize = new Size(820, 460);
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;
            KeyPreview = true;

            _lista = CriarLista();
            _comboDeCandidatos = CriarCombo();
            _detalhes = CriarDetalhes();
            _rodape = CriarRodape();

            _lista.Controls.Add(_comboDeCandidatos);

            Controls.Add(_lista);
            Controls.Add(CriarPainelDeDetalhes());
            Controls.Add(CriarCabecalho());
            Controls.Add(CriarPainelDeBotoes());
            Controls.Add(_rodape);

            PreencherLista();
            AtualizarRodape();

            KeyDown += AoTeclar;
        }

        // ---- Resultado -----------------------------------------------------------------------

        /// <summary>Todas as linhas, marcadas ou não.</summary>
        public IReadOnlyList<JogoRevisado> Linhas => _linhas;

        /// <summary>Pastas que o scanner olhou e descartou, mostradas no rodapé.</summary>
        public IReadOnlyList<PastaDescartada> Descartes { get; }

        /// <summary>Só as linhas marcadas — é o que vai para a biblioteca.</summary>
        public List<JogoRevisado> Confirmados()
        {
            var confirmados = new List<JogoRevisado>();
            foreach (var linha in _linhas)
            {
                if (linha.Incluir) confirmados.Add(linha);
            }
            return confirmados;
        }

        // ---- Operações da tela ------------------------------------------------------------------
        //
        // Marcar, trocar candidato e listar o combo são métodos de verdade, não código solto
        // dentro dos eventos. Assim o auto-teste exercita exatamente o que o clique exercita,
        // sem precisar de janela na tela.

        /// <summary>Marca ou desmarca uma linha (o mesmo que clicar no checkbox).</summary>
        public void DefinirInclusao(int indice, bool incluir)
        {
            if (indice < 0 || indice >= _linhas.Count) return;

            _linhas[indice].Incluir = incluir;
            _lista.Items[indice].Checked = incluir;
            AtualizarRodape();
        }

        /// <summary>O que aparece no combo daquela linha, na ordem em que aparece.</summary>
        public List<string> OpcoesDoCombo(int indice)
        {
            var opcoes = new List<string>();
            if (indice < 0 || indice >= _linhas.Count) return opcoes;

            foreach (var candidato in _linhas[indice].Deteccao.Candidatos)
                opcoes.Add(DescreverCandidato(candidato));

            return opcoes;
        }

        /// <summary>Troca o executável escolhido (o mesmo que escolher no combo).</summary>
        public void TrocarCandidato(int indiceDaLinha, int indiceDoCandidato)
        {
            if (indiceDaLinha < 0 || indiceDaLinha >= _linhas.Count) return;

            var linha = _linhas[indiceDaLinha];
            if (indiceDoCandidato < 0 || indiceDoCandidato >= linha.Deteccao.Candidatos.Count) return;

            linha.Escolhido = linha.Deteccao.Candidatos[indiceDoCandidato];

            AtualizarLinha(indiceDaLinha);
            AtualizarRodape();
        }

        /// <summary>
        /// Liga/desliga o "não alterar em rescan" de uma linha (o mesmo que clicar na
        /// coluna do cadeado ou apertar Ctrl+L).
        /// </summary>
        public void DefinirFixado(int indice, bool fixar)
        {
            if (indice < 0 || indice >= _linhas.Count) return;

            _linhas[indice].Fixar = fixar;
            AtualizarLinha(indice);
            AtualizarRodape();
        }

        /// <summary>
        /// Aceita ou desfaz a religação proposta para a linha. Enquanto não for aceita, a
        /// linha adiciona um jogo novo, como sempre fez.
        /// </summary>
        public void DefinirReligacao(int indice, bool religar)
        {
            if (indice < 0 || indice >= _linhas.Count) return;

            var linha = _linhas[indice];
            if (linha.ReligacaoPossivel is not { } candidato) return;

            linha.ReligarComId = religar ? candidato.Id : null;

            // Religar é sobre um jogo que eu já tinha: não faz sentido deixá-lo de fora.
            if (religar) linha.Incluir = true;

            AtualizarLinha(indice);
            AtualizarRodape();
        }

        /// <summary>
        /// O título como ele aparece na lista. A oferta de religação vive aqui, ao lado do
        /// nome, porque é sobre este jogo que ela fala — coluna nova só para isso seria
        /// espaço gasto em algo que aparece uma vez a cada dez scans.
        /// </summary>
        private static string TextoDoTitulo(JogoRevisado linha)
        {
            if (linha.ReligacaoPossivel is not { } candidato) return linha.Titulo;

            return linha.VaiReligar
                ? $"{linha.Titulo}   ↩ religa com \"{candidato.Titulo}\" (mantém tempo e capa)"
                : $"{linha.Titulo}   ↩ Ctrl+R religa com \"{candidato.Titulo}\", que sumiu do lugar";
        }

        /// <summary>Renomeia (o mesmo que editar o título com F2).</summary>
        public void RenomearTitulo(int indice, string titulo)
        {
            if (indice < 0 || indice >= _linhas.Count) return;
            if (string.IsNullOrWhiteSpace(titulo)) return;

            _linhas[indice].Titulo = titulo.Trim();
            AtualizarLinha(indice);
        }

        /// <summary>Cor de fundo da linha — amarela quando o placar está abaixo de 30.</summary>
        public Color CorDaLinha(int indice)
            => indice < 0 || indice >= _lista.Items.Count ? Tema.Fundo : _lista.Items[indice].BackColor;

        /// <summary>Texto da linha como ele aparece na lista: título, executável e placar.</summary>
        public string[] TextoDaLinha(int indice)
        {
            if (indice < 0 || indice >= _lista.Items.Count) return Array.Empty<string>();

            var item = _lista.Items[indice];
            var textos = new string[item.SubItems.Count];
            for (var i = 0; i < item.SubItems.Count; i++) textos[i] = item.SubItems[i].Text;

            return textos;
        }

        /// <summary>
        /// Traz para as linhas o que a biblioteca já sabe: o cadeado e, nos jogos fixados,
        /// o executável que eu escolhi da última vez.
        ///
        /// Jogo destravado NÃO tem o executável restaurado de propósito: ele continua
        /// mostrando o que o scanner propôs agora, que é justamente o rescan podendo
        /// corrigir a escolha anterior.
        /// </summary>
        private void AplicarEstadoDaBiblioteca(Biblioteca biblioteca)
        {
            AnotarEstado(biblioteca);

            // Pasta renomeada faz o rescan achar que descobriu um jogo novo. Isto só marca
            // a possibilidade; aceitar é Ctrl+R, e é decisão minha — título igual não é prova.
            MescladorDeBiblioteca.SugerirReligacoes(biblioteca, _linhas);
        }

        private void AnotarEstado(Biblioteca biblioteca)
        {
            foreach (var linha in _linhas)
            {
                var existente = MescladorDeBiblioteca.ObterPorPastaDoJogo(biblioteca, linha.PastaDoJogo);
                if (existente is null) continue;

                linha.JaNaBiblioteca = true;
                linha.Titulo = existente.Titulo;
                linha.Fixar = existente.ExecutavelFixadoPeloUsuario;

                if (!existente.ExecutavelFixadoPeloUsuario) continue;

                var fixado = existente.CaminhoExecutavel();
                if (fixado is null) continue;

                foreach (var candidato in linha.Deteccao.Candidatos)
                {
                    if (string.Equals(candidato.Caminho, fixado, StringComparison.OrdinalIgnoreCase))
                    {
                        linha.Escolhido = candidato;
                        break;
                    }
                }
            }
        }

        // ---- Montagem da tela -----------------------------------------------------------------

        private ListaComRolagem CriarLista()
        {
            var lista = new ListaComRolagem
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                LabelEdit = true,               // F2 edita o título
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Tema.Fundo,
                ForeColor = Tema.Texto,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f)
            };

            lista.Columns.Add("Título  (F2 para editar)", 260);
            lista.Columns.Add("Executável escolhido  (clique para trocar)", 340);
            lista.Columns.Add("Placar", 55, HorizontalAlignment.Right);
            lista.Columns.Add("Não alterar em rescan", 140);
            lista.Columns.Add("Pasta do jogo", 280);

            lista.ItemChecked += (_, e) =>
            {
                if (e.Item.Tag is JogoRevisado linha) linha.Incluir = e.Item.Checked;
                AtualizarRodape();
            };
            lista.SelectedIndexChanged += (_, _) => { EsconderCombo(); MostrarDetalhes(); };
            lista.MouseUp += AoClicarNaLista;
            lista.AfterLabelEdit += AoEditarTitulo;
            lista.Rolou += (_, _) => EsconderCombo();

            return lista;
        }

        private ComboBox CriarCombo()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Visible = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
                Font = new Font("Segoe UI", 9f),
                // Lista longa (uma pasta pode ter 15 candidatos) não pode passar da tela.
                MaxDropDownItems = 12,
                IntegralHeight = false
            };

            combo.SelectedIndexChanged += AoTrocarCandidato;
            combo.DropDownClosed += (_, _) => EsconderCombo();
            combo.LostFocus += (_, _) => EsconderCombo();
            return combo;
        }

        private static TextBox CriarDetalhes() => new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Tema.Superficie,
            ForeColor = Tema.TextoFraco,
            Font = new Font("Consolas", 8.5f)
        };

        /// <summary>
        /// Explicação do placar do jogo selecionado. Não é log do scan: é a evidência que
        /// justifica a linha amarela, e é o que me deixa decidir se troco o executável.
        /// Some quando não há nada a dizer, em vez de ficar ocupando meia janela vazio.
        /// </summary>
        private Control CriarPainelDeDetalhes()
        {
            _painelDeDetalhes = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = AlturaDosDetalhes,
                BackColor = Tema.Superficie,
                Padding = new Padding(12, 6, 12, 6),
                Visible = false
            };
            _painelDeDetalhes.Controls.Add(_detalhes);
            return _painelDeDetalhes;
        }

        private Control CriarCabecalho()
        {
            var painel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Tema.Superficie,
                Padding = new Padding(12, 8, 12, 8)
            };

            painel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Tema.TextoFraco,
                Text = "Desmarque o que não é jogo, corrija o título com F2 e troque o executável clicando na coluna do meio." +
                       Environment.NewLine +
                       "\"Não alterar em rescan\" (clique na coluna ou Ctrl+L) trava a escolha; trocar de executável, sozinho, não trava nada." +
                       Environment.NewLine +
                       "Linhas em amarelo têm placar baixo — o scanner não está seguro delas. Nada é gravado até você confirmar."
            });

            return painel;
        }

        /// <summary>
        /// Barra de ações.
        ///
        /// Nada de coordenada absoluta aqui. A versão anterior posicionava os botões da
        /// direita com "ClientSize.Width - 172" e deixava o Anchor cuidar do resto — só
        /// que, na hora do Add, o painel ainda tinha o tamanho PADRÃO de 200 px. O âncora
        /// gravou uma distância negativa de centenas de pixels e manteve os dois botões
        /// fora da janela para sempre. Não dava para concluir o scan pela interface.
        ///
        /// Com dois FlowLayoutPanel o layout se resolve sozinho, em qualquer largura e
        /// qualquer DPI: um encosta à esquerda, o outro à direita, e nenhum dos dois
        /// depende de eu saber o tamanho da janela antes dela existir.
        /// </summary>
        private Control CriarPainelDeBotoes()
        {
            var painel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = AlturaDaBarraDeBotoes,
                BackColor = Tema.Fundo,
                Padding = new Padding(12, 8, 12, 8)
            };

            var marcarTodos = Botoes.Criar("Marcar todos", Point.Empty, 110);
            marcarTodos.Click += (_, _) => MarcarTodos(true);

            var desmarcarTodos = Botoes.Criar("Desmarcar todos", Point.Empty, 120);
            desmarcarTodos.Click += (_, _) => MarcarTodos(false);

            var soConfiaveis = Botoes.Criar("Só os confiáveis", Point.Empty, 120);
            soConfiaveis.Click += (_, _) => MarcarSomenteConfiaveis();

            _botaoCancelar = Botoes.Criar("Cancelar", Point.Empty, 110);
            _botaoCancelar.Click += (_, _) => FecharSemGravar();

            _botaoAdicionar = Botoes.Criar(TextoDeAdicionar(_linhas.Count), Point.Empty, 190);
            _botaoAdicionar.Click += (_, _) => Confirmar();

            // RightToLeft: o primeiro entra encostado na borda direita. Cancelar por
            // último à direita, Adicionar à esquerda dele — a ordem do Windows.
            var daDireita = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Padding.Empty
            };
            daDireita.Controls.Add(_botaoCancelar);
            daDireita.Controls.Add(_botaoAdicionar);

            var daEsquerda = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Padding.Empty
            };
            daEsquerda.Controls.Add(marcarTodos);
            daEsquerda.Controls.Add(desmarcarTodos);
            daEsquerda.Controls.Add(soConfiaveis);

            // A direita entra primeiro: numa janela estreita, quem espreme é o grupo da
            // esquerda. Confirmar e cancelar não podem sumir nunca.
            painel.Controls.Add(daDireita);
            painel.Controls.Add(daEsquerda);

            AcceptButton = _botaoAdicionar;
            CancelButton = _botaoCancelar;

            return painel;
        }

        /// <summary>O texto diz quantos serão gravados — some a dúvida do "quantos mesmo?".</summary>
        private static string TextoDeAdicionar(int marcados)
            => marcados == 1 ? "Adicionar 1 jogo" : $"Adicionar {marcados} jogo(s)";

        private static Label CriarRodape() => new Label
        {
            Dock = DockStyle.Bottom,
            // Altura vinda da fonte, não cravada: com escala de 125% um 26 fixo corta a
            // linha de status pela metade.
            Height = AlturaDoRodape,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
            BackColor = Tema.Superficie,
            ForeColor = Tema.TextoFraco
        };

        // ---- Preenchimento --------------------------------------------------------------------

        private void PreencherLista()
        {
            _lista.BeginUpdate();
            _lista.Items.Clear();

            foreach (var linha in _linhas)
            {
                var item = new ListViewItem(TextoDoTitulo(linha)) { Checked = linha.Incluir, Tag = linha };
                item.SubItems.Add(linha.Escolhido.CaminhoRelativoAoJogo);
                item.SubItems.Add(linha.Escolhido.Placar.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(linha.Fixar ? MarcaFixado : MarcaLivre);
                item.SubItems.Add(linha.PastaDoJogo);

                AplicarCor(item, linha);
                _lista.Items.Add(item);
            }

            _lista.EndUpdate();

            if (_lista.Items.Count > 0) _lista.Items[0].Selected = true;
        }

        /// <summary>Amarelo = placar abaixo de 30. É o aviso de "olha esta aqui".</summary>
        private static void AplicarCor(ListViewItem item, JogoRevisado linha)
        {
            if (linha.BaixaConfianca)
            {
                item.BackColor = Tema.FundoBaixaConfianca;
                item.ForeColor = Tema.TextoBaixaConfianca;
            }
            else
            {
                item.BackColor = Tema.Fundo;
                item.ForeColor = Tema.Texto;
            }
        }

        private void AtualizarLinha(int indice)
        {
            var item = _lista.Items[indice];
            if (item.Tag is not JogoRevisado linha) return;

            item.Text = TextoDoTitulo(linha);
            item.SubItems[ColunaExecutavel].Text = linha.Escolhido.CaminhoRelativoAoJogo;
            item.SubItems[ColunaPlacar].Text = linha.Escolhido.Placar.ToString(CultureInfo.InvariantCulture);
            item.SubItems[ColunaFixar].Text = linha.Fixar ? MarcaFixado : MarcaLivre;

            AplicarCor(item, linha);
        }

        private void AtualizarRodape()
        {
            var marcados = Confirmados().Count;
            var baixaConfianca = 0;
            var fixados = 0;

            foreach (var linha in _linhas)
            {
                if (linha.BaixaConfianca) baixaConfianca++;
                if (linha.Fixar) fixados++;
            }

            _rodape.Text =
                $"{marcados} de {_linhas.Count} jogo(s) marcado(s)  |  " +
                $"{baixaConfianca} com placar abaixo de {Pontuador.PlacarDeConfianca}  |  " +
                $"{fixados} travado(s) contra rescan  |  " +
                $"{Descartes.Count} pasta(s) descartada(s) pelo scanner";

            // O botão diz o que vai acontecer, e não deixa confirmar o nada.
            _botaoAdicionar.Text = TextoDeAdicionar(marcados);
            _botaoAdicionar.Enabled = marcados > 0;
        }

        private void MostrarDetalhes()
        {
            if (LinhaSelecionada() is not { } linha)
            {
                _detalhes.Text = "";
                _painelDeDetalhes.Visible = false;
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Pasta: {linha.PastaDoJogo}");
            sb.AppendLine($"{linha.Deteccao.Candidatos.Count} candidato(s), do melhor para o pior:");
            sb.AppendLine();

            foreach (var candidato in linha.Deteccao.Candidatos)
            {
                var marca = ReferenceEquals(candidato, linha.Escolhido) ? "-> " : "   ";
                sb.AppendLine(marca + candidato.Detalhar());
            }

            _detalhes.Text = sb.ToString();
            _detalhes.SelectionStart = 0;
            _detalhes.ScrollToCaret();

            _painelDeDetalhes.Visible = true;
        }

        // ---- Interação -------------------------------------------------------------------------

        private JogoRevisado? LinhaSelecionada()
            => _lista.SelectedItems.Count == 0 ? null : _lista.SelectedItems[0].Tag as JogoRevisado;

        private void AoTeclar(object? remetente, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                FecharSemGravar();
                return;
            }

            // Ctrl+L alterna a trava da linha selecionada, para não precisar do mouse.
            if (e.Control && e.KeyCode == Keys.L && _lista.SelectedItems.Count > 0)
            {
                var indice = _lista.SelectedItems[0].Index;
                DefinirFixado(indice, !_linhas[indice].Fixar);
                e.Handled = true;
                return;
            }

            // Ctrl+R aceita (ou desfaz) a religação proposta.
            if (e.Control && e.KeyCode == Keys.R && _lista.SelectedItems.Count > 0)
            {
                var indice = _lista.SelectedItems[0].Index;
                DefinirReligacao(indice, !_linhas[indice].VaiReligar);
                e.Handled = true;
            }
        }

        private void AoClicarNaLista(object? remetente, MouseEventArgs e)
        {
            var item = _lista.GetItemAt(e.X, e.Y);
            if (item is null) return;

            if (DentroDaColuna(item, ColunaExecutavel, e.X))
            {
                AbrirCombo(item);
                return;
            }

            // Clicar na coluna do cadeado alterna a trava. Nada de trancar por efeito
            // colateral: fixar é sempre um clique meu, aqui.
            if (DentroDaColuna(item, ColunaFixar, e.X))
                DefinirFixado(item.Index, !_linhas[item.Index].Fixar);
        }

        private static bool DentroDaColuna(ListViewItem item, int coluna, int x)
        {
            if (coluna >= item.SubItems.Count) return false;

            var limites = item.SubItems[coluna].Bounds;
            return x >= limites.Left && x <= limites.Right;
        }

        private void AbrirCombo(ListViewItem item)
        {
            if (item.Tag is not JogoRevisado linha) return;

            var limites = item.SubItems[ColunaExecutavel].Bounds;

            _preenchendoCombo = true;
            _comboDeCandidatos.Items.Clear();

            var selecionado = 0;
            var opcoes = OpcoesDoCombo(item.Index);
            for (var i = 0; i < opcoes.Count; i++)
            {
                _comboDeCandidatos.Items.Add(opcoes[i]);

                if (ReferenceEquals(linha.Deteccao.Candidatos[i], linha.Escolhido)) selecionado = i;
            }

            _comboDeCandidatos.SelectedIndex = selecionado;
            _preenchendoCombo = false;

            _linhaEmEdicao = item.Index;
            _comboDeCandidatos.Bounds = new Rectangle(limites.Left, limites.Top, limites.Width, limites.Height);
            _comboDeCandidatos.Visible = true;
            _comboDeCandidatos.BringToFront();
            _comboDeCandidatos.Focus();
            _comboDeCandidatos.DroppedDown = true;
        }

        /// <summary>
        /// Texto de cada item do combo. O vetado continua na lista, marcado — pode ser que
        /// um dia eu precise mesmo apontar para um deles.
        /// </summary>
        public static string DescreverCandidato(CandidatoExecutavel candidato)
        {
            var sb = new StringBuilder();
            sb.Append(candidato.CaminhoRelativoAoJogo)
              .Append("   (")
              .Append(candidato.Placar.ToString(CultureInfo.InvariantCulture))
              .Append(" pts");

            if (candidato.Excluido) sb.Append(", EXCLUÍDO: ").Append(candidato.MotivoDaExclusao);
            if (candidato.Promovido) sb.Append(", ").Append(candidato.MotivoDaPromocao);
            if (!candidato.EhExe) sb.Append(", não é .exe");

            return sb.Append(')').ToString();
        }

        private void AoTrocarCandidato(object? remetente, EventArgs e)
        {
            if (_preenchendoCombo || _linhaEmEdicao < 0) return;

            TrocarCandidato(_linhaEmEdicao, _comboDeCandidatos.SelectedIndex);
            MostrarDetalhes();
        }

        private void EsconderCombo()
        {
            if (!_comboDeCandidatos.Visible) return;

            _comboDeCandidatos.Visible = false;
            _linhaEmEdicao = -1;
        }

        private void AoEditarTitulo(object? remetente, LabelEditEventArgs e)
        {
            var novo = e.Label?.Trim();

            // Cancelou a edição (Esc) ou apagou tudo: mantém o que estava.
            if (string.IsNullOrEmpty(novo))
            {
                e.CancelEdit = true;
                return;
            }

            if (_lista.Items[e.Item].Tag is JogoRevisado linha) linha.Titulo = novo!;
        }

        public void MarcarTodos(bool marcar)
        {
            _lista.BeginUpdate();
            for (var i = 0; i < _linhas.Count; i++) DefinirInclusao(i, marcar);
            _lista.EndUpdate();
        }

        /// <summary>Desmarca só as linhas amarelas — o atalho para "aceito o que o scanner tem certeza".</summary>
        public void MarcarSomenteConfiaveis()
        {
            _lista.BeginUpdate();
            for (var i = 0; i < _linhas.Count; i++) DefinirInclusao(i, !_linhas[i].BaixaConfianca);
            _lista.EndUpdate();
        }

        private void Confirmar()
        {
            EsconderCombo();

            if (Confirmados().Count == 0)
            {
                var resposta = MessageBox.Show(this,
                    "Nenhum jogo marcado. Fechar sem gravar nada?",
                    "Revisão do scan", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (resposta != DialogResult.Yes) return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void FecharSemGravar()
        {
            EsconderCombo();
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
