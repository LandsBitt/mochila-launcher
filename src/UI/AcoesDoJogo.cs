using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Mochila.Capas;
using Mochila.Dados;
using Mochila.Execucao;
using Mochila.Modelo;
using Mochila.Scanner;

namespace Mochila.UI
{
    /// <summary>
    /// O que a janela precisa oferecer para as ações funcionarem. Existe para
    /// <see cref="AcoesDoJogo"/> não guardar uma referência ao <c>FormPrincipal</c>
    /// inteiro — a ação precisa de meia dúzia de coisas, não da janela toda.
    /// </summary>
    public interface IContextoDoLauncher
    {
        /// <summary>Dono dos diálogos. Sem isso, caixa de mensagem aparece atrás da janela.</summary>
        Form Janela { get; }

        Biblioteca Biblioteca { get; }

        Config Config { get; }

        /// <summary>
        /// O histórico de sessões (fase 13), carregado do disco na PRIMEIRA vez que alguém
        /// pergunta — nunca na abertura do launcher. É por isso que isto é método e não
        /// propriedade lida no construtor: o tempo de abertura da grade não pode pagar por
        /// cinco anos de sessões que só a tela de detalhes vai olhar.
        /// </summary>
        HistoricoDeSessoes Sessoes();

        Jogo? JogoSelecionado { get; }

        /// <summary>true enquanto um jogo está aberto (inclui a busca pelo processo-filho).</summary>
        bool JogoRodando { get; }

        void SalvarBiblioteca();

        /// <summary>Recado curto no rodapé. Nunca é caixa de diálogo.</summary>
        void Avisar(string texto);

        void RedesenharGrade();

        /// <summary>
        /// Refaz a lista visível a partir da biblioteca. Diferente de redesenhar: usa-se
        /// quando um jogo entrou ou saiu, não quando só a capa mudou.
        /// </summary>
        void ReaplicarFiltros();
    }

    /// <summary>
    /// Tudo que é ação sobre um jogo: capa, lançamento, executável sumido.
    ///
    /// Saiu do <c>FormPrincipal</c> na fase 9 por um motivo prático: a janela já estava em
    /// 1300 linhas e, por padrão, toda feature nova cairia lá dentro. Aqui o
    /// <c>FormPrincipal</c> fica com layout, eventos de entrada e orquestração; a regra de
    /// cada ação mora neste arquivo.
    /// </summary>
    public sealed class AcoesDoJogo
    {
        /// <summary>Extensões que valem como capa, no arrastar e no colar.</summary>
        public static readonly string[] ExtensoesDeImagem =
            { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".ico" };

        private readonly IContextoDoLauncher _contexto;
        private readonly GerenciadorDeCapas _capas;
        private readonly CacheDeMiniaturas _miniaturas;
        private readonly LancadorDeJogos _lancador;

        public AcoesDoJogo(IContextoDoLauncher contexto, GerenciadorDeCapas capas,
                           CacheDeMiniaturas miniaturas, LancadorDeJogos lancador)
        {
            _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
            _capas = capas ?? throw new ArgumentNullException(nameof(capas));
            _miniaturas = miniaturas ?? throw new ArgumentNullException(nameof(miniaturas));
            _lancador = lancador ?? throw new ArgumentNullException(nameof(lancador));
        }

        private Form Janela => _contexto.Janela;

        // ---- Lançar o jogo ---------------------------------------------------------------------

