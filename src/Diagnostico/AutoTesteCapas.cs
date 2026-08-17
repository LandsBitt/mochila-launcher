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
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mochila.Capas;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação da fase 6: provedor de capas, fallback local e sigilo da chave.
    ///
    /// Nenhum teste toca a internet. Tudo passa por um <see cref="HttpSimulado"/>, o que
    /// deixa cobrir de graça o que na vida real é raro e caro de reproduzir: 401, 404,
    /// timeout, queda de rede e JSON quebrado.
    /// </summary>
    public static class AutoTesteCapas
    {
        private const string NomePastaSandbox = "_autoteste-capas-tmp";

        /// <summary>Chave falsa dos testes. Nenhuma mensagem pode conter isto.</summary>
        private const string ChaveFalsa = "chave-secreta-de-teste-abc123";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarChaveVazia(v);
                TestarBuscaComSucesso(v);
                TestarEscolhaDaMelhorCapa(v);
                TestarFalhasDeRede(v);
                TestarSigiloDaChave(v);
                TestarExtensaoPeloTipo(v);
                TestarGravacaoDaCapa(v, raizReal);
                TestarFallbackLocal(v, raizReal);
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

        // ---- Sem chave -------------------------------------------------------------------------

        /// <summary>
        /// Chave vazia não é erro: é o caminho manual. E, principalmente, não pode gerar
        /// nem UMA requisição — o launcher não fala com a internet sem eu mandar.
        /// </summary>
        private static void TestarChaveVazia(Verificador v)
        {
            v.Escrever("Chave vazia não toca na rede");

            var simulado = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true,\"data\":[]}"));

            using (var provedor = new SteamGridDbProvider("", simulado))
            {
                v.Verificar("provedor se declara não configurado", !provedor.Configurado);

                var busca = Esperar(provedor.BuscarJogo("NFS Carbon", CancellationToken.None));
                v.Verificar("busca devolve SemChave", busca.Falha == FalhaDeCapa.SemChave, busca.Falha.ToString());

                var download = Esperar(provedor.BaixarCapa(42, TamanhoDeCapa.Miniatura, CancellationToken.None));
                v.Verificar("download devolve SemChave", download.Falha == FalhaDeCapa.SemChave);

                v.Verificar("nenhuma requisição foi feita", simulado.Pedidos == 0, simulado.Pedidos.ToString());
                v.Verificar("e a mensagem explica sem parecer erro",
                    busca.Mensagem.IndexOf("chave", StringComparison.OrdinalIgnoreCase) >= 0, busca.Mensagem);
            }

            // Só espaço em branco conta como vazio.
            using (var comEspacos = new SteamGridDbProvider("   ", simulado))
                v.Verificar("chave só com espaços também é 'sem chave'", !comEspacos.Configurado);
        }

        // ---- Busca -----------------------------------------------------------------------------

        private static void TestarBuscaComSucesso(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Busca de jogo");

            const string resposta = @"{
              ""success"": true,
              ""data"": [
                { ""id"": 1234, ""name"": ""Need for Speed: Carbon"", ""release_date"": ""2006"" },
                { ""id"": 5678, ""name"": ""Need for Speed: Carbon - Own the City"" }
              ]
            }";

            var simulado = new HttpSimulado(_ => HttpSimulado.Json(resposta));

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, simulado))
            {
                var busca = Esperar(provedor.BuscarJogo("NFS Carbon", CancellationToken.None));

                v.Verificar("a busca deu certo", busca.DeuCerto, busca.Mensagem);
                v.Verificar("dois candidatos", busca.Valor!.Count == 2, busca.Valor.Count.ToString());
                v.Verificar("id lido", busca.Valor[0].Id == 1234, busca.Valor[0].Id.ToString());
                v.VerificarTexto("nome lido", "Need for Speed: Carbon", busca.Valor[0].Nome);
                v.VerificarTexto("data de lançamento lida", "2006", busca.Valor[0].Lancamento ?? "");

                v.Verificar("o termo foi codificado na URL",
                    simulado.UrlsPedidas[0].Contains("NFS%20Carbon"), simulado.UrlsPedidas[0]);
            }

            // Envelope com success:false não pode ser lido como se fosse dado bom.
            var negado = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":false,\"data\":[{\"id\":1,\"name\":\"x\"}]}"));
            using (var provedor = new SteamGridDbProvider(ChaveFalsa, negado))
            {
                var busca = Esperar(provedor.BuscarJogo("qualquer", CancellationToken.None));
                v.Verificar("success:false é tratado como sem resultado",
                    busca.Falha == FalhaDeCapa.NaoEncontrado, busca.Falha.ToString());
            }

            // Lista vazia é "não achei", não sucesso com zero.
            var vazio = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true,\"data\":[]}"));
            using (var provedor = new SteamGridDbProvider(ChaveFalsa, vazio))
            {
                var busca = Esperar(provedor.BuscarJogo("jogo inexistente", CancellationToken.None));
                v.Verificar("lista vazia vira NaoEncontrado", busca.Falha == FalhaDeCapa.NaoEncontrado);
            }
        }

        /// <summary>
        /// O campo "score" é o voto da comunidade, e a spec manda pegar a mais votada —
        /// não a primeira que o servidor devolveu.
        /// </summary>
        private static void TestarEscolhaDaMelhorCapa(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Escolha da capa (score manda, não a ordem)");

            const string grids = @"{
              ""success"": true,
              ""data"": [
                { ""id"": 10, ""score"": 3,  ""style"": ""alternate"", ""url"": ""https://cdn/10.png"", ""thumb"": ""https://cdn/t10.png"" },
                { ""id"": 20, ""score"": 47, ""style"": ""official"",  ""url"": ""https://cdn/20.jpg"", ""thumb"": ""https://cdn/t20.jpg"" },
                { ""id"": 30, ""score"": 12, ""style"": ""white"",     ""url"": ""https://cdn/30.png"", ""thumb"": ""https://cdn/t30.png"" }
              ]
            }";

            var pixels = PngDeUmPixel();

            var simulado = new HttpSimulado(requisicao =>
            {
                var url = requisicao.RequestUri!.ToString();

                if (url.Contains("/grids/game/")) return HttpSimulado.Json(grids);
                if (url.Contains("t20.jpg")) return HttpSimulado.Imagem(pixels, "image/jpeg");

                return HttpSimulado.Imagem(pixels, "image/png");
            });

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, simulado))
            {
                var lista = Esperar(provedor.ListarCapas(1234, CancellationToken.None));

                v.Verificar("listou as três capas", lista.DeuCerto && lista.Valor!.Count == 3);
                v.Verificar("a mais votada vem primeiro", lista.Valor![0].Id == 20, lista.Valor[0].Id.ToString());
                v.Verificar("e a menos votada por último", lista.Valor[2].Id == 10, lista.Valor[2].Id.ToString());

                var url = simulado.UrlsPedidas[0];
                v.Verificar("pede a proporção 2:3", url.Contains("dimensions=600x900"), url);
                v.Verificar("pede só imagem estática (nada de APNG)", url.Contains("types=static"), url);
                v.Verificar("filtra nsfw", url.Contains("nsfw=false"), url);
                v.Verificar("filtra capa-piada", url.Contains("humor=false"), url);

                // Procura pela URL pedida em vez de indexar às cegas: BaixarCapa lista as
                // capas de novo antes de baixar, e um índice fixo quebraria à toa.
                simulado.UrlsPedidas.Clear();
                var baixada = Esperar(provedor.BaixarCapa(1234, TamanhoDeCapa.Miniatura, CancellationToken.None));

                v.Verificar("baixou a capa", baixada.DeuCerto, baixada.Mensagem);
                v.Verificar("usou o thumb da mais votada",
                    simulado.UrlsPedidas.Exists(u => u.EndsWith("t20.jpg", StringComparison.Ordinal)),
                    string.Join(" | ", simulado.UrlsPedidas.ToArray()));
                v.VerificarTexto("extensão veio do Content-Type", ".jpg", baixada.Valor!.Extensao);

                // Tamanho cheio usa a outra URL — a arte grande, não o thumb.
                simulado.UrlsPedidas.Clear();
                var cheia = Esperar(provedor.BaixarCapa(1234, TamanhoDeCapa.Cheia, CancellationToken.None));

                v.Verificar("tamanho cheio usa a url grande",
                    cheia.DeuCerto &&
                    simulado.UrlsPedidas.Exists(u => u.EndsWith("/20.jpg", StringComparison.Ordinal)) &&
                    !simulado.UrlsPedidas.Exists(u => u.EndsWith("t20.jpg", StringComparison.Ordinal)),
                    string.Join(" | ", simulado.UrlsPedidas.ToArray()));
            }
        }

        // ---- Falhas ---------------------------------------------------------------------------

        /// <summary>
        /// O ponto da fase inteira: nada disso pode virar exceção não tratada no meio de
        /// um lote de 200 jogos.
        /// </summary>
        private static void TestarFalhasDeRede(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Falhas de rede (nenhuma vira exceção)");

            ConferirFalha(v, "401 vira ChaveInvalida",
                new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.Unauthorized)), FalhaDeCapa.ChaveInvalida);

            ConferirFalha(v, "403 também vira ChaveInvalida",
                new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.Forbidden)), FalhaDeCapa.ChaveInvalida);

            ConferirFalha(v, "404 vira NaoEncontrado",
                new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.NotFound)), FalhaDeCapa.NaoEncontrado);

            ConferirFalha(v, "429 vira LimiteExcedido",
                new HttpSimulado(_ => HttpSimulado.Status((HttpStatusCode)429)), FalhaDeCapa.LimiteExcedido);

            ConferirFalha(v, "500 vira RespostaInvalida",
                new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.InternalServerError)), FalhaDeCapa.RespostaInvalida);

            ConferirFalha(v, "timeout vira TempoEsgotado", HttpSimulado.QueDemora(), FalhaDeCapa.TempoEsgotado);

            ConferirFalha(v, "sem rede vira SemRede", HttpSimulado.SemRede(), FalhaDeCapa.SemRede);

            ConferirFalha(v, "JSON quebrado vira RespostaInvalida",
                new HttpSimulado(_ => HttpSimulado.Json("{\"success\": tru")), FalhaDeCapa.RespostaInvalida);

            ConferirFalha(v, "resposta que não é objeto vira RespostaInvalida",
                new HttpSimulado(_ => HttpSimulado.Json("[1,2,3]")), FalhaDeCapa.RespostaInvalida);

            ConferirFalha(v, "envelope sem 'data' vira RespostaInvalida",
                new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true}")), FalhaDeCapa.RespostaInvalida);

            // Cancelamento no meio do lote.
            using (var cancelador = new CancellationTokenSource())
            {
                var simulado = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true,\"data\":[]}"));
                cancelador.Cancel();

                using (var provedor = new SteamGridDbProvider(ChaveFalsa, simulado))
                {
                    var busca = Esperar(provedor.BuscarJogo("qualquer", cancelador.Token));
                    v.Verificar("cancelamento vira Cancelado", busca.Falha == FalhaDeCapa.Cancelado,
                        busca.Falha.ToString());
                }
            }
        }

        private static void ConferirFalha(Verificador v, string nome, HttpSimulado simulado, FalhaDeCapa esperada)
        {
            using (var provedor = new SteamGridDbProvider(ChaveFalsa, simulado))
            {
                var busca = Esperar(provedor.BuscarJogo("NFS Carbon", CancellationToken.None));
                v.Verificar(nome, busca.Falha == esperada, $"veio {busca.Falha}");
            }
        }

        // ---- Sigilo da chave --------------------------------------------------------------------

        /// <summary>
        /// A chave vai no header Authorization e em lugar nenhum além dele. Este teste
        /// varre URL e mensagem de erro de TODOS os caminhos de falha atrás de qualquer
        /// pedaço dela — vazamento parcial também é vazamento.
        /// </summary>
        private static void TestarSigiloDaChave(Verificador v)
        {
            v.Escrever("");
            v.Escrever("A chave não vaza");

            var cenarios = new List<(string Nome, HttpSimulado Simulado)>
            {
                ("sucesso", new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true,\"data\":[{\"id\":1,\"name\":\"X\"}]}"))),
                ("401", new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.Unauthorized))),
                ("404", new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.NotFound))),
                ("500", new HttpSimulado(_ => HttpSimulado.Status(HttpStatusCode.InternalServerError))),
                ("timeout", HttpSimulado.QueDemora()),
                ("sem rede", HttpSimulado.SemRede()),
                ("json quebrado", new HttpSimulado(_ => HttpSimulado.Json("{{{")))
            };

            var mensagens = new List<string>();
            var urls = new List<string>();
            var mandouHeader = false;

            foreach (var cenario in cenarios)
            {
                using (var provedor = new SteamGridDbProvider(ChaveFalsa, cenario.Simulado))
                {
                    var busca = Esperar(provedor.BuscarJogo("NFS Carbon", CancellationToken.None));
                    mensagens.Add(busca.Mensagem);

                    var capa = Esperar(provedor.BaixarCapa(99, TamanhoDeCapa.Miniatura, CancellationToken.None));
                    mensagens.Add(capa.Mensagem);
                }

                urls.AddRange(cenario.Simulado.UrlsPedidas);

                foreach (var autorizacao in cenario.Simulado.AutorizacoesVistas)
                {
                    if (autorizacao == "Bearer " + ChaveFalsa) mandouHeader = true;
                }
            }

            v.Verificar("a chave viaja no header Authorization: Bearer", mandouHeader);

            var urlComChave = urls.Find(u => ContemPedacoDaChave(u));
            v.Verificar("nenhuma URL contém a chave (nem pedaço dela)", urlComChave is null, urlComChave);

            var mensagemComChave = mensagens.Find(ContemPedacoDaChave);
            v.Verificar("nenhuma mensagem de erro contém a chave", mensagemComChave is null, mensagemComChave);

            v.Verificar("nenhuma URL traz parâmetro de autenticação",
                urls.TrueForAll(u => u.IndexOf("key=", StringComparison.OrdinalIgnoreCase) < 0 &&
                                     u.IndexOf("token=", StringComparison.OrdinalIgnoreCase) < 0 &&
                                     u.IndexOf("auth", StringComparison.OrdinalIgnoreCase) < 0));
        }

        /// <summary>
        /// Procura a chave inteira e também qualquer trecho longo dela: vazar metade da
        /// credencial continua sendo vazar.
        /// </summary>
        private static bool ContemPedacoDaChave(string? texto)
        {
            if (string.IsNullOrEmpty(texto)) return false;

            const int menorTrecho = 8;

            for (var inicio = 0; inicio + menorTrecho <= ChaveFalsa.Length; inicio++)
            {
                var trecho = ChaveFalsa.Substring(inicio, menorTrecho);
                if (texto!.IndexOf(trecho, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // ---- Extensão --------------------------------------------------------------------------

        private static void TestarExtensaoPeloTipo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Extensão da imagem (png não pode virar jpg)");

            v.VerificarTexto("image/png", ".png", SteamGridDbProvider.ExtensaoDe("image/png", "https://cdn/x"));
            v.VerificarTexto("image/jpeg", ".jpg", SteamGridDbProvider.ExtensaoDe("image/jpeg", "https://cdn/x"));
            v.VerificarTexto("image/webp", ".webp", SteamGridDbProvider.ExtensaoDe("image/webp", "https://cdn/x"));

            v.VerificarTexto("sem Content-Type, vale a extensão da URL", ".png",
                SteamGridDbProvider.ExtensaoDe(null, "https://cdn2.steamgriddb.com/grid/abc.png"));

            v.VerificarTexto("URL com query não confunde", ".jpg",
                SteamGridDbProvider.ExtensaoDe(null, "https://cdn/abc.jpg?v=2"));

            v.VerificarTexto(".jpeg da URL vira .jpg", ".jpg",
                SteamGridDbProvider.ExtensaoDe(null, "https://cdn/abc.jpeg"));

            v.VerificarTexto("sem pista nenhuma, assume png", ".png",
                SteamGridDbProvider.ExtensaoDe(null, "https://cdn/sem-extensao"));

            v.VerificarTexto("Content-Type ganha da URL", ".png",
                SteamGridDbProvider.ExtensaoDe("image/png", "https://cdn/abc.jpg"));
        }

        // ---- Gravação --------------------------------------------------------------------------

        private static void TestarGravacaoDaCapa(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Gravação da capa e invalidação da miniatura");

            PrepararSandbox(raizReal);

            var jogo = new Jogo { Id = "nfsc", Titulo = "NFS Carbon", ExecutavelRelativo = @"Jogos\nfsc\nfsc.exe" };

            const string grids = @"{""success"":true,""data"":[
                {""id"":20,""score"":9,""url"":""https://cdn/20.png"",""thumb"":""https://cdn/t20.png""}]}";

            var pixels = PngDeUmPixel();
            var simulado = new HttpSimulado(requisicao =>
            {
                var url = requisicao.RequestUri!.ToString();

                if (url.Contains("/search/autocomplete/"))
                    return HttpSimulado.Json("{\"success\":true,\"data\":[{\"id\":777,\"name\":\"NFS Carbon\"}]}");

                if (url.Contains("/grids/game/")) return HttpSimulado.Json(grids);
                return HttpSimulado.Imagem(pixels, "image/png");
            });

            var gerenciador = new GerenciadorDeCapas();

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, simulado))
            {
                var resultado = Esperar(gerenciador.BaixarPara(jogo, provedor, CancellationToken.None));

                v.Verificar("a capa foi aplicada", resultado.DeuCerto, resultado.Mensagem);
                v.VerificarTexto("gravada com a extensão real (png)", "nfsc.png", jogo.CapaArquivo ?? "");
                v.Verificar("o arquivo existe em _mochila\\capas", File.Exists(jogo.CaminhoCapa()!));

                v.Verificar("o id do provedor foi guardado (não repete a busca)",
                    jogo.SteamGridDbId == 777, jogo.SteamGridDbId?.ToString());

                // Segunda chamada: com o id conhecido, não busca de novo.
                var pedidosAntes = simulado.Pedidos;
                Esperar(gerenciador.BaixarPara(jogo, provedor, CancellationToken.None));

                var buscouDeNovo = simulado.UrlsPedidas
                    .GetRange(pedidosAntes, simulado.Pedidos - pedidosAntes)
                    .Exists(u => u.Contains("/search/autocomplete/"));

                v.Verificar("com o id guardado, a busca não se repete", !buscouDeNovo);
            }

            // Trocar a capa por uma de outra extensão não pode deixar as duas na pasta.
            using (var imagem = GeradorDeCapa.Gerar("Capa manual", 600, 900))
                gerenciador.AplicarImagem(jogo, imagem);

            v.VerificarTexto("capa manual vira jpg", "nfsc.jpg", jogo.CapaArquivo ?? "");
            v.Verificar("a capa antiga em png foi removida",
                !File.Exists(Path.Combine(Caminhos.PastaCapas, "nfsc.png")));

            // E a miniatura velha não pode sobreviver à troca.
            var thumb = jogo.CaminhoThumbnail();
            Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            File.WriteAllBytes(thumb, new byte[] { 1, 2, 3 });

            using (var outra = GeradorDeCapa.Gerar("Outra capa", 600, 900))
                gerenciador.AplicarImagem(jogo, outra);

            v.Verificar("a miniatura em cache é descartada ao trocar a capa", !File.Exists(thumb));
        }

        // ---- Fallback local ---------------------------------------------------------------------

        private static void TestarFallbackLocal(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Fallback sem internet (pasta do jogo -> ícone do exe)");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "Need for Speed - Underground 2");
            Directory.CreateDirectory(pastaDoJogo);

            // Arquivos que NÃO são capa.
            File.WriteAllBytes(Path.Combine(pastaDoJogo, "dinput8.dll"), new byte[500]);
            File.WriteAllBytes(Path.Combine(pastaDoJogo, "botao.png"), new byte[300]);

            v.Verificar("pasta sem capa nenhuma devolve null",
                CapaLocal.Procurar(pastaDoJogo, "Need for Speed - Underground 2") is null);

            // O caso real: NFSU_icon.ico na pasta do Underground 2.
            var icone = Path.Combine(pastaDoJogo, "NFSU_icon.ico");
            using (var desenho = GeradorDeCapa.Gerar("NFSU", 256, 384))
                GeradorDeCapa.SalvarJpeg(desenho, icone);   // conteúdo não importa para a escolha

            var achado = CapaLocal.Procurar(pastaDoJogo, "Need for Speed - Underground 2");
            v.VerificarTexto("acha o ícone solto da pasta", "NFSU_icon.ico", Path.GetFileName(achado ?? ""));

            // cover.jpg tem que ganhar do ícone: é capa de verdade.
            var capa = Path.Combine(pastaDoJogo, "cover.jpg");
            using (var desenho = GeradorDeCapa.Gerar("Capa", 600, 900))
                GeradorDeCapa.SalvarJpeg(desenho, capa);

            achado = CapaLocal.Procurar(pastaDoJogo, "Need for Speed - Underground 2");
            v.VerificarTexto("cover.jpg ganha do ícone", "cover.jpg", Path.GetFileName(achado ?? ""));

            // Aplicar de ponta a ponta.
            var jogo = new Jogo
            {
                Id = "nfsu2",
                Titulo = "Need for Speed - Underground 2",
                ExecutavelRelativo = Path.Combine("Jogos", "Need for Speed - Underground 2", "SPEED2.EXE")
            };

            var gerenciador = new GerenciadorDeCapas();
            var origem = gerenciador.AplicarFallbackLocal(jogo);

            v.Verificar("o fallback usou a arte da pasta", origem == OrigemDaCapa.PastaDoJogo, origem.ToString());
            v.Verificar("e gravou a capa do jogo", File.Exists(jogo.CaminhoCapa()!));

            using (var gravada = GeradorDeCapa.AbrirSemTravarArquivo(jogo.CaminhoCapa()!))
            {
                v.Verificar("na proporção 2:3",
                    gravada.Width == GerenciadorDeCapas.LarguraDaCapa &&
                    gravada.Height == GerenciadorDeCapas.AlturaDaCapa,
                    $"{gravada.Width}x{gravada.Height}");
            }

            // Ícone do executável: usa o próprio Mochila.exe, que tem ícone de verdade.
            var doExe = ExtratorDeIcone.Extrair(System.Reflection.Assembly.GetEntryAssembly()?.Location);
            if (doExe != null)
            {
                using (doExe) v.Verificar("consegue extrair ícone de um .exe real", doExe.Width > 0);
            }
            else
            {
                v.Escrever("  (sem ícone embutido no exe de teste — extração não exercitada)");
            }

            v.Verificar("exe inexistente não quebra a extração",
                ExtratorDeIcone.Extrair(Path.Combine(sandbox, "nao-existe.exe")) is null);

            // Último degrau: card gerado, que nunca falha.
            using (var gerada = GeradorDeCapa.Gerar("Jogo Sem Nada", 600, 900))
                v.Verificar("o card gerado fecha a cadeia (nenhum card fica vazio)", gerada.Width == 600);
        }

        // ---- Apoio -------------------------------------------------------------------------------

        /// <summary>
        /// Espera a Task sem travar em deadlock. Os testes rodam fora da thread de UI,
        /// então não há contexto de sincronização para disputar.
        /// </summary>
        private static T Esperar<T>(Task<T> tarefa) => tarefa.GetAwaiter().GetResult();

        /// <summary>PNG mínimo de 1x1, para os testes terem bytes de imagem de verdade.</summary>
        private static byte[] PngDeUmPixel()
        {
            using (var bitmap = new Bitmap(1, 1))
            using (var memoria = new MemoryStream())
            {
                bitmap.SetPixel(0, 0, Color.Magenta);
                bitmap.Save(memoria, System.Drawing.Imaging.ImageFormat.Png);
                return memoria.ToArray();
            }
        }

        private static string PrepararSandbox(string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);

            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();

            return sandbox;
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
