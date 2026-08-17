// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Mochila.Dados;
using Mochila.Entrada;
using Mochila.Execucao;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 13: log de sessões, estatísticas e a seção
    /// "Continuar jogando".
    ///
    /// Três coisas erram calado nesta fase, e é por elas que os testes existem:
    ///
    /// <list type="number">
    /// <item><b>Fuso.</b> A data vai para o disco em UTC e toda pergunta é de calendário
    /// local. Uma sessão de 31/01 às 22h daqui é 01/02 em UTC — somá-la em fevereiro dá a
    /// resposta errada sem nenhum sintoma.</item>
    /// <item><b>Custo de abertura.</b> <c>sessoes.json</c> não pode ser lido no caminho de
    /// inicialização. Isso não aparece em tela nenhuma: aparece no tempo de abrir o
    /// launcher, dois anos de histórico depois.</item>
    /// <item><b>Card duplicado.</b> A seção "Continuar jogando" é uma reordenação da lista
    /// filtrada, nunca uma cópia dela. Card repetido brigaria com a marcação por id, com a
    /// seleção por id e com a contagem do rodapé.</item>
    /// </list>
    /// </summary>
    public static class AutoTesteSessoes
    {
        private const string NomePastaSandbox = "_autoteste-sessoes-tmp";

        /// <summary>Detecta qualquer letra de drive num texto. O JSON não pode ter isso.</summary>
        private static readonly Regex LetraDeDrive = new Regex(@"[A-Za-z]:[\\/]", RegexOptions.Compiled);

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                PrepararSandbox(raizReal);

                TestarGravacaoDaSessao(v);
                TestarRegraDosCincoSegundos(v);
                TestarArquivoCorrompido(v);
                TestarArquivoDoFuturo(v);
                TestarRegistrosQuebrados(v);
                TestarFusoHorario(v);
                TestarAgregacoes(v);
                TestarJogoOrfao(v);

                Caminhos.RestaurarPastaBase();

                TestarLayoutComSecoes(v);
                TestarContinuarJogandoNaJanela(v);
                TestarOpcaoNasConfiguracoes(v);
                TestarAberturaNaoLeOHistorico(v);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
                LimparSandbox(v, raizReal);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- O arquivo ---------------------------------------------------------------------------

        private static void TestarGravacaoDaSessao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("sessoes.json: gravar e reler");

            var historico = new HistoricoDeSessoes();
            var inicio = new DateTime(2026, 8, 14, 0, 4, 11, DateTimeKind.Utc);

            v.Verificar("registrar grava na hora", historico.Registrar(new Sessao("nfsmw-black", inicio, 3120)));
            v.Verificar("o arquivo foi criado", File.Exists(Caminhos.ArquivoSessoes));

            var conteudo = ArquivoTexto.Ler(Caminhos.ArquivoSessoes);

            v.Verificar("data gravada em UTC com Z", conteudo.Contains("\"2026-08-14T00:04:11Z\""), conteudo);
            v.Verificar("sem letra de drive no arquivo", !LetraDeDrive.IsMatch(conteudo));
            v.Verificar($"versão {HistoricoDeSessoes.VersaoAtual} no arquivo",
                conteudo.Contains($"\"versao\": {HistoricoDeSessoes.VersaoAtual}"));

            var lido = HistoricoDeSessoes.Carregar();
            v.Verificar("releu uma sessão", lido.Sessoes.Count == 1, lido.Sessoes.Count.ToString());
            v.Verificar("não veio marcado como corrompido", !lido.Corrompido);

            if (lido.Sessoes.Count == 1)
            {
                v.Verificar("jogoId preservado", lido.Sessoes[0].JogoId == "nfsmw-black");
                v.Verificar("segundos preservados", lido.Sessoes[0].Segundos == 3120);
                v.Verificar("data preservada, e ainda em UTC",
                    lido.Sessoes[0].InicioUtc == inicio && lido.Sessoes[0].InicioUtc.Kind == DateTimeKind.Utc,
                    lido.Sessoes[0].InicioUtc.ToString("o"));
            }

            // Segunda sessão: acrescenta, não substitui.
            lido.Registrar(new Sessao("nfsmw-black", inicio.AddDays(1), 600));
            v.Verificar("a segunda sessão soma no arquivo em vez de substituir",
                HistoricoDeSessoes.Carregar().Sessoes.Count == 2);
        }

        /// <summary>
        /// A regra dos 5 segundos, no caminho de produção: quem decide é o
        /// <see cref="ContabilizadorDeTempo"/>, e o histórico obedece. Testar a condição
        /// duplicada dentro do histórico provaria a cópia, não a regra.
        /// </summary>
        private static void TestarRegraDosCincoSegundos(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Sessão curta (o caso do processo-filho)");

            var agora = DateTime.UtcNow;
            var jogo = new Jogo { Id = "curto", Titulo = "Curto", ExecutavelRelativo = @"Jogos\c\c.exe" };

            var quatro = ContabilizadorDeTempo.Contabilizar(jogo, TimeSpan.FromSeconds(4), agora);
            v.Verificar("sessão de 4 s não vai para o histórico",
                ContabilizadorDeTempo.ParaHistorico(jogo, agora, quatro) is null);

            var seis = ContabilizadorDeTempo.Contabilizar(jogo, TimeSpan.FromSeconds(6), agora);
            var registro = ContabilizadorDeTempo.ParaHistorico(jogo, agora, seis);

            v.Verificar("sessão de 6 s vai", registro is not null);
            v.Verificar("e vai com os segundos que a biblioteca creditou",
                registro?.Segundos == 6, registro?.Segundos.ToString());

            v.Verificar("sem resultado (biblioteca somente leitura) também não grava nada",
                ContabilizadorDeTempo.ParaHistorico(jogo, agora, null) is null);

            v.Verificar("e o jogo de 4 s não ganhou tempo nenhum na biblioteca",
                jogo.SegundosJogados == 6, jogo.SegundosJogados.ToString());
        }

        private static void TestarArquivoCorrompido(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Arquivo corrompido não trava nada");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "sessoes-quebrado.json");
            ArquivoTexto.EscreverAtomico(arquivo, "{ isto não é json, [[[");

            var historico = HistoricoDeSessoes.Carregar(arquivo);

            v.Verificar("carregar não lança e devolve histórico vazio", historico.Sessoes.Count == 0);
            v.Verificar("marcado como corrompido (é o aviso discreto da barra)", historico.Corrompido);

            // A gravação seguinte põe o ilegível de lado em vez de passar por cima: pode ter
            // cinco anos de histórico recuperável à mão lá dentro.
            historico.Registrar(new Sessao("jogo", DateTime.UtcNow, 100));

            // O nome tem que sair legível: o padrão de busca é literalmente
            // "corrompido-<data>", e é o que eu vou procurar no HD para recuperar o arquivo.
            var postoDeLado = Directory.GetFiles(Caminhos.PastaEstado, "sessoes-quebrado.json.corrompido-*");
            v.Verificar("o arquivo ilegível foi guardado com nome datado E legível",
                postoDeLado.Length == 1,
                string.Join(" | ", Directory.GetFiles(Caminhos.PastaEstado)));
            v.Verificar("e o novo tem a sessão nova",
                HistoricoDeSessoes.Carregar(arquivo).Sessoes.Count == 1);

            // Arquivo ausente é o caso normal do primeiro uso, e não é corrupção.
            var inexistente = HistoricoDeSessoes.Carregar(Path.Combine(Caminhos.PastaEstado, "nao-existe.json"));
            v.Verificar("arquivo ausente abre vazio e SEM aviso de corrupção",
                inexistente.Sessoes.Count == 0 && !inexistente.Corrompido);
        }

        private static void TestarArquivoDoFuturo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Histórico de versão mais nova que o binário");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "sessoes-v99.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 99,
  ""sessoes"": [ { ""jogoId"": ""x"", ""inicio"": ""2026-01-01T10:00:00Z"", ""segundos"": 60 } ]
}");

            var historico = HistoricoDeSessoes.Carregar(arquivo);
            var antes = ArquivoTexto.Ler(arquivo);

            v.Verificar("abre para leitura", historico.Sessoes.Count == 1);
            v.Verificar("marcado como somente leitura", historico.SomenteLeitura);

            v.Verificar("registrar devolve false", !historico.Registrar(new Sessao("y", DateTime.UtcNow, 90)));
            v.Verificar("e o arquivo do futuro NÃO foi reescrito",
                ArquivoTexto.Ler(arquivo) == antes);
        }

        private static void TestarRegistrosQuebrados(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Registro quebrado no meio do arquivo");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "sessoes-meio-quebrado.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 1,
  ""sessoes"": [
    { ""jogoId"": ""bom"", ""inicio"": ""2026-03-02T18:00:00Z"", ""segundos"": 1800 },
    { ""jogoId"": """", ""inicio"": ""2026-03-02T19:00:00Z"", ""segundos"": 1800 },
    { ""jogoId"": ""sem-data"", ""segundos"": 1800 },
    { ""jogoId"": ""outro-bom"", ""inicio"": ""2026-03-03T18:00:00Z"", ""segundos"": 60 }
  ]
}");

            var historico = HistoricoDeSessoes.Carregar(arquivo);

            v.Verificar("linha sem jogo e linha sem data são descartadas, o resto entra",
                historico.Sessoes.Count == 2, historico.Sessoes.Count.ToString());
            v.Verificar("e o arquivo não é dado por corrompido por causa delas", !historico.Corrompido);
        }

        // ---- Fuso horário -----------------------------------------------------------------------

        /// <summary>
        /// O teste que a spec pede com nome e sobrenome: sessão de 31/01 às 22h LOCAL não
        /// pode aparecer em fevereiro. Montada a partir da hora local justamente para valer
        /// em qualquer fuso onde eu rode isto.
        /// </summary>
        private static void TestarFusoHorario(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Agregação em hora local (o erro que não dá sintoma)");

            var jogo = new Jogo { Id = "fim-do-mes", Titulo = "Fim do mês", ExecutavelRelativo = @"Jogos\f\f.exe" };
            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(jogo);

            var local = new DateTime(2026, 1, 31, 22, 0, 0, DateTimeKind.Local);

            var historico = new HistoricoDeSessoes();
            historico.Sessoes.Add(new Sessao(jogo.Id, local.ToUniversalTime(), 3600));

            var estatisticas = new EstatisticasDeSessoes(historico, biblioteca);
            var meses = estatisticas.SegundosPorMes(2026);

            v.Verificar("a sessão de 31/01 22h local conta em JANEIRO",
                meses[0] == 3600, meses[0].ToString());
            v.Verificar("e NÃO conta em fevereiro", meses[1] == 0, meses[1].ToString());

            v.Verificar("o total do mês do jogo também é de janeiro",
                estatisticas.SegundosDoJogoNoMes(jogo.Id, local) == 3600);
            v.Verificar("e zero em fevereiro",
                estatisticas.SegundosDoJogoNoMes(jogo.Id, local.AddDays(1)) == 0);

            // O mapa de calor usa o mesmo dia local.
            var dias = estatisticas.SegundosPorDia(2026);
            v.Verificar("o mapa de calor marca o dia 31/01",
                dias.ContainsKey(new DateTime(2026, 1, 31)), string.Join(", ", DescreverDias(dias)));

            // E a volta: a data no disco continua em UTC, não em local.
            var arquivo = Path.Combine(Caminhos.PastaEstado, "sessoes-fuso.json");
            historico.Salvar(arquivo);
            var conteudo = ArquivoTexto.Ler(arquivo);
            v.Verificar("no disco a data continua em UTC (termina em Z)",
                conteudo.Contains(Json.FormatarData(local.ToUniversalTime())!), conteudo);
        }

        private static List<string> DescreverDias(Dictionary<DateTime, int> dias)
        {
            var lista = new List<string>();
            foreach (var par in dias) lista.Add($"{par.Key:dd/MM}={par.Value}");
            return lista;
        }

        // ---- Agregações -------------------------------------------------------------------------

        private static void TestarAgregacoes(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Últimas sessões, semana e top 10");

            var biblioteca = new Biblioteca();
            var muito = new Jogo { Id = "muito", Titulo = "Muito jogado", ExecutavelRelativo = @"Jogos\m\m.exe" };
            var pouco = new Jogo { Id = "pouco", Titulo = "Pouco jogado", ExecutavelRelativo = @"Jogos\p\p.exe" };
            biblioteca.Jogos.Add(muito);
            biblioteca.Jogos.Add(pouco);

            var hoje = DateTime.Now.Date.AddHours(12);
            var historico = new HistoricoDeSessoes();

            // 12 sessões do "muito", em dias distintos, para o corte em 10 aparecer.
            for (var i = 0; i < 12; i++)
                historico.Sessoes.Add(new Sessao(muito.Id, hoje.AddDays(-i).ToUniversalTime(), 600 + i));

            historico.Sessoes.Add(new Sessao(pouco.Id, hoje.AddDays(-40).ToUniversalTime(), 300));

            var estatisticas = new EstatisticasDeSessoes(historico, biblioteca);

            var ultimas = estatisticas.UltimasDoJogo(muito.Id, 10);
            v.Verificar("as últimas sessões param em 10", ultimas.Count == 10, ultimas.Count.ToString());
            v.Verificar("e vêm da mais recente para a mais antiga",
                ultimas[0].InicioUtc > ultimas[9].InicioUtc);
            v.Verificar("a primeira é a de hoje",
                ultimas[0].InicioLocal.Date == DateTime.Now.Date, ultimas[0].InicioLocal.ToString("dd/MM"));

            v.Verificar("as sessões do outro jogo não entram na lista dele",
                estatisticas.UltimasDoJogo(pouco.Id, 10).Count == 1);

            // "Esta semana": a sessão de 40 dias atrás nunca pode entrar; a de hoje sempre entra.
            var semana = estatisticas.SegundosNaSemana(DateTime.Now);
            v.Verificar("a sessão de hoje entra em 'esta semana'", semana >= 600, semana.ToString());
            v.Verificar("a de 40 dias atrás não entra", semana < 300 + (12 * 700), semana.ToString());

            var inicio = EstatisticasDeSessoes.InicioDaSemana(DateTime.Now);
            v.Verificar("a semana começa no primeiro dia de semana da cultura do sistema",
                inicio.DayOfWeek == System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
                inicio.DayOfWeek.ToString());
            v.Verificar("e nunca começa no futuro", inicio <= DateTime.Now.Date);

            var top = estatisticas.Top(10);
            v.Verificar("o top traz os dois jogos", top.Count == 2, top.Count.ToString());
            v.Verificar("o mais jogado vem primeiro", top[0].Jogo.Id == "muito", top[0].Jogo.Id);
            v.Verificar("com o tempo somado das sessões dele",
                top[0].Segundos == SomaEsperada(12), top[0].Segundos.ToString());
            v.Verificar("e a contagem de sessões", top[0].Sessoes == 12, top[0].Sessoes.ToString());

            v.Verificar("o total geral é a soma de tudo que tem jogo na biblioteca",
                estatisticas.SegundosTotais == SomaEsperada(12) + 300, estatisticas.SegundosTotais.ToString());

            v.Verificar("os anos com histórico são listados do mais novo para o mais velho",
                estatisticas.AnosComHistorico().Count >= 1);
        }

        private static int SomaEsperada(int quantas)
        {
            var total = 0;
            for (var i = 0; i < quantas; i++) total += 600 + i;
            return total;
        }

        /// <summary>
        /// <c>jogoId</c> órfão: ignorado na leitura, <b>nunca</b> apagado. Reaproveitar slug
        /// é intencional no projeto — o jogo pode voltar com o mesmo id, e volta com o
        /// histórico dele.
        /// </summary>
        private static void TestarJogoOrfao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Sessão de jogo que não está mais na biblioteca");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "sessoes-orfa.json");

            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(new Jogo { Id = "existe", Titulo = "Existe", ExecutavelRelativo = @"Jogos\e\e.exe" });

            var historico = new HistoricoDeSessoes();
            historico.Sessoes.Add(new Sessao("existe", DateTime.UtcNow.AddDays(-1), 1200));
            historico.Sessoes.Add(new Sessao("sumiu", DateTime.UtcNow.AddDays(-2), 9999));
            historico.Salvar(arquivo);

            var estatisticas = new EstatisticasDeSessoes(HistoricoDeSessoes.Carregar(arquivo), biblioteca);

            v.Verificar("a órfã é ignorada nas contas", estatisticas.Quantidade == 1, estatisticas.Quantidade.ToString());
            v.Verificar("e contada como órfã, para a tela poder dizer isso", estatisticas.Orfas == 1);
            v.Verificar("ela não aparece no top", estatisticas.Top(10).Count == 1);
            v.Verificar("nem no total", estatisticas.SegundosTotais == 1200, estatisticas.SegundosTotais.ToString());

            // O que importa: gravar de novo NÃO apaga a órfã do arquivo.
            var relido = HistoricoDeSessoes.Carregar(arquivo);
            relido.Registrar(new Sessao("existe", DateTime.UtcNow, 60));
            relido.Salvar(arquivo);

            var conteudo = ArquivoTexto.Ler(arquivo);
            v.Verificar("e o registro dela continua no arquivo depois de uma gravação nova",
                conteudo.Contains("\"sumiu\""), conteudo);

            // O jogo volta com o mesmo id e o histórico volta com ele.
            biblioteca.Jogos.Add(new Jogo { Id = "sumiu", Titulo = "Voltou", ExecutavelRelativo = @"Jogos\s\s.exe" });
            var comOJogoDeVolta = new EstatisticasDeSessoes(HistoricoDeSessoes.Carregar(arquivo), biblioteca);

            v.Verificar("re-adicionar o jogo devolve o histórico dele",
                comOJogoDeVolta.SessoesDoJogo("sumiu") == 1);
            v.Verificar("e a contagem de órfãs zera", comOJogoDeVolta.Orfas == 0);
        }

        // ---- A seção "Continuar jogando" ---------------------------------------------------------

        private static void TestarLayoutComSecoes(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Layout com duas seções");

            var semSecao = new LayoutDaGrade(TamanhoCard.M, 1000, 50);
            var comSecao = new LayoutDaGrade(TamanhoCard.M, 1000, 50, 5);

            v.Verificar("sem seção, nada muda em relação à fase 4",
                !semSecao.TemSecoes && semSecao.AlturaDoCabecalho == 0 &&
                semSecao.Celula(0) == new LayoutDaGrade(TamanhoCard.M, 1000, 50, 0).Celula(0));

            v.Verificar("com seção, existem dois cabeçalhos", comSecao.TemSecoes && comSecao.Secoes == 2);
            v.Verificar("o primeiro card desce para dar lugar ao título",
                comSecao.Celula(0).Y > semSecao.Celula(0).Y,
                $"{comSecao.Celula(0).Y} vs {semSecao.Celula(0).Y}");
            v.Verificar("e o conteúdo fica mais alto (dois cabeçalhos a mais)",
                comSecao.AlturaTotal > semSecao.AlturaTotal);

            v.Verificar("índice 4 é da primeira seção", comSecao.SecaoDoIndice(4) == 0);
            v.Verificar("índice 5 é da segunda", comSecao.SecaoDoIndice(5) == 1);

            // A quebra de seção quebra a linha: o card 5 abre a segunda seção, mesmo que
            // caibam 5 por linha.
            v.Verificar("a segunda seção começa numa linha nova",
                comSecao.Celula(5).X == comSecao.Celula(0).X &&
                comSecao.Celula(5).Y > comSecao.Celula(4).Y);

            v.Verificar("e depois do cabeçalho dela, não encostado no card de cima",
                comSecao.Celula(5).Y - comSecao.Celula(4).Bottom >= comSecao.AlturaDoCabecalho);

            // Navegação: descer da seção 1 entra na 2, e andar de lado nunca troca de linha.
            v.Verificar("descer da última linha de 'continuar jogando' entra no acervo",
                comSecao.SecaoDoIndice(comSecao.Mover(0, 0, +1)) == 1,
                comSecao.Mover(0, 0, +1).ToString());
            v.Verificar("subir de volta devolve o mesmo card",
                comSecao.Mover(comSecao.Mover(0, 0, +1), 0, -1) == 0);
            v.Verificar("andar para a direita no fim da seção não pula para a de baixo",
                comSecao.Mover(4, +1, 0) == 4, comSecao.Mover(4, +1, 0).ToString());
            v.Verificar("nem para a esquerda no começo dela", comSecao.Mover(0, -1, 0) == 0);

            // Clique: o cabeçalho não é card.
            var cabecalho = comSecao.AreaDoCabecalho(0);
            v.Verificar("clique no cabeçalho não seleciona card nenhum",
                comSecao.IndiceEm(new Point(cabecalho.X + 10, cabecalho.Y + 4), 0) == -1);

            var primeiro = comSecao.Celula(0);
            v.Verificar("clique no primeiro card ainda acha o índice 0",
                comSecao.IndiceEm(new Point(primeiro.X + 5, primeiro.Y + 5), 0) == 0);

            var doAcervo = comSecao.Celula(7);
            v.Verificar("e clique num card da segunda seção acha o índice dele",
                comSecao.IndiceEm(new Point(doAcervo.X + 5, doAcervo.Y + 5), 0) == 7);

            // A faixa visível continua pequena: seção não pode arrastar a lista inteira
            // para a memória.
            var grande = new LayoutDaGrade(TamanhoCard.M, 1000, 200, 5);
            grande.FaixaVisivel(0, 700, out var de, out var ate);
            v.Verificar("a faixa visível continua sendo um punhado de cards",
                de == 0 && ate - de + 1 < 40, $"{de}..{ate}");

            v.Verificar("o topo para rolar inclui o cabeçalho da seção",
                grande.TopoParaRolar(0) < grande.Celula(0).Y);

            // Casos-limite que não podem estourar.
            var soASecao = new LayoutDaGrade(TamanhoCard.M, 1000, 5, 5);
            v.Verificar("seção do tamanho da lista inteira não vira seção", !soASecao.TemSecoes);

            var vazio = new LayoutDaGrade(TamanhoCard.M, 1000, 0, 5);
            v.Verificar("lista vazia com seção pedida não tem altura nem linhas",
                vazio.AlturaTotal == 0 && vazio.Linhas == 0 && !vazio.TemSecoes);
        }

        /// <summary>
        /// A seção dentro da janela de verdade: quem sobe, quantos, e as três condições que
        /// a desligam.
        /// </summary>
        private static void TestarContinuarJogandoNaJanela(Verificador v)
        {
            v.Escrever("");
            v.Escrever("\"Continuar jogando\" na grade");

            AcervoDeDemonstracao.Montar(30);
            try
            {
                ComJanela(janela =>
                {
                    var grade = janela.GradeParaDiagnostico;
                    var biblioteca = janela.BibliotecaParaDiagnostico;

                    v.Verificar("a grade abriu com a seção", grade.LayoutAtual.TemSecoes);
                    v.Verificar("com cinco jogos nela",
                        grade.LayoutAtual.QuantidadeNaPrimeiraSecao == 5,
                        grade.LayoutAtual.QuantidadeNaPrimeiraSecao.ToString());

                    // Nenhum card repetido: a lista visível é uma permutação do filtro.
                    var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var repetido = false;
                    foreach (var jogo in grade.Jogos)
                    {
                        if (!vistos.Add(jogo.Id)) repetido = true;
                    }

                    v.Verificar("nenhum jogo aparece duas vezes", !repetido);
                    v.Verificar("e a contagem da grade continua sendo a do acervo",
                        grade.Jogos.Count == biblioteca.Jogos.Count,
                        $"{grade.Jogos.Count} de {biblioteca.Jogos.Count}");

                    // Os cinco de cima são os cinco mais recentes, e em ordem.
                    var ordenados = true;
                    for (var i = 1; i < 5; i++)
                    {
                        var anterior = grade.Jogos[i - 1].UltimaVezJogado;
                        var atual = grade.Jogos[i].UltimaVezJogado;

                        if (anterior is null || atual is null || anterior < atual) ordenados = false;
                    }

                    v.Verificar("os cinco de cima têm data de jogo e vêm do mais recente para o menos",
                        ordenados);

                    var maisRecenteDoAcervo = (DateTime?)null;
                    foreach (var jogo in biblioteca.Jogos)
                    {
                        if (jogo.UltimaVezJogado is { } data &&
                            (maisRecenteDoAcervo is null || data > maisRecenteDoAcervo)) maisRecenteDoAcervo = data;
                    }

                    v.Verificar("e o primeiro card é o jogo mais recente do acervo",
                        grade.Jogos[0].UltimaVezJogado == maisRecenteDoAcervo);

                    // Busca digitada desliga a seção: filtro é pergunta com resposta exata.
                    janela.BuscarParaDiagnostico("a");
                    v.Verificar("com busca digitada a seção desaparece", !grade.LayoutAtual.TemSecoes);

                    janela.BuscarParaDiagnostico("");
                    v.Verificar("e volta quando eu apago a busca", grade.LayoutAtual.TemSecoes);

                    // A opção das configurações desliga de verdade.
                    janela.DefinirContinuarJogandoParaDiagnostico(false);
                    v.Verificar("desligada nas configurações, a seção não aparece", !grade.LayoutAtual.TemSecoes);
                    v.Verificar("e a grade continua com todos os jogos",
                        grade.Jogos.Count == biblioteca.Jogos.Count);

                    janela.DefinirContinuarJogandoParaDiagnostico(true);

                    // Acervo sem histórico nenhum: a seção se esconde sozinha.
                    var datas = new List<DateTime?>();
                    foreach (var jogo in biblioteca.Jogos)
                    {
                        datas.Add(jogo.UltimaVezJogado);
                        jogo.UltimaVezJogado = null;
                    }

                    janela.ReaplicarFiltrosParaDiagnostico();
                    v.Verificar("sem nenhum jogo jogado, a seção se esconde sozinha",
                        !grade.LayoutAtual.TemSecoes);

                    for (var i = 0; i < biblioteca.Jogos.Count; i++) biblioteca.Jogos[i].UltimaVezJogado = datas[i];
                    janela.ReaplicarFiltrosParaDiagnostico();

                    // Navegar com o controle atravessa a fronteira das seções.
                    v.Verificar("o comando Baixo atravessa da seção para o acervo",
                        janela.DespacharParaDiagnostico(ComandoDeNavegacao.Baixo) &&
                        grade.LayoutAtual.SecaoDoIndice(grade.IndiceSelecionado) == 1,
                        grade.IndiceSelecionado.ToString());
                });
            }
            finally
            {
                AcervoDeDemonstracao.Limpar();
            }
        }

        /// <summary>
        /// A opção e o caminho para as estatísticas dentro das configurações.
        ///
        /// Geometria entra aqui pelo mesmo motivo da fase 3: um controle posicionado fora da
        /// área cliente abre, funciona e não dá para clicar — e nenhum teste de
        /// comportamento vê isso.
        /// </summary>
        private static void TestarOpcaoNasConfiguracoes(Verificador v)
        {
            v.Escrever("");
            v.Escrever("A opção nas configurações");

            var config = new Config { MostrarContinuarJogando = true };
            var biblioteca = new Biblioteca();

            using (var janela = new FormConfiguracoes(config, biblioteca))
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                var caixa = janela.CaixaDeContinuarJogando;
                var botao = janela.BotaoDasEstatisticas;

                v.Verificar("a caixa começa marcada, como está no config", caixa.Checked);
                v.Verificar("existe o botão das estatísticas", botao is not null);

                v.Verificar("a caixa cabe na área cliente da janela",
                    Dentro(janela, caixa), Descrever(janela, caixa));

                if (botao is not null)
                {
                    v.Verificar("e o botão também", Dentro(janela, botao), Descrever(janela, botao));
                    v.Verificar("o botão diz qual é a tecla dele", botao.Text.Contains("F9"), botao.Text);
                }

                // Desmarcar e salvar tem que chegar no Config.
                caixa.Checked = false;
                (janela.AcceptButton as Button)?.PerformClick();

                v.Verificar("desmarcar e salvar desliga a seção no config", !config.MostrarContinuarJogando);
                v.Verificar("e a janela avisa que algo mudou", janela.Mudou);
            }

            // O botão das estatísticas fecha a janela pedindo a tela — quem abre é a
            // janela principal, depois, para não empilhar dois modais.
            using (var janela = new FormConfiguracoes(config, biblioteca))
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                v.Verificar("no começo, ninguém pediu estatísticas", !janela.PediuEstatisticas);

                janela.BotaoDasEstatisticas?.PerformClick();

                v.Verificar("clicar no botão registra o pedido", janela.PediuEstatisticas);
                v.Verificar("e NÃO grava configuração nenhuma (não é o botão Salvar)", !janela.Mudou);
            }
        }

        private static bool Dentro(Form janela, Control controle)
        {
            var area = janela.RectangleToScreen(janela.ClientRectangle);
            var dele = controle.Parent?.RectangleToScreen(controle.Bounds) ?? Rectangle.Empty;

            return !dele.IsEmpty && area.Contains(dele);
        }

        private static string Descrever(Form janela, Control controle)
            => $"controle {controle.Bounds} em janela cliente {janela.ClientRectangle}";

        /// <summary>
        /// A regra invisível da fase 13: abrir o launcher não lê <c>sessoes.json</c>.
        ///
        /// O contador de leituras do <see cref="ArquivoTexto"/> existe só para isto. A spec
        /// proíbe explicitamente inventar uma abstração de sistema de arquivos inteira para
        /// provar uma linha — e está certa.
        /// </summary>
        private static void TestarAberturaNaoLeOHistorico(Verificador v)
        {
            v.Escrever("");
            v.Escrever("A abertura do launcher não toca em sessoes.json");

            AcervoDeDemonstracao.Montar(12);
            try
            {
                // Um histórico de verdade em disco: se ele não existisse, "não foi lido"
                // seria verdade por acidente.
                var historico = new HistoricoDeSessoes();
                var biblioteca = Biblioteca.Carregar();

                foreach (var jogo in biblioteca.Jogos)
                    historico.Sessoes.Add(new Sessao(jogo.Id, DateTime.UtcNow.AddDays(-1), 1800));

                historico.Salvar();

                v.Verificar("o histórico existe em disco antes do teste", File.Exists(Caminhos.ArquivoSessoes));

                ArquivoTexto.ZerarContadorDeLeituras();

                ComJanela(janela =>
                {
                    v.Verificar("a biblioteca FOI lida na abertura (senão o contador não prova nada)",
                        ArquivoTexto.LeiturasDe("biblioteca.json") > 0,
                        ArquivoTexto.LeiturasDe("biblioteca.json").ToString());

                    v.Verificar("e sessoes.json NÃO foi lido nenhuma vez",
                        ArquivoTexto.LeiturasDe("sessoes.json") == 0,
                        ArquivoTexto.LeiturasDe("sessoes.json").ToString());

                    // F5 é o outro caminho barato que não pode encostar no histórico.
                    janela.RecarregarParaDiagnostico();
                    v.Verificar("F5 recarrega a biblioteca e continua sem ler o histórico",
                        ArquivoTexto.LeiturasDe("sessoes.json") == 0,
                        ArquivoTexto.LeiturasDe("sessoes.json").ToString());

                    // Abrir os detalhes é o momento em que ele PODE ser lido — e é lido uma
                    // vez só, porque fica em memória depois.
                    var painel = janela.AbrirDetalhesParaDiagnostico();

                    v.Verificar("abrir a tela de detalhes lê o histórico",
                        ArquivoTexto.LeiturasDe("sessoes.json") == 1,
                        ArquivoTexto.LeiturasDe("sessoes.json").ToString());

                    v.Verificar("e a ficha do jogo mostra as sessões dele",
                        painel.SessoesCarregadasParaDiagnostico == 1,
                        painel.SessoesCarregadasParaDiagnostico.ToString());

                    v.Verificar("com o total do mês preenchido",
                        painel.SegundosNoMesParaDiagnostico == 1800,
                        painel.SegundosNoMesParaDiagnostico.ToString());

                    janela.FecharDetalhesParaDiagnostico();
                    janela.AbrirDetalhesParaDiagnostico();

                    v.Verificar("abrir de novo não relê o arquivo (fica em memória)",
                        ArquivoTexto.LeiturasDe("sessoes.json") == 1,
                        ArquivoTexto.LeiturasDe("sessoes.json").ToString());

                    janela.FecharDetalhesParaDiagnostico();
                });
            }
            finally
            {
                AcervoDeDemonstracao.Limpar();
            }
        }

        // ---- Apoio -------------------------------------------------------------------------------

        private static void ComJanela(Action<FormPrincipal> teste)
        {
            using (var janela = new FormPrincipal())
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Size = new Size(1200, 800);
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                try
                {
                    teste(janela);
                }
                finally
                {
                    janela.Close();
                }
            }
        }

        private static void PrepararSandbox(string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);
            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();
        }

        private static void LimparSandbox(Verificador v, string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox);
            try
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception ex)
            {
                v.Escrever($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
            }
        }
    }
}
#endif   // DEBUG