        /// <summary>
        /// Salva a biblioteca e abre o jogo. Devolve true quando o processo subiu — e só
        /// aí a janela sai da frente.
        ///
        /// A ordem importa e é da spec: a biblioteca vai para o disco ANTES do jogo abrir,
        /// porque se o jogo travar o PC eu não posso perder o que já estava catalogado.
        /// Esconder a janela continua sendo assunto de quem tem janela.
        /// </summary>
        public bool Lancar(Jogo jogo)
        {
            // Trava contra dois Enter seguidos. Vale para o card e para a biblioteca
            // inteira: dois jogos antigos ao mesmo tempo num notebook fraco também não é
            // o que eu quero, e enquanto um roda a janela nem está visível.
            if (_contexto.JogoRodando)
            {
                var emExecucao = _lancador.JogoAtual;
                _contexto.Avisar(emExecucao is not null && emExecucao.Id == jogo.Id
                    ? $"\"{jogo.Titulo}\" já está aberto."
                    : $"\"{emExecucao?.Titulo}\" ainda está aberto — feche antes de abrir outro.");
                return false;
            }

            _contexto.SalvarBiblioteca();

            try
            {
                _lancador.Lancar(jogo);
                return true;
            }
            catch (ExecutavelIndisponivelException ex)
            {
                OferecerLocalizarExecutavel(jogo, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Janela, $"Não consegui abrir \"{jogo.Titulo}\": {ex.Message}",
                    "Jogar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Executável sumido (HD reorganizado, pasta renomeada): em vez de crashar, deixa
        /// eu apontar onde ele foi parar. A correção manual vira
        /// <see cref="Jogo.ExecutavelFixadoPeloUsuario"/>, e rescan nenhum a desfaz.
        /// </summary>
        public void OferecerLocalizarExecutavel(Jogo jogo, string motivo)
        {
            var resposta = MessageBox.Show(Janela,
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
                if (dialogo.ShowDialog(Janela) != DialogResult.OK) return;

                if (!Caminhos.TentarParaRelativo(dialogo.FileName, out var relativo))
                {
                    MessageBox.Show(Janela,
                        $"\"{dialogo.FileName}\" está fora da pasta do launcher.{Environment.NewLine}" +
                        "Um caminho de fora viraria letra de drive na biblioteca e quebraria em outro PC.",
                        "Executável não encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                jogo.ExecutavelRelativo = relativo;
                jogo.ExecutavelFixadoPeloUsuario = true;
                _contexto.SalvarBiblioteca();

                _contexto.RedesenharGrade();
                _contexto.Avisar($"Executável de \"{jogo.Titulo}\" atualizado.");
            }
        }

        /// <summary>Abre o diálogo na pasta do jogo se ela existir; senão, na raiz do HD.</summary>
        private static string PastaInicialPara(Jogo jogo)
        {
            var pasta = jogo.PastaDoJogo();
            return !string.IsNullOrEmpty(pasta) && Directory.Exists(pasta) ? pasta! : Caminhos.PastaBase;
        }

        // ---- Ações do card (fase 10) -----------------------------------------------------------

        /// <summary>Abre o Explorer já com o executável selecionado dentro da pasta.</summary>
        public void AbrirPastaDoJogo(Jogo jogo)
        {
            var exe = jogo.CaminhoExecutavel();

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                // Sem o exe ainda dá para abrir a pasta, se ela existir.
                var pasta = jogo.PastaDoJogo();
                if (!string.IsNullOrEmpty(pasta) && Directory.Exists(pasta))
                {
                    Abrir("explorer.exe", $"\"{pasta}\"");
                    return;
                }

                _contexto.Avisar($"A pasta de \"{jogo.Titulo}\" não está acessível agora.");
                return;
            }

            Abrir("explorer.exe", $"/select,\"{exe}\"");
        }

        private void Abrir(string programa, string argumentos)
        {
            try
            {
                using (var processo = System.Diagnostics.Process.Start(
                           new System.Diagnostics.ProcessStartInfo(programa, argumentos) { UseShellExecute = true }))
                {
                    // O Explorer não é acompanhado: só abre e some do nosso radar.
                    processo?.Dispose();
                }
            }
            catch (Exception erro)
            {
                _contexto.Avisar($"Não consegui abrir o Explorer: {erro.Message}");
            }
        }

        /// <summary>Título e argumentos, numa janelinha. Título vazio é recusado lá dentro.</summary>
        public void EditarJogo(Jogo jogo)
        {
            using (var janela = new FormEditarJogo(jogo))
            {
                if (janela.ShowDialog(Janela) != DialogResult.OK) return;

                var mudouTitulo = !string.Equals(jogo.Titulo, janela.TituloEscolhido, StringComparison.Ordinal);

                jogo.Titulo = janela.TituloEscolhido;
                jogo.Argumentos = janela.ArgumentosEscolhidos;

                _contexto.SalvarBiblioteca();

                // O id NÃO muda junto com o título: ele nomeia a capa e a miniatura em
                // disco, e é a chave do histórico. Renomear um jogo não pode custar a arte
                // dele nem o tempo jogado.
                if (mudouTitulo) _contexto.ReaplicarFiltros();
                else _contexto.RedesenharGrade();

                _contexto.Avisar($"\"{jogo.Titulo}\" atualizado.");
            }
        }

        /// <summary>
        /// Tira o jogo da biblioteca. **Nunca** encosta no jogo em disco — só a entrada e
        /// a arte dentro de <c>_mochila\</c>. A confirmação diz isso com todas as letras,
        /// porque "remover" numa lista de jogos assusta com razão.
        /// </summary>
        public void RemoverDaBiblioteca(Jogo jogo)
        {
            var resposta = MessageBox.Show(Janela,
                $"Tirar \"{jogo.Titulo}\" da biblioteca?{Environment.NewLine}{Environment.NewLine}" +
                "Some o card, a capa e o tempo jogado. " +
                "O JOGO EM DISCO NÃO É TOCADO — nenhum arquivo dele é apagado, e um novo " +
                "scan (F6) o encontra de volta.",
                "Remover da biblioteca", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (resposta != DialogResult.Yes) return;

            EsquecerArte(jogo);

            _contexto.Biblioteca.Jogos.Remove(jogo);
            _contexto.SalvarBiblioteca();
            _contexto.ReaplicarFiltros();

            _contexto.Avisar($"\"{jogo.Titulo}\" saiu da biblioteca. O jogo continua no HD.");
        }

        /// <summary>
        /// Tranca o executável atual contra rescan. É a mesma marca que a janela de revisão
        /// põe, disponível de fora dela para quando eu já corrigi e não quero mais mexer.
        /// </summary>
        public void DefinirComoExecutavelPrincipal(Jogo jogo)
        {
            jogo.ExecutavelFixadoPeloUsuario = true;

            // Aproveita para gravar a impressão enquanto o caminho ainda bate: é ela que
            // vai reconhecer este jogo se a pasta mudar de lugar depois.
            if (ImpressaoDigital.De(jogo.CaminhoExecutavel()) is { } impressao) jogo.Impressao = impressao;

            _contexto.SalvarBiblioteca();
            _contexto.Avisar($"Executável de \"{jogo.Titulo}\" fixado — nenhum rescan vai trocá-lo.");
        }

        /// <summary>
        /// Atalho na área de trabalho. Uma das duas únicas escritas fora da pasta do
        /// launcher, e por isso passa por confirmação que explica o custo: o <c>.lnk</c>
        /// guarda caminho absoluto e quebra quando a letra do drive mudar.
        /// </summary>
        public void CriarAtalhoNaAreaDeTrabalho(Jogo jogo)
        {
            var exe = jogo.CaminhoExecutavel();

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                _contexto.Avisar($"Não dá para criar o atalho: o executável de \"{jogo.Titulo}\" não está acessível.");
                return;
            }

            var resposta = MessageBox.Show(Janela,
                $"Criar um atalho para \"{jogo.Titulo}\" na área de trabalho?" +
                Environment.NewLine + Environment.NewLine +
                "É a única coisa que o launcher grava fora da pasta dele. O atalho guarda o " +
                $"caminho completo ({exe}) e, por isso, PARA DE FUNCIONAR se o HD for plugado " +
                "com outra letra. Ele é descartável: apague e crie de novo quando isso acontecer.",
                "Criar atalho", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (resposta != DialogResult.Yes) return;

            try
            {
                var destino = AtalhosDoWindows.CriarNaAreaDeTrabalho(jogo.Titulo, exe!, jogo.Argumentos,
                                                                    jogo.PastaDoJogo());
                _contexto.Avisar($"Atalho criado: {Path.GetFileName(destino)}");
            }
            catch (Exception erro)
            {
                MessageBox.Show(Janela, $"Não consegui criar o atalho: {erro.Message}",
                    "Criar atalho", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---- Adicionar jogo manualmente (fase 10) ----------------------------------------------

        /// <summary>Extensões que valem como "isto é um jogo" num arrastar-e-soltar.</summary>
        public static readonly string[] ExtensoesDeExecutavel = { ".exe", ".lnk", ".bat", ".cmd" };

        public static bool EhExecutavel(string? caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return false;

            var extensao = Path.GetExtension(caminho).ToLowerInvariant();
            foreach (var aceita in ExtensoesDeExecutavel)
            {
                if (extensao == aceita) return true;
            }
            return false;
        }

        /// <summary>O primeiro executável de um arrastar-e-soltar, ou null.</summary>
        public static string? ExecutavelArrastado(IDataObject? dados)
        {
            if (dados?.GetData(DataFormats.FileDrop) is not string[] arquivos) return null;

            foreach (var arquivo in arquivos)
            {
                if (EhExecutavel(arquivo)) return arquivo;
            }
            return null;
        }

        /// <summary>A primeira pasta de um arrastar-e-soltar, ou null.</summary>
        public static string? PastaArrastada(IDataObject? dados)
        {
            if (dados?.GetData(DataFormats.FileDrop) is not string[] arquivos) return null;

            foreach (var arquivo in arquivos)
            {
                if (!string.IsNullOrEmpty(arquivo) && Directory.Exists(arquivo)) return arquivo;
            }
            return null;
        }

        /// <summary>
        /// Cataloga um executável que eu arrastei para a janela. Devolve o jogo criado, ou
        /// null quando não deu.
        ///
        /// Entra sempre com <c>executavelFixadoPeloUsuario</c>: fui eu que escolhi este
        /// arquivo, e rescan nenhum tem por que discordar depois.
        /// </summary>
        public Jogo? AdicionarDeArquivo(string caminho)
        {
            var alvo = caminho;

            // .lnk vira o alvo dele: gravar o atalho deixaria a biblioteca dependendo de um
            // arquivo que normalmente vive fora do HD.
            if (string.Equals(Path.GetExtension(caminho), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                alvo = AtalhosDoWindows.AlvoDe(caminho) ?? "";

                if (alvo.Length == 0)
                {
                    _contexto.Avisar("Não consegui descobrir para onde esse atalho aponta.");
                    return null;
                }
            }

            if (!File.Exists(alvo))
            {
                _contexto.Avisar($"\"{Path.GetFileName(alvo)}\" não existe mais.");
                return null;
            }

            if (!Caminhos.TentarParaRelativo(alvo, out var relativo))
            {
                MessageBox.Show(Janela,
                    $"\"{alvo}\" está em outro drive.{Environment.NewLine}{Environment.NewLine}" +
                    $"O launcher está em \"{Caminhos.PastaBase}\". Só dá para catalogar jogos do " +
                    "mesmo HD — um caminho de fora viraria letra fixa no JSON e quebraria no próximo PC.",
                    "Adicionar jogo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            if (_contexto.Biblioteca.ObterPorExecutavel(relativo) is { } jaExiste)
            {
                _contexto.Avisar($"\"{jaExiste.Titulo}\" já está na biblioteca.");
                return jaExiste;
            }

            // Título vem do nome da PASTA, limpo pela mesma rotina do scanner — "speed.exe"
            // seria um nome de card horrível, e a pasta quase sempre tem o nome do jogo.
            var pasta = Path.GetDirectoryName(alvo) ?? "";
            var titulo = TituloDePasta.Limpar(Path.GetFileName(pasta));
            if (string.IsNullOrWhiteSpace(titulo)) titulo = Path.GetFileNameWithoutExtension(alvo);

            var jogo = new Jogo
            {
                Id = _contexto.Biblioteca.GerarId(titulo),
                Titulo = titulo,
                ExecutavelRelativo = relativo,
                Impressao = ImpressaoDigital.De(alvo),
                ExecutavelFixadoPeloUsuario = true
            };

            _contexto.Biblioteca.Jogos.Add(jogo);
            _contexto.SalvarBiblioteca();
            _contexto.ReaplicarFiltros();

            _contexto.Avisar($"\"{jogo.Titulo}\" adicionado. Botão direito no card para trocar a capa.");
            return jogo;
        }

        // ---- Capas -----------------------------------------------------------------------------

        /// <summary>O primeiro arquivo de imagem de um arrastar-e-soltar, ou null.</summary>
        public static string? ArquivoDeImagemArrastado(IDataObject? dados)
        {
            if (dados?.GetData(DataFormats.FileDrop) is not string[] arquivos) return null;

            foreach (var arquivo in arquivos)
            {
                if (EhImagem(arquivo)) return arquivo;
            }
            return null;
        }

        public static bool EhImagem(string? caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return false;

            var extensao = Path.GetExtension(caminho).ToLowerInvariant();
            foreach (var aceita in ExtensoesDeImagem)
            {
                if (extensao == aceita) return true;
            }
            return false;
        }

        public void AplicarCapaDeArquivo(Jogo jogo, string caminho)
        {
            try
            {
                _capas.AplicarDeArquivo(jogo, caminho);
                _contexto.SalvarBiblioteca();
                _contexto.RedesenharGrade();
                _contexto.Avisar($"Capa de \"{jogo.Titulo}\" atualizada.");
            }
            catch (Exception erro)
            {
                MessageBox.Show(Janela, erro.Message, "Capa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void EscolherCapaDeArquivo()
        {
            if (_contexto.JogoSelecionado is not { } jogo) return;

            using (var dialogo = new OpenFileDialog
                   {
                       Title = $"Capa de {jogo.Titulo}",
                       Filter = "Imagens|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.ico|Todos os arquivos|*.*",
                       CheckFileExists = true
                   })
            {
                if (dialogo.ShowDialog(Janela) == DialogResult.OK) AplicarCapaDeArquivo(jogo, dialogo.FileName);
            }
        }

        public void ColarCapa()
        {
            if (_contexto.JogoSelecionado is not { } jogo) return;

            try
            {
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } imagem)
                {
                    using (imagem)
                    {
                        _capas.AplicarImagem(jogo, imagem);
                        _contexto.SalvarBiblioteca();
                        _contexto.RedesenharGrade();
                        _contexto.Avisar($"Capa de \"{jogo.Titulo}\" colada da área de transferência.");
                    }
                    return;
                }

                // Copiar um arquivo no Explorer também é "colar uma capa".
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (var arquivo in Clipboard.GetFileDropList())
                    {
                        if (!EhImagem(arquivo)) continue;

                        AplicarCapaDeArquivo(jogo, arquivo!);
                        return;
                    }
                }

                _contexto.Avisar("Não há imagem na área de transferência.");
            }
            catch (Exception erro)
            {
                MessageBox.Show(Janela, $"Não consegui usar o que está na área de transferência: {erro.Message}",
                    "Capa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Fallback da spec sob demanda: arte solta na pasta, senão ícone do exe.</summary>
        public void UsarArteLocal()
        {
            if (_contexto.JogoSelecionado is not { } jogo) return;

            var origem = _capas.AplicarFallbackLocal(jogo);

            if (origem == OrigemDaCapa.Nenhuma)
            {
                _contexto.Avisar($"Não achei arte na pasta de \"{jogo.Titulo}\" nem ícone no executável.");
                return;
            }

            _contexto.SalvarBiblioteca();
            _contexto.RedesenharGrade();

            _contexto.Avisar(origem == OrigemDaCapa.PastaDoJogo
                ? $"Capa de \"{jogo.Titulo}\" veio de um arquivo da pasta do jogo."
                : $"Capa de \"{jogo.Titulo}\" veio do ícone do executável.");
        }

        public void RemoverCapa()
        {
            if (_contexto.JogoSelecionado is not { } jogo) return;

            ApagarArquivosDaCapa(jogo);

            jogo.CapaArquivo = null;
            _miniaturas.Invalidar(jogo.Id);

            _contexto.SalvarBiblioteca();
            _contexto.RedesenharGrade();
            _contexto.Avisar($"Capa de \"{jogo.Titulo}\" removida — o card volta a ser desenhado.");
        }

        /// <summary>
        /// Esquece a arte de um jogo: apaga a capa e a miniatura dentro de
        /// <c>_mochila\</c> e tira a imagem da memória. Público porque a remoção em lote
        /// (fase 12) precisa exatamente disto, e duas cópias da regra de onde a arte mora
        /// é como uma delas fica para trás.
        /// </summary>
        public void EsquecerArte(Jogo jogo)
        {
            ApagarArquivosDaCapa(jogo);
            _miniaturas.Invalidar(jogo.Id);
        }

        /// <summary>
        /// Apaga capa e miniatura do jogo dentro de <c>_mochila\</c>. Nunca toca em nada
        /// fora dali — o arquivo do jogo em disco não é assunto do launcher.
        /// </summary>
        private void ApagarArquivosDaCapa(Jogo jogo)
        {
            try
            {
                var capa = jogo.CaminhoCapa();
                if (capa != null && File.Exists(capa)) File.Delete(capa);

                var thumb = jogo.CaminhoThumbnail();
                if (File.Exists(thumb)) File.Delete(thumb);
            }
            catch (Exception)
            {
                // Arquivo preso: o importante é a biblioteca deixar de apontar para ele.
            }
        }

        public void BuscarCapaOnline()
        {
            if (_contexto.JogoSelecionado is not { } jogo) return;
            if (!ExigirChave()) return;

            using (var provedor = new SteamGridDbProvider(_contexto.Config.SteamGridDbApiKey))
            using (var janela = new FormBuscaDeCapa(jogo, provedor))
            {
                if (janela.ShowDialog(Janela) != DialogResult.OK || janela.Escolhida is null) return;

                try
                {
                    using (var memoria = new MemoryStream(janela.Escolhida.Bytes))
                    using (var imagem = Image.FromStream(memoria))
                    {
                        _capas.AplicarImagem(jogo, imagem);
                    }

                    jogo.SteamGridDbId = janela.IdEscolhido;
                    _contexto.SalvarBiblioteca();
                    _contexto.RedesenharGrade();

                    _contexto.Avisar($"Capa de \"{jogo.Titulo}\" baixada.");
                }
                catch (Exception erro)
                {
                    MessageBox.Show(Janela, $"Baixou, mas não consegui gravar: {erro.Message}",
                        "Capa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// Lote das capas que faltam. Nunca com jogo aberto: a spec é explícita, e roubar
        /// banda e CPU no meio de uma partida é o oposto do que este launcher promete.
        /// </summary>
        public void BaixarCapasEmLote()
        {
            if (_contexto.JogoRodando)
            {
                _contexto.Avisar("Tem jogo aberto — o lote de capas fica para depois.");
                return;
            }

            if (!ExigirChave()) return;

            var faltando = new List<Jogo>();
            foreach (var jogo in _contexto.Biblioteca.Jogos)
            {
                if (string.IsNullOrEmpty(jogo.CapaArquivo) || !File.Exists(jogo.CaminhoCapa()!))
                    faltando.Add(jogo);
            }

            if (faltando.Count == 0)
            {
                _contexto.Avisar("Todos os jogos já têm capa.");
                return;
            }

            var pergunta = MessageBox.Show(Janela,
                $"Buscar capa para {faltando.Count} jogo(s) sem capa?{Environment.NewLine}{Environment.NewLine}" +
                "Vai um pedido a cada meio segundo, e dá para cancelar no meio.",
                "Baixar capas", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (pergunta != DialogResult.OK) return;

            using (var provedor = new SteamGridDbProvider(_contexto.Config.SteamGridDbApiKey))
            using (var janela = new FormCapasEmLote(faltando, provedor, _capas))
            {
                janela.ShowDialog(Janela);

                _contexto.SalvarBiblioteca();
                _contexto.RedesenharGrade();

                _contexto.Avisar(janela.ChaveRecusada
                    ? "A chave do SteamGridDB foi recusada — confira em Configurações."
                    : $"{janela.Baixadas} capa(s) baixada(s), {janela.SemCapa} sem capa no acervo, " +
                      $"{janela.ComFalha} com erro.");
            }
        }

        /// <summary>
        /// Chave vazia não é erro: é o caminho manual. Aviso curto e nenhuma tentativa de
        /// rede — a spec proíbe conexão que eu não pedi.
        /// </summary>
        public bool ExigirChave()
        {
            if (_contexto.Config.TemChaveSteamGridDb()) return true;

            _contexto.Avisar("Sem chave do SteamGridDB: use \"Escolher capa do arquivo...\" ou arraste uma imagem no card.");
            return false;
        }
    }
}
