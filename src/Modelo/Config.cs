using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Launcher.Dados;

namespace Launcher.Modelo
{
    /// <summary>Tamanho do card na grade de capas.</summary>
    public enum TamanhoCard
    {
        P = 0,
        M = 1,
        G = 2
    }

    /// <summary>Critério de ordenação da grade.</summary>
    public enum OrdenacaoBiblioteca
    {
        Alfabetica = 0,
        MaisJogados = 1,
        JogadosRecentemente = 2
    }

    /// <summary>
    /// Conteúdo do _launcher\config.json: chave da API do SteamGridDB e preferências de tela.
    /// Nada aqui vai para o registro nem para o AppData.
    /// </summary>
    public sealed class Config
    {
        public const int VersaoAtual = 1;

        public int Versao { get; set; } = VersaoAtual;

        /// <summary>Vazio = pula a busca online e vai direto para o fallback de capa.</summary>
        public string SteamGridDbApiKey { get; set; } = "";

        public TamanhoCard TamanhoCard { get; set; } = TamanhoCard.M;

        public OrdenacaoBiblioteca Ordenacao { get; set; } = OrdenacaoBiblioteca.Alfabetica;

        public bool SomenteFavoritos { get; set; }

        /// <summary>
        /// Pastas que o scanner nem olha. Aceita curinga ("Riot*"), casa pelo nome da pasta
        /// (não pelo caminho) e é editável no config.json — dá para acrescentar outras sem
        /// recompilar. Lista presente e vazia no arquivo significa "não ignore nada".
        /// </summary>
        public List<string> PastasIgnoradas { get; set; } = new List<string>(PastasIgnoradasPadrao);

        /// <summary>Executáveis que o scanner nem considera candidatos. Mesmas regras de curinga.</summary>
        public List<string> ExecutaveisIgnorados { get; set; } = new List<string>(ExecutaveisIgnoradosPadrao);

        /// <summary>
        /// Padrão: as plataformas com launcher e atualizador próprios.
        ///
        /// Nada disso é portátil — depende de instalação, serviço, registro e login, e
        /// tentar catalogar entrada por entrada só encheria a biblioteca de atalho que não
        /// abre em outro PC. O jogo de plataforma se abre pela plataforma; este launcher é
        /// para o acervo solto do HD.
        ///
        /// Não confundir com jogo indie que tem launcher próprio: aquele é um exe na pasta
        /// dele, roda do HD e continua sendo catalogado normalmente (a adoção do
        /// processo-filho da fase 5 existe justamente para ele).
        /// </summary>
        public static readonly string[] PastasIgnoradasPadrao =
        {
            // Riot
            "Riot Games", "RiotClientElectron", "League of Legends", "VALORANT",
            // Demais plataformas
            "Epic Games", "Steam", "steamapps", "Battle.net", "Ubisoft", "Ubisoft Game Launcher",
            "EA Games", "EA Desktop", "Origin", "Origin Games", "GOG Galaxy"
        };

        public static readonly string[] ExecutaveisIgnoradosPadrao =
        {
            "RiotClient", "RiotClientServices", "LeagueClient"
        };

        public bool TemChaveSteamGridDb() => !string.IsNullOrWhiteSpace(SteamGridDbApiKey);

        // ---- Persistência --------------------------------------------------------------------

        /// <summary>Carrega _launcher\config.json. Ausente ou ilegível devolve os padrões.</summary>
        public static Config Carregar() => Carregar(Caminhos.ArquivoConfig);

        public static Config Carregar(string caminhoArquivo)
        {
            if (!ArquivoTexto.Existe(caminhoArquivo)) return new Config();

            try
            {
                if (Json.ComoObjeto(Json.Analisar(ArquivoTexto.Ler(caminhoArquivo))) is not { } raiz)
                    return new Config();

                return new Config
                {
                    Versao = Json.Inteiro(raiz, "versao", VersaoAtual),
                    SteamGridDbApiKey = Json.Texto(raiz, "steamGridDbApiKey", "") ?? "",
                    TamanhoCard = LerEnum(Json.Texto(raiz, "tamanhoCard", null), TamanhoCard.M),
                    Ordenacao = LerEnum(Json.Texto(raiz, "ordenacao", null), OrdenacaoBiblioteca.Alfabetica),
                    SomenteFavoritos = Json.Booleano(raiz, "somenteFavoritos", false),
                    PastasIgnoradas = LerLista(raiz, "pastasIgnoradas", PastasIgnoradasPadrao),
                    ExecutaveisIgnorados = LerLista(raiz, "executaveisIgnorados", ExecutaveisIgnoradosPadrao)
                };
            }
            catch (Exception)
            {
                // Preferência corrompida não é motivo para não abrir o launcher.
                return new Config();
            }
        }

        public void Salvar() => Salvar(Caminhos.ArquivoConfig);

        public void Salvar(string caminhoArquivo)
            => ArquivoTexto.EscreverAtomico(caminhoArquivo, Json.Escrever(ParaJson()));

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("versao", Versao)
            .Add("steamGridDbApiKey", SteamGridDbApiKey)
            .Add("tamanhoCard", TamanhoCard.ToString())
            .Add("ordenacao", Ordenacao.ToString())
            .Add("somenteFavoritos", SomenteFavoritos)
            .Add("pastasIgnoradas", PastasIgnoradas.Cast<object?>().ToList())
            .Add("executaveisIgnorados", ExecutaveisIgnorados.Cast<object?>().ToList());

        /// <summary>
        /// Lê uma lista de textos. Chave ausente devolve o padrão; chave presente vale
        /// como está, inclusive vazia — apagar a lista no arquivo tem que significar
        /// "não ignore nada", não "volte aos padrões".
        /// </summary>
        private static List<string> LerLista(Dictionary<string, object> raiz, string chave, string[] padrao)
        {
            if (!raiz.ContainsKey(chave)) return new List<string>(padrao);

            var lista = new List<string>();
            foreach (var item in Json.ComoLista(raiz[chave]))
            {
                var texto = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(texto)) lista.Add(texto!.Trim());
            }
            return lista;
        }

        /// <summary>Lê enum por nome, tolerando lixo no arquivo.</summary>
        private static T LerEnum<T>(string? texto, T padrao) where T : struct, Enum
            => Enum.TryParse<T>(texto, ignoreCase: true, out var valor) && Enum.IsDefined(typeof(T), valor)
                ? valor
                : padrao;
    }
}
