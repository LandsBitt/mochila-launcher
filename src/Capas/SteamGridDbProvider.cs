using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Mochila.Dados;

namespace Mochila.Capas
{
    /// <summary>
    /// Capas do SteamGridDB (https://www.steamgriddb.com/api/v2).
    ///
    /// Três coisas que a API exige e que é fácil errar:
    ///
    /// 1. Toda resposta vem num envelope {"success": true, "data": [...]}. Ler "data"
    ///    sem conferir "success" funciona até o dia em que não funciona.
    /// 2. As imagens são PNG **ou** JPG. Salvar tudo como .jpg cegamente grava PNG com
    ///    extensão errada — a extensão sai do Content-Type.
    /// 3. O campo "score" é o voto da comunidade. Ordenar por ele e pegar o primeiro dá
    ///    uma capa muito melhor do que confiar na ordem natural do retorno.
    ///
    /// Sobre a chave: ela vive no header Authorization e em nenhum outro lugar. Não vai
    /// para query string (que apareceria em log de proxy e em histórico), não vai para
    /// mensagem de erro e não vai para tela — nem em pedaço.
    /// </summary>
    public sealed class SteamGridDbProvider : ICapaProvider, IDisposable
    {
        public const string BaseOficial = "https://www.steamgriddb.com/api/v2";

        /// <summary>Proporção 2:3, o formato de boxart. É o que a grade desenha.</summary>
        private const string Dimensoes = "600x900";

        /// <summary>O formato de hero do SteamGridDB. É o que a fase 14 desfoca de fundo.</summary>
        private const string DimensoesDoHero = "1920x620";

        private static readonly TimeSpan TempoLimite = TimeSpan.FromSeconds(15);

        private readonly HttpClient _http;
        private readonly string _chave;
        private readonly string _base;
        private readonly bool _clienteProprio;

        /// <param name="manipulador">
        /// Injetado nos testes para simular respostas. Em produção fica null e o
        /// HttpClient padrão é criado aqui.
        /// </param>
        public SteamGridDbProvider(string? chaveDaApi, HttpMessageHandler? manipulador = null,
                                   string? enderecoBase = null)
        {
            _chave = (chaveDaApi ?? "").Trim();
            _base = (enderecoBase ?? BaseOficial).TrimEnd('/');

            _clienteProprio = manipulador is null;
            _http = manipulador is null ? new HttpClient() : new HttpClient(manipulador, disposeHandler: false);
            _http.Timeout = TempoLimite;

            // A chave vai UMA vez, no header. Nunca na URL.
            if (Configurado)
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _chave);

            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public string Nome => "SteamGridDB";

        public bool Configurado => _chave.Length > 0;

        // ---- Busca -------------------------------------------------------------------------

        public async Task<ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>> BuscarJogo(
            string termo, CancellationToken cancelamento)
        {
            if (!Configurado)
                return ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>.Erro(FalhaDeCapa.SemChave, SemChave);

            if (string.IsNullOrWhiteSpace(termo))
                return ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>.Erro(FalhaDeCapa.NaoEncontrado, "Título vazio.");

            var endereco = $"{_base}/search/autocomplete/{Uri.EscapeDataString(termo.Trim())}";
            var resposta = await LerJson(endereco, cancelamento).ConfigureAwait(false);

            if (!resposta.DeuCerto)
                return ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>.Erro(resposta.Falha, resposta.Mensagem);

            var jogos = new List<JogoDeCapa>();

            foreach (var item in Json.ComoLista(resposta.Valor!.Dados))
            {
                if (Json.ComoObjeto(item) is not { } objeto) continue;

                var id = Json.InteiroOpcional(objeto, "id");
                var nome = Json.Texto(objeto, "name", "") ?? "";

                if (id is null || nome.Length == 0) continue;

                jogos.Add(new JogoDeCapa(id.Value, nome, Json.Texto(objeto, "release_date", null)));
            }

            if (jogos.Count == 0)
                return ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>.Erro(FalhaDeCapa.NaoEncontrado,
                    $"Nenhum jogo com esse nome no {Nome}.");

            return ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>.Certo(jogos);
        }

        // ---- Download ----------------------------------------------------------------------

        public Task<ResultadoDeCapa<CapaBaixada>> BaixarCapa(
            int idDoJogo, TamanhoDeCapa tamanho, CancellationToken cancelamento)
            => BaixarArte(idDoJogo, TipoDeArte.Capa, tamanho, cancelamento);

