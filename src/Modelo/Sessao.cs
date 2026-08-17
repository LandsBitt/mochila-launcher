using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mochila.Dados;

namespace Mochila.Modelo
{
    /// <summary>
    /// Uma sessão de jogo já encerrada: quem, quando começou e quanto durou.
    ///
    /// A data é gravada em <b>UTC com Z</b>, como o resto do projeto. Toda pergunta que
    /// se faz a ela, porém, é de horário LOCAL ("horas por mês", "jogado esta semana"),
    /// e a conversão acontece na leitura — ver <see cref="InicioLocal"/>. Uma sessão
    /// começada às 22h de 31/01 no meu fuso não pode aparecer em fevereiro só porque em
    /// UTC ela é 01/02.
    /// </summary>
    public sealed class Sessao
    {
        public Sessao(string jogoId, DateTime inicioUtc, int segundos)
        {
            JogoId = jogoId ?? "";
            InicioUtc = inicioUtc.Kind == DateTimeKind.Utc ? inicioUtc : inicioUtc.ToUniversalTime();
            Segundos = Math.Max(0, segundos);
        }

        public string JogoId { get; }

        /// <summary>Sempre UTC.</summary>
        public DateTime InicioUtc { get; }

        public int Segundos { get; }

        /// <summary>O começo no fuso desta máquina. É por aqui que toda agregação passa.</summary>
        public DateTime InicioLocal => InicioUtc.ToLocalTime();

        /// <summary>
        /// Lê uma sessão do JSON. Devolve null para registro sem jogo ou sem data — linha
        /// quebrada no meio do arquivo não pode derrubar o histórico inteiro.
        /// </summary>
        public static Sessao? DeJson(Dictionary<string, object>? objeto)
        {
            if (objeto is null) return null;

            var id = (Json.Texto(objeto, "jogoId", "") ?? "").Trim();
            if (id.Length == 0) return null;

            if (Json.DataOpcional(objeto, "inicio") is not { } inicio) return null;

            return new Sessao(id, inicio, Json.Inteiro(objeto, "segundos", 0));
        }

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("jogoId", JogoId)
            .Add("inicio", Json.FormatarData(InicioUtc))
            .Add("segundos", Segundos);
    }

    /// <summary>
    /// O conteúdo de <c>_mochila\sessoes.json</c>: o histórico bruto, em ordem de
    /// gravação.
    ///
    /// Regras da fase 13 que moram aqui:
    ///
    /// <list type="bullet">
    /// <item><b>Não compacta, não rotaciona, não resume.</b> Duas sessões por dia em cinco
    /// anos são ~3.600 registros e uns 300 KB. Não existe problema a resolver.</item>
    /// <item><b>Arquivo ausente ou corrompido não trava nada.</b> Abre com histórico vazio
    /// e <see cref="Corrompido"/> ligado, para a janela dar o aviso discreto. Estatística
    /// nunca pode ser o motivo de o launcher não abrir.</item>
    /// <item><b><c>jogoId</c> órfão é ignorado na leitura, nunca apagado.</b> Quem ignora é
    /// a agregação (<see cref="EstatisticasDeSessoes"/>); o arquivo continua com ele,
    /// porque o jogo pode voltar com o mesmo id — e voltar com o histórico é o
    /// comportamento que a regra global de <c>id</c> promete.</item>
    /// </list>
    /// </summary>
    public sealed class HistoricoDeSessoes
    {
        public const int VersaoAtual = 1;

        public int Versao { get; private set; } = VersaoAtual;

        /// <summary>
        /// Arquivo de versão mais nova que este binário: dá para ler, não dá para gravar.
        /// Mesma regra da biblioteca — regravar o que eu não entendo por inteiro é destruir
        /// em silêncio o que a versão nova escreveu.
        /// </summary>
        public bool SomenteLeitura { get; private set; }

        /// <summary>
        /// O arquivo existia e não deu para entender. O histórico abre vazio, e a primeira
        /// gravação põe o arquivo problemático de lado com nome datado em vez de
        /// sobrescrevê-lo — pode ter dado recuperável à mão lá dentro.
        /// </summary>
        public bool Corrompido { get; private set; }

        public List<Sessao> Sessoes { get; } = new List<Sessao>();

