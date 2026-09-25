using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Mochila.Dados;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// Configurações do launcher.
    ///
    /// A chave do SteamGridDB, as pastas que o F6 escaneia, a aparência (tema e cor de
    /// acento, fase 17), o tamanho padrão do card e o botão de limpar o cache.
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
        private readonly ComboBox _tema;
        private readonly TextBox _acento;
        private readonly Panel _amostraDoAcento;
        private readonly CheckBox _continuarJogando;
        private readonly Label _situacaoDoCache;
        private readonly Label _situacaoDoRuntime;
        private readonly Button _prepararRuntime;
        private readonly Button _removerRuntime;

        /// <summary>Guardado em campo só para o teste de geometria alcançá-lo.</summary>
        private Button? _botaoDasEstatisticas;

        private Button? _botaoDaIntegridade;

        internal CheckBox CaixaDeContinuarJogando => _continuarJogando;

        internal Button? BotaoDasEstatisticas => _botaoDasEstatisticas;

        internal Button? BotaoDaIntegridade => _botaoDaIntegridade;

        internal ComboBox CaixaDoTema => _tema;

        internal TextBox CampoDoAcento => _acento;

        internal ListBox ListaDePastas => _pastas;

        internal Label SituacaoDoCache => _situacaoDoCache;

        internal Label SituacaoDoRuntime => _situacaoDoRuntime;

        /// <summary>true quando algo mudou e a janela principal precisa recarregar.</summary>
        public bool Mudou { get; private set; }

        /// <summary>true quando o cache foi limpo — a grade precisa redesenhar.</summary>
        public bool CacheLimpo { get; private set; }

        /// <summary>
        /// true quando o tema ou a cor de acento mudaram (fase 17).
        ///
        /// A janela principal usa isto para oferecer a reabertura. A paleta é copiada por
        /// cada controle no construtor dele, então trocar de tema com a janela montada não
        /// repinta nada — e fingir que aplicou seria pior que dizer a verdade.
        /// </summary>
        public bool TemaMudou { get; private set; }

        /// <summary>
        /// true quando eu cliquei em "Estatísticas do acervo".
        ///
        /// A janela principal abre a tela depois desta fechar, em vez de esta abrir um
        /// segundo modal em cima de si mesma — ver <c>FormPrincipal.AbrirConfiguracoes</c>.
        /// </summary>
        public bool PediuEstatisticas { get; private set; }

        /// <summary>
        /// true quando eu cliquei em "Integridade do acervo" (fase 17). Mesma mecânica do
        /// <see cref="PediuEstatisticas"/>, e pelo mesmo motivo: a tela abre depois desta
        /// fechar, não como um segundo modal em cima do primeiro.
        /// </summary>
        public bool PediuIntegridade { get; private set; }

        public FormConfiguracoes(Config config, Biblioteca biblioteca)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _biblioteca = biblioteca ?? throw new ArgumentNullException(nameof(biblioteca));

            Text = "Configurações";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 690);
            MinimumSize = new Size(560, 650);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;
            KeyPreview = true;

            _chave = new TextBox
            {
                Text = config.SteamGridDbApiKey,
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
                BorderStyle = BorderStyle.FixedSingle
            };

            _mostrarChave = new CheckBox
            {
                Text = "Mostrar",
                Dock = DockStyle.Right,
                Width = 90,
                ForeColor = Tema.Texto,
                FlatStyle = FlatStyle.Flat
            };
            _mostrarChave.CheckedChanged += (_, _) => _chave.UseSystemPasswordChar = !_mostrarChave.Checked;

            _pastas = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
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
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto
            };
            _tamanho.Items.AddRange(new object[] { "Card P", "Card M", "Card G" });
            _tamanho.SelectedIndex = (int)config.TamanhoCard;

            _tema = new ComboBox
            {
                Dock = DockStyle.Left,
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto
            };
            _tema.Items.AddRange(new object[] { "Tema escuro", "Tema claro" });
            _tema.SelectedIndex = config.Tema == TemaDoLauncher.Claro ? 1 : 0;

            _acento = new TextBox
            {
                Text = config.CorDeAcento,
                Dock = DockStyle.Left,
                Width = 110,
                BackColor = Tema.Controle,
                ForeColor = Tema.Texto,
                BorderStyle = BorderStyle.FixedSingle
            };
            _acento.TextChanged += (_, _) => AtualizarAmostra();

            _amostraDoAcento = new Panel { Dock = DockStyle.Left, Width = 34, Margin = new Padding(0) };

            // Borda desenhada à mão: BorderStyle.FixedSingle não deixa escolher a cor, e a
            // cor é justamente o recado — vermelha quando o texto não vira cor nenhuma.
            _amostraDoAcento.Paint += (_, e) =>
            {
                using (var caneta = new Pen(_amostraDoAcento.ForeColor))
                    e.Graphics.DrawRectangle(caneta, 0, 0, _amostraDoAcento.Width - 1, _amostraDoAcento.Height - 1);
            };

            _continuarJogando = new CheckBox
            {
                Text = "Mostrar \"Continuar jogando\" no topo da grade",
                Checked = config.MostrarContinuarJogando,
                Dock = DockStyle.Left,
                Width = 340,
                ForeColor = Tema.Texto,
                FlatStyle = FlatStyle.Flat
            };

            _situacaoDoCache = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Tema.TextoFraco
            };
            _situacaoDoRuntime = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = Tema.TextoFraco
            };

            _prepararRuntime = Botoes.Criar("Baixar da Microsoft...", Point.Empty, 190);
            _prepararRuntime.Dock = DockStyle.Left;
            _prepararRuntime.Click += (_, _) => PrepararRuntime();

            _removerRuntime = Botoes.Criar("Remover", new Point(198, 0), 110);
            _removerRuntime.Click += (_, _) => RemoverRuntime();

            AtualizarSituacaoDoCache();
            AtualizarSituacaoDoRuntime();
            AtualizarAmostra();

            Controls.Add(MontarCorpo());
            Controls.Add(MontarRodape());

            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // ---- Montagem ---------------------------------------------------------------------

        private Control MontarCorpo()
        {
            var corpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };

            // A ORDEM AQUI É A DO LAYOUT, e ela é invertida: o WinForms ancora do último
            // filho para o primeiro, e cada um tira o seu espaço do que sobrou. Ou seja,
            // quem entra por ÚLTIMO manda primeiro, e o Fill precisa ser o PRIMEIRO
            // acrescentado para ficar sendo o último a se servir.
            //
            // Isto estava trocado: a lista de pastas (Fill) entrava antes dos dois blocos
            // de baixo, engolia toda a área restante, e "Grade" e "Limpar cache" eram
            // desenhados com altura zero — a janela abria sem eles, sem erro nenhum.
            //
            // De cima para baixo, o resultado é: chave, pastas, aparência, runtime, cache.
            corpo.Controls.Add(BlocoDasPastas());
            corpo.Controls.Add(BlocoDaGrade());
            corpo.Controls.Add(BlocoDoRuntime());
            corpo.Controls.Add(BlocoDoCache());
            corpo.Controls.Add(BlocoDaChave());

            return corpo;
        }

        private Control BlocoDaChave()
        {
            var bloco = new Panel { Dock = DockStyle.Top, Height = 110 };

            var linha = new Panel { Dock = DockStyle.Top, Height = 26 };
            linha.Controls.Add(_chave);
            linha.Controls.Add(_mostrarChave);

            // Quem baixa o launcher não sabe o que é SteamGridDB, e o campo sozinho não
            // conta de onde a chave vem. O link explica o passo a passo antes de abrir o
            // navegador: cair direto na tela de login de um site desconhecido assusta.
            var comoConseguir = new LinkLabel
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Text = "Como conseguir uma chave (é grátis)",
                LinkColor = Tema.Acento,
                ActiveLinkColor = Tema.TextoForte,
                VisitedLinkColor = Tema.Acento,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = ContentAlignment.MiddleLeft
            };
            comoConseguir.LinkClicked += (_, _) => ExplicarComoConseguirChave();

            // Docking de baixo para cima: quem entra por último fica mais embaixo. O link
            // vem depois do texto para ficar embaixo dele.
            bloco.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                ForeColor = Tema.TextoFraco,
                Text = "Sem chave, a busca online fica desligada e as capas continuam pelo caminho manual\r\n" +
                       "(arrastar imagem no card, colar, ou usar a arte da pasta do jogo)."
            });
            bloco.Controls.Add(comoConseguir);

            bloco.Controls.Add(linha);
            bloco.Controls.Add(Titulo("Chave do SteamGridDB"));

            return bloco;
        }

        private void ExplicarComoConseguirChave()
        {
            var passos =
                "A chave é pessoal e gratuita. Para gerar a sua:" + Environment.NewLine + Environment.NewLine +
                "1. Entre no SteamGridDB com a sua conta Steam." + Environment.NewLine +
                "2. Na página de API (Preferences → API), clique em \"Generate API Key\"." + Environment.NewLine +
                "3. Copie a chave, cole no campo \"Chave do SteamGridDB\" e clique em Salvar." + Environment.NewLine + Environment.NewLine +
                "Não compartilhe a chave: ela fica só no config.json deste HD." + Environment.NewLine + Environment.NewLine +
                "Abrir a página do SteamGridDB no navegador agora?";

            var resposta = MessageBox.Show(this, passos, "Chave do SteamGridDB",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (resposta != DialogResult.Yes) return;

            try
            {
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                       {
                           FileName = Capas.SteamGridDbProvider.PaginaDaChave,
                           UseShellExecute = true
                       }))
                {
                }
            }
            catch (Exception)
            {
                // PC sem navegador padrão (ou com a associação quebrada): o endereço fica
                // à mostra para copiar à mão, em vez de a ajuda simplesmente não fazer nada.
                MessageBox.Show(this,
                    "Não consegui abrir o navegador. O endereço é:" + Environment.NewLine + Environment.NewLine +
                    Capas.SteamGridDbProvider.PaginaDaChave,
                    "Chave do SteamGridDB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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

        /// <summary>
        /// Tema e cor de acento (fase 17), tamanho do card, a seção "Continuar jogando"
        /// (fase 13) e o caminho para as estatísticas — tudo que muda o que eu vejo.
        ///
        /// As linhas são todas <c>Dock.Bottom</c>, e nesse arranjo a PRIMEIRA acrescentada
        /// é a que fica mais embaixo. Daí a ordem de baixo para cima aqui: o que eu mexo
        /// uma vez na vida (tema) acaba em cima, perto do título.
        /// </summary>
        private Control BlocoDaGrade()
        {
            var bloco = new Panel { Dock = DockStyle.Bottom, Height = 168 };

            var estatisticas = Botoes.Criar("Estatísticas do acervo (F9)", Point.Empty, 210);
            estatisticas.Dock = DockStyle.Left;
            estatisticas.Click += (_, _) =>
            {
                PediuEstatisticas = true;
                Close();
            };

            _botaoDasEstatisticas = estatisticas;

            var integridade = Botoes.Criar("Integridade do acervo (F8)", new Point(218, 0), 200);
            integridade.Click += (_, _) =>
            {
                PediuIntegridade = true;
                Close();
            };

            _botaoDaIntegridade = integridade;

            var linhaDoBotao = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 4, 0, 0) };
            linhaDoBotao.Controls.Add(integridade);
            linhaDoBotao.Controls.Add(estatisticas);

            var linhaDaSecao = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            linhaDaSecao.Controls.Add(_continuarJogando);

            var linhaDoTamanho = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            linhaDoTamanho.Controls.Add(_tamanho);

            var linhaDoAcento = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 2, 0, 2) };
            linhaDoAcento.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Tema.TextoFraco,
                Text = "   Cor de destaque: #RRGGBB, ou vazio para a do tema."
            });
            linhaDoAcento.Controls.Add(_amostraDoAcento);
            linhaDoAcento.Controls.Add(new Label { Dock = DockStyle.Left, Width = 8 });
            linhaDoAcento.Controls.Add(_acento);

            var linhaDoTema = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            linhaDoTema.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Tema.TextoFraco,
                Text = "   Vale a partir da próxima abertura."
            });
            linhaDoTema.Controls.Add(_tema);

            // Mesma regra invertida do MontarCorpo: entre irmãos ancorados embaixo, quem
            // entra por último fica MAIS EMBAIXO. Daí a lista aqui estar na ordem em que
            // as linhas aparecem na tela, de cima para baixo.
            bloco.Controls.Add(linhaDoTema);
            bloco.Controls.Add(linhaDoAcento);
            bloco.Controls.Add(linhaDoTamanho);
            bloco.Controls.Add(linhaDaSecao);
            bloco.Controls.Add(linhaDoBotao);
            bloco.Controls.Add(Titulo("Aparência e grade"));

            return bloco;
        }

        /// <summary>
        /// O quadradinho ao lado do campo, com a cor que está escrita nele.
        ///
        /// É o que separa "escrevi um hexadecimal certo" de "escrevi um hexadecimal que dá
        /// a cor que eu queria" — e evita descobrir a diferença só depois de reabrir o
        /// launcher. Texto que não vira cor deixa a amostra com a cor atual do acento e uma
        /// borda vermelha.
        /// </summary>
        private void AtualizarAmostra()
        {
            var texto = _acento.Text.Trim();

            if (texto.Length == 0)
            {
                _amostraDoAcento.BackColor = Tema.Acento;
                _amostraDoAcento.ForeColor = Tema.Borda;
                return;
            }

            var valida = Tema.TentarLerCor(texto, out var cor);

            _amostraDoAcento.BackColor = valida ? cor : Tema.Acento;
            _amostraDoAcento.ForeColor = valida ? Tema.Borda : Tema.Erro;
            _amostraDoAcento.Invalidate();
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

        /// <summary>
        /// O DirectX portátil: baixa o pacote oficial da Microsoft para o HD, uma vez só, e
        /// daí em diante todo jogo antigo acha as DLLs em qualquer PC, sem admin.
        /// </summary>
        private Control BlocoDoRuntime()
        {
            var bloco = new Panel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(0, 8, 0, 0) };

            var linha = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 0, 0, 6) };
            linha.Controls.Add(_removerRuntime);
            linha.Controls.Add(_prepararRuntime);

            bloco.Controls.Add(_situacaoDoRuntime);
            bloco.Controls.Add(linha);
            bloco.Controls.Add(Titulo("Runtime DirectX para jogos antigos"));

            return bloco;
        }

        private static Label Titulo(string texto) => new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = texto,
            ForeColor = Tema.Texto,
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
        /// Apaga _mochila\cache. É seguro: miniatura se refaz sozinha a partir da capa.
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

        private void AtualizarSituacaoDoRuntime()
        {
            var pronto = Execucao.RuntimeDirectX.EstaPronto;

            _situacaoDoRuntime.Text = Execucao.RuntimeDirectX.Descrever();
            _prepararRuntime.Text = pronto ? "Baixar de novo..." : "Baixar da Microsoft...";
            _removerRuntime.Visible = pronto;
        }

        private void PrepararRuntime()
        {
            var explicacao =
                "O Mochila vai baixar o pacote oficial \"DirectX End-User Runtimes (June 2010)\" " +
                "direto do site da Microsoft (cerca de 96 MB) e guardar as bibliotecas neste HD, " +
                @"em _mochila\runtime\directx (cerca de 225 MB)." + Environment.NewLine + Environment.NewLine +
                "Depois disso, os jogos antigos abertos pelo Mochila acham o D3DX9, o XInput e o " +
                "áudio do DirectX em qualquer PC, sem instalar nada e sem pedir administrador. " +
                "Num PC que já tem o DirectX instalado, o Windows continua usando o dele." + Environment.NewLine + Environment.NewLine +
                "Baixar agora?";

            if (MessageBox.Show(this, explicacao, "Runtime DirectX",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            using (var preparo = new FormPreparoDoRuntime())
            {
                preparo.ShowDialog(this);

                AtualizarSituacaoDoRuntime();

                if (preparo.Falha != null)
                {
                    MessageBox.Show(this, preparo.Falha, "Runtime DirectX",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (!preparo.Cancelado)
                {
                    MessageBox.Show(this,
                        "Pronto. Os jogos abertos pelo Mochila já usam o runtime deste HD.",
                        "Runtime DirectX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void RemoverRuntime()
        {
            if (MessageBox.Show(this,
                    "Apagar o runtime DirectX deste HD? Dá para baixar de novo quando quiser.",
                    "Runtime DirectX", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                Execucao.InstaladorDoRuntimeDirectX.Remover();
            }
            catch (Exception erro)
            {
                MessageBox.Show(this,
                    $"Não consegui apagar tudo ({erro.Message}). Se um jogo estiver aberto, feche e tente de novo.",
                    "Runtime DirectX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            AtualizarSituacaoDoRuntime();
        }

        private static string Formatar(long bytes)
            => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB" : $"{bytes / 1024.0:F0} KB";

        private void Salvar()
        {
            var acento = _acento.Text.Trim();

            // Cor sem sentido é recusada AQUI, com o campo na frente, em vez de gravada
            // para virar um recado no rodapé na próxima abertura. O launcher tolera lixo
            // no arquivo (ver UI.Tema.Aplicar) porque ele pode ter sido editado à mão —
            // mas não é motivo para ele mesmo escrever lixo.
            if (acento.Length > 0 && !Tema.TentarLerCor(acento, out _))
            {
                MessageBox.Show(this,
                    $"Não entendi a cor \"{acento}\".{Environment.NewLine}{Environment.NewLine}" +
                    "Escreva em hexadecimal (#A8FF3E) ou deixe o campo vazio para usar a cor do tema.",
                    "Cor de destaque", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                DialogResult = DialogResult.None;
                _acento.Focus();
                return;
            }

            var temaEscolhido = _tema.SelectedIndex == 1 ? TemaDoLauncher.Claro : TemaDoLauncher.Escuro;

            TemaMudou = _config.Tema != temaEscolhido ||
                        !string.Equals(_config.CorDeAcento.Trim(), acento, StringComparison.OrdinalIgnoreCase);

            _config.Tema = temaEscolhido;
            _config.CorDeAcento = acento;

            _config.SteamGridDbApiKey = _chave.Text.Trim();
            _config.TamanhoCard = (TamanhoCard)Math.Max(0, _tamanho.SelectedIndex);
            _config.MostrarContinuarJogando = _continuarJogando.Checked;

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