        /// <summary>
        /// Baixa a arte mais votada do tipo pedido. Um caminho só para os três endpoints:
        /// o que muda entre eles está em <see cref="Endereco"/>, e nada mais.
        /// </summary>
        public async Task<ResultadoDeCapa<CapaBaixada>> BaixarArte(
            int idDoJogo, TipoDeArte tipo, TamanhoDeCapa tamanho, CancellationToken cancelamento)
        {
            if (!Configurado) return ResultadoDeCapa<CapaBaixada>.Erro(FalhaDeCapa.SemChave, SemChave);

            var lista = await ListarArte(idDoJogo, tipo, cancelamento).ConfigureAwait(false);
            if (!lista.DeuCerto) return ResultadoDeCapa<CapaBaixada>.Erro(lista.Falha, lista.Mensagem);

            // Mais votada primeiro: é a capa que a maioria considera a melhor.
            var melhor = lista.Valor![0];
            var endereco = tamanho == TamanhoDeCapa.Cheia ? melhor.UrlCheia : melhor.UrlMiniatura;

            if (string.IsNullOrEmpty(endereco))
                endereco = melhor.UrlCheia.Length > 0 ? melhor.UrlCheia : melhor.UrlMiniatura;

            if (string.IsNullOrEmpty(endereco))
                return ResultadoDeCapa<CapaBaixada>.Erro(FalhaDeCapa.NaoEncontrado, "A capa veio sem endereço.");

            return await BaixarBytes(endereco, cancelamento).ConfigureAwait(false);
        }

        /// <summary>
        /// Capas do jogo, da mais votada para a menos. Fica separado do download para o
        /// seletor de capas poder mostrar as opções.
        /// </summary>
        public Task<ResultadoDeCapa<IReadOnlyList<CapaDisponivel>>> ListarCapas(
            int idDoJogo, CancellationToken cancelamento)
            => ListarArte(idDoJogo, TipoDeArte.Capa, cancelamento);

        /// <summary>
        /// As artes de um tipo, da mais votada para a menos.
        ///
        /// Os três endpoints respondem no mesmo envelope e com os mesmos campos
        /// (<c>url</c>, <c>thumb</c>, <c>score</c>), então a única diferença real entre
        /// pedir capa, hero e logo é a linha montada em <see cref="Endereco"/>.
        /// </summary>
        public async Task<ResultadoDeCapa<IReadOnlyList<CapaDisponivel>>> ListarArte(
            int idDoJogo, TipoDeArte tipo, CancellationToken cancelamento)
        {
            if (!Configurado)
                return ResultadoDeCapa<IReadOnlyList<CapaDisponivel>>.Erro(FalhaDeCapa.SemChave, SemChave);

            var resposta = await LerJson(Endereco(idDoJogo, tipo), cancelamento).ConfigureAwait(false);

            if (!resposta.DeuCerto)
                return ResultadoDeCapa<IReadOnlyList<CapaDisponivel>>.Erro(resposta.Falha, resposta.Mensagem);

            var capas = new List<CapaDisponivel>();

            foreach (var item in Json.ComoLista(resposta.Valor!.Dados))
            {
                if (Json.ComoObjeto(item) is not { } objeto) continue;

                var url = Json.Texto(objeto, "url", "") ?? "";
                var thumb = Json.Texto(objeto, "thumb", "") ?? "";

                if (url.Length == 0 && thumb.Length == 0) continue;

                capas.Add(new CapaDisponivel(
                    Json.Inteiro(objeto, "id", 0),
                    Json.Numero(objeto, "score", 0),
                    url,
                    thumb,
                    Json.Texto(objeto, "style", "") ?? ""));
            }

            if (capas.Count == 0)
                return ResultadoDeCapa<IReadOnlyList<CapaDisponivel>>.Erro(FalhaDeCapa.NaoEncontrado, SemArte(tipo));

            capas.Sort((a, b) => b.Pontuacao.CompareTo(a.Pontuacao));
            return ResultadoDeCapa<IReadOnlyList<CapaDisponivel>>.Certo(capas);
        }

        /// <summary>
        /// A tabela dos três endpoints. Tudo que difere entre capa, hero e logo está aqui.
        ///
        /// Comum aos três: <c>types=static</c> evita APNG animado (pesa, e o WinForms não
        /// anima); <c>nsfw</c> e <c>humor</c> de fora, senão vem arte-piada e conteúdo
        /// adulto; <c>mimes</c> restringe ao que o GDI+ sabe abrir.
        ///
        /// <b>O logo pede só PNG, e isso é regra da spec, não preferência.</b> Ele existe
        /// para ser desenhado por cima do fundo, e um logo em jpg vem com o retângulo de
        /// fundo embutido — o alfa é a razão de ser do arquivo.
        ///
        /// <b>O logo também não leva dimensões.</b> Logo não tem proporção fixa (uns são
        /// largos, outros quadrados); filtrar por tamanho aqui devolveria lista vazia para
        /// quase todo jogo.
        /// </summary>
        private string Endereco(int idDoJogo, TipoDeArte tipo)
        {
            var id = idDoJogo.ToString(CultureInfo.InvariantCulture);
            var comum = "types=static&nsfw=false&humor=false";

            return tipo switch
            {
                TipoDeArte.Hero =>
                    $"{_base}/heroes/game/{id}?dimensions={DimensoesDoHero}&{comum}&mimes=image/png,image/jpeg",
                TipoDeArte.Logo =>
                    $"{_base}/logos/game/{id}?{comum}&mimes=image/png",
                _ =>
                    $"{_base}/grids/game/{id}?dimensions={Dimensoes}&{comum}&mimes=image/png,image/jpeg"
            };
        }