        /// <summary>
        /// De onde este histórico veio, e para onde ele volta.
        ///
        /// Existe porque <see cref="Registrar"/> grava sozinho: sem lembrar o caminho, um
        /// histórico lido de um arquivo qualquer gravaria no <c>sessoes.json</c> padrão — o
        /// tipo de erro que só aparece quando alguém olha o arquivo errado.
        /// </summary>
        private string _caminho = Caminhos.ArquivoSessoes;

        // ---- Persistência ------------------------------------------------------------------

        public static HistoricoDeSessoes Carregar() => Carregar(Caminhos.ArquivoSessoes);

        /// <summary>
        /// Nunca lança. Toda falha vira histórico vazio com <see cref="Corrompido"/>
        /// ligado: o pior que pode acontecer com uma estatística é ela faltar.
        /// </summary>
        public static HistoricoDeSessoes Carregar(string caminhoArquivo)
        {
            var historico = new HistoricoDeSessoes { _caminho = caminhoArquivo };
            if (!ArquivoTexto.Existe(caminhoArquivo)) return historico;

            Dictionary<string, object>? raiz;
            try
            {
                raiz = Json.ComoObjeto(Json.Analisar(ArquivoTexto.Ler(caminhoArquivo)));
            }
            catch (Exception)
            {
                historico.Corrompido = true;
                return historico;
            }

            if (raiz is null)
            {
                historico.Corrompido = true;
                return historico;
            }

            historico.Versao = Json.Inteiro(raiz, "versao", VersaoAtual);
            historico.SomenteLeitura = historico.Versao > VersaoAtual;

            if (raiz.TryGetValue("sessoes", out var lista))
            {
                foreach (var item in Json.ComoLista(lista))
                {
                    if (Sessao.DeJson(Json.ComoObjeto(item)) is { } sessao) historico.Sessoes.Add(sessao);
                }
            }

            return historico;
        }

        /// <summary>
        /// Acrescenta a sessão e grava na hora. Devolve false quando não gravou (arquivo de
        /// versão mais nova, ou falha de I/O) — quem chama transforma isso num aviso de
        /// rodapé, nunca numa caixa de erro: o jogo já acabou, não há nada a interromper.
        /// </summary>
        public bool Registrar(Sessao sessao)
        {
            if (sessao is null) throw new ArgumentNullException(nameof(sessao));
            if (SomenteLeitura) return false;

            Sessoes.Add(sessao);

            try
            {
                Salvar();
                return true;
            }
            catch (Exception)
            {
                // Disco cheio, HD arrancado, pasta somente-leitura. A sessão fica em
                // memória (a tela de detalhes já a mostra) e o acumulado do jogo, que é o
                // que ordena a grade, foi gravado pela biblioteca no caminho normal.
                return false;
            }
        }

        /// <summary>Grava de volta no arquivo de onde este histórico veio.</summary>
        public void Salvar() => Salvar(_caminho);

        public void Salvar(string caminhoArquivo)
        {
            if (SomenteLeitura) return;

            _caminho = caminhoArquivo;

            // Arquivo ilegível não é sobrescrito: sai de cena com nome datado, do mesmo
            // jeito que a biblioteca corrompida sai. Cinco anos de histórico não podem ser
            // apagados por uma gravação de rotina.
            if (Corrompido)
            {
                PorDeLado(caminhoArquivo);
                Corrompido = false;
            }

            Versao = VersaoAtual;
            ArquivoTexto.EscreverAtomico(caminhoArquivo, Json.Escrever(ParaJson()));
        }

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("versao", Versao)
            .Add("sessoes", Sessoes.Select(s => (object?)s.ParaJson()).ToList());

        private static void PorDeLado(string caminho)
        {
            try
            {
                if (!File.Exists(caminho)) return;

                // O texto literal vai entre apóstrofos: sem isso, "m", "d" e "s" dentro de
                // "corrompido" são interpretados como minuto, dia e segundo, e o arquivo sai
                // com nome de lixo ("...corro19pi17o-..."). Custa um par de apóstrofos e
                // aparece só no dia em que eu preciso justamente recuperar o arquivo.
                var nome = Path.GetFileName(caminho) +
                           DateTime.Now.ToString("'.corrompido-'yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

                File.Move(caminho, Path.Combine(Path.GetDirectoryName(caminho)!, nome));
            }
            catch (Exception)
            {
                // Não deu para preservar: a gravação a seguir passa por cima. Perder o
                // histórico ilegível é ruim; não conseguir gravar sessão nenhuma daqui
                // para a frente seria pior.
            }
        }
    }
}