        /// <summary>
        /// Frases diferentes porque as reações são diferentes: sem capa eu vou atrás de uma
        /// à mão; sem hero ou sem logo eu não faço nada, porque é o normal para jogo antigo.
        /// </summary>
        private static string SemArte(TipoDeArte tipo) => tipo switch
        {
            TipoDeArte.Hero => "Esse jogo não tem arte de fundo no acervo.",
            TipoDeArte.Logo => "Esse jogo não tem logo no acervo.",
            _ => "Esse jogo não tem capa 600x900 no acervo."
        };

        /// <summary>Baixa uma imagem. Público porque o seletor mostra as miniaturas antes de eu escolher.</summary>
        public async Task<ResultadoDeCapa<CapaBaixada>> BaixarBytes(string endereco, CancellationToken cancelamento)
        {
            try
            {
                using (var resposta = await _http.GetAsync(endereco, cancelamento).ConfigureAwait(false))
                {
                    if (!resposta.IsSuccessStatusCode)
                        return ResultadoDeCapa<CapaBaixada>.Erro(DeStatus(resposta.StatusCode),
                            $"O servidor respondeu {(int)resposta.StatusCode} ao baixar a imagem.");

                    var bytes = await resposta.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length == 0)
                        return ResultadoDeCapa<CapaBaixada>.Erro(FalhaDeCapa.RespostaInvalida, "A imagem veio vazia.");

                    var tipo = resposta.Content.Headers.ContentType?.MediaType;
                    return ResultadoDeCapa<CapaBaixada>.Certo(
                        new CapaBaixada(bytes, ExtensaoDe(tipo, endereco), Nome));
                }
            }
            catch (Exception erro)
            {
                return ResultadoDeCapa<CapaBaixada>.Erro(Classificar(erro, cancelamento), DescreverFalha(erro, cancelamento));
            }
        }

        // ---- HTTP -------------------------------------------------------------------------

        private sealed class Envelope
        {
            public object? Dados;
        }

        /// <summary>
        /// GET + envelope. Todo tratamento de erro da API passa por aqui, e nenhuma das
        /// mensagens montadas aqui contém a chave.
        /// </summary>
        private async Task<ResultadoDeCapa<Envelope>> LerJson(string endereco, CancellationToken cancelamento)
        {
            try
            {
                using (var resposta = await _http.GetAsync(endereco, cancelamento).ConfigureAwait(false))
                {
                    if (!resposta.IsSuccessStatusCode)
                    {
                        var falha = DeStatus(resposta.StatusCode);
                        return ResultadoDeCapa<Envelope>.Erro(falha, MensagemDeStatus(falha, resposta.StatusCode));
                    }

                    var texto = await resposta.Content.ReadAsStringAsync().ConfigureAwait(false);

                    object? bruto;
                    try
                    {
                        bruto = Json.Analisar(texto);
                    }
                    catch (Exception)
                    {
                        return ResultadoDeCapa<Envelope>.Erro(FalhaDeCapa.RespostaInvalida,
                            "A resposta do serviço não é um JSON válido.");
                    }

                    if (Json.ComoObjeto(bruto) is not { } raiz)
                        return ResultadoDeCapa<Envelope>.Erro(FalhaDeCapa.RespostaInvalida,
                            "A resposta do serviço não veio no formato esperado.");

                    // O envelope manda: success falso não tem "data" confiável.
                    if (!Json.Booleano(raiz, "success", false))
                        return ResultadoDeCapa<Envelope>.Erro(FalhaDeCapa.NaoEncontrado,
                            "O serviço respondeu que não conseguiu atender o pedido.");

                    if (!raiz.TryGetValue("data", out var dados))
                        return ResultadoDeCapa<Envelope>.Erro(FalhaDeCapa.RespostaInvalida,
                            "A resposta veio sem os dados.");

                    return ResultadoDeCapa<Envelope>.Certo(new Envelope { Dados = dados });
                }
            }
            catch (Exception erro)
            {
                return ResultadoDeCapa<Envelope>.Erro(Classificar(erro, cancelamento), DescreverFalha(erro, cancelamento));
            }
        }

        private static FalhaDeCapa DeStatus(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized => FalhaDeCapa.ChaveInvalida,
            HttpStatusCode.Forbidden => FalhaDeCapa.ChaveInvalida,
            HttpStatusCode.NotFound => FalhaDeCapa.NaoEncontrado,
            (HttpStatusCode)429 => FalhaDeCapa.LimiteExcedido,
            _ => FalhaDeCapa.RespostaInvalida
        };

        /// <summary>
        /// Mensagens fixas, escritas à mão. Nada aqui é montado a partir da requisição —
        /// é assim que se garante que a chave não vaza para a tela.
        /// </summary>
        private static string MensagemDeStatus(FalhaDeCapa falha, HttpStatusCode status) => falha switch
        {
            FalhaDeCapa.ChaveInvalida => "A chave da API foi recusada. Confira em Configurações.",
            FalhaDeCapa.NaoEncontrado => "O serviço não tem esse jogo.",
            FalhaDeCapa.LimiteExcedido => "Muitos pedidos seguidos. Espere um pouco e tente de novo.",
            _ => $"O serviço respondeu com o código {(int)status}."
        };

        private static FalhaDeCapa Classificar(Exception erro, CancellationToken cancelamento)
        {
            if (cancelamento.IsCancellationRequested) return FalhaDeCapa.Cancelado;

            // HttpClient sinaliza estouro de tempo como cancelamento — sem token cancelado,
            // é timeout de verdade.
            if (erro is TaskCanceledException or OperationCanceledException) return FalhaDeCapa.TempoEsgotado;
            if (erro is HttpRequestException) return FalhaDeCapa.SemRede;

            return FalhaDeCapa.RespostaInvalida;
        }

        /// <summary>
        /// Texto do erro sem nada vindo da exceção original.
        ///
        /// De propósito: mensagem de HttpRequestException às vezes traz a requisição
        /// inteira, e a nossa carrega o header Authorization. Melhor uma frase genérica
        /// do que a chave num MessageBox.
        /// </summary>
        private static string DescreverFalha(Exception erro, CancellationToken cancelamento)
            => Classificar(erro, cancelamento) switch
            {
                FalhaDeCapa.Cancelado => "Cancelado.",
                FalhaDeCapa.TempoEsgotado => "O serviço demorou demais para responder.",
                FalhaDeCapa.SemRede => "Sem conexão com o serviço de capas.",
                _ => "Não consegui entender a resposta do serviço."
            };

        private const string SemChave =
            "Nenhuma chave do SteamGridDB configurada — a busca online está desligada.";

        /// <summary>
        /// Extensão pelo Content-Type, com a URL como segunda opção. Nunca chuta .jpg:
        /// PNG salvo como .jpg engana o próximo programa que abrir a pasta.
        /// </summary>
        public static string ExtensaoDe(string? tipoDeConteudo, string? endereco)
        {
            var tipo = (tipoDeConteudo ?? "").Trim().ToLowerInvariant();

            switch (tipo)
            {
                case "image/png": return ".png";
                case "image/jpeg":
                case "image/jpg": return ".jpg";
                case "image/webp": return ".webp";
                case "image/gif": return ".gif";
            }

            var daUrl = ExtensaoDaUrl(endereco);
            return daUrl.Length > 0 ? daUrl : ".png";
        }

        private static string ExtensaoDaUrl(string? endereco)
        {
            if (string.IsNullOrEmpty(endereco)) return "";

            var texto = endereco!;
            var corte = texto.IndexOfAny(new[] { '?', '#' });
            if (corte >= 0) texto = texto.Substring(0, corte);

            var ponto = texto.LastIndexOf('.');
            if (ponto < 0 || ponto < texto.LastIndexOf('/')) return "";

            var extensao = texto.Substring(ponto).ToLowerInvariant();
            return extensao switch
            {
                ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => extensao == ".jpeg" ? ".jpg" : extensao,
                _ => ""
            };
        }

        public void Dispose()
        {
            if (_clienteProprio) _http.Dispose();
        }
    }

    /// <summary>Uma capa disponível no provedor, antes de baixar.</summary>
    public sealed class CapaDisponivel
    {
        public CapaDisponivel(int id, double pontuacao, string urlCheia, string urlMiniatura, string estilo)
        {
            Id = id;
            Pontuacao = pontuacao;
            UrlCheia = urlCheia ?? "";
            UrlMiniatura = urlMiniatura ?? "";
            Estilo = estilo ?? "";
        }

        public int Id { get; }

        /// <summary>Voto da comunidade. É o critério de ordenação.</summary>
        public double Pontuacao { get; }

        public string UrlCheia { get; }

        public string UrlMiniatura { get; }

        public string Estilo { get; }
    }
}
