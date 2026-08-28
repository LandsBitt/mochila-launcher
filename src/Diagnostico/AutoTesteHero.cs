// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Mochila.Capas;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação da fase 14: hero art, logo, fundo desfocado e cor de acento.
    ///
    /// Como na fase 6, nenhum teste toca a internet — tudo passa por
    /// <see cref="HttpSimulado"/>. O que se prova aqui é o que a spec grifa: o logo
    /// sobrevive como PNG com alfa, o borrão é gerado uma vez só, e capa escura não pode
    /// produzir um acento invisível.
    /// </summary>
    public static class AutoTesteHero
    {
        private const string NomePastaSandbox = "_autoteste-hero-tmp";

        private const string ChaveFalsa = "chave-secreta-de-teste-hero999";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarEnderecosDosEndpoints(v);
                TestarSucessoFalsoNaoQuebra(v);
                TestarLogoMantemAlfa(v, raizReal);
                TestarHeroPrecisaDeIdentidade(v, raizReal);
                TestarBorraoGeradoUmaVez(v, raizReal);
                TestarBorraoInvalidadoComHeroNovo(v, raizReal);
                TestarLimpezaDeCacheLevaOsBorroes(v, raizReal);
                TestarCorDominante(v);
                TestarPisoDeContraste(v);
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

        // ---- Os endpoints irmãos --------------------------------------------------------------

        /// <summary>
        /// A tabela dos três endpoints. É o coração da fase: errar a linha do hero ou do
        /// logo não quebra nada visivelmente — só devolve lista vazia para sempre, e isso
        /// pareceria "esse jogo não tem arte".
        /// </summary>
        private static void TestarEnderecosDosEndpoints(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Os endereços de /grids, /heroes e /logos");

            var http = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true,\"data\":[]}"));

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, http, "https://exemplo/api"))
            {
                provedor.ListarArte(42, TipoDeArte.Capa, CancellationToken.None).GetAwaiter().GetResult();
                provedor.ListarArte(42, TipoDeArte.Hero, CancellationToken.None).GetAwaiter().GetResult();
                provedor.ListarArte(42, TipoDeArte.Logo, CancellationToken.None).GetAwaiter().GetResult();
            }

            v.Verificar("três pedidos, um por tipo", http.Pedidos == 3, http.Pedidos.ToString());
            if (http.Pedidos < 3) return;

            var capa = http.UrlsPedidas[0];
            var hero = http.UrlsPedidas[1];
            var logo = http.UrlsPedidas[2];

            v.Verificar("a capa continua em /grids/game/42", capa.Contains("/grids/game/42"), capa);
            v.Verificar("e continua pedindo 600x900", capa.Contains("dimensions=600x900"), capa);

            v.Verificar("o hero vai para /heroes/game/42", hero.Contains("/heroes/game/42"), hero);
            v.Verificar("com as dimensões de hero da spec", hero.Contains("dimensions=1920x620"), hero);

            v.Verificar("o logo vai para /logos/game/42", logo.Contains("/logos/game/42"), logo);
            v.Verificar("o logo pede SÓ png (jpg mataria o alfa)",
                logo.Contains("mimes=image/png") && !logo.Contains("image/jpeg"), logo);
            v.Verificar("e o logo NÃO filtra por dimensão (logo não tem proporção fixa)",
                !logo.Contains("dimensions="), logo);

            foreach (var url in new[] { capa, hero, logo })
            {
                v.Verificar("nsfw e humor ficam de fora",
                    url.Contains("nsfw=false") && url.Contains("humor=false"), url);
                v.Verificar("e nada de animado", url.Contains("types=static"), url);
            }

            // A regra da fase 6 continua valendo nos endpoints novos.
            var vazouNaUrl = false;
            foreach (var url in http.UrlsPedidas)
            {
                if (url.Contains(ChaveFalsa)) vazouNaUrl = true;
            }

            v.Verificar("a chave NÃO aparece em nenhuma das URLs novas", !vazouNaUrl);
            v.Verificar("ela viaja no header, como na fase 6",
                http.AutorizacoesVistas.Count == 3 && http.AutorizacoesVistas[0].Contains(ChaveFalsa));
        }

        /// <summary>
        /// <c>success: false</c> é a resposta que a spec manda testar por nome. Ela não pode
        /// virar exceção: no meio de um lote, exceção deixa a biblioteca pela metade.
        /// </summary>
        private static void TestarSucessoFalsoNaoQuebra(Verificador v)
        {
            v.Escrever("");
            v.Escrever("success:false -> sem hero, sem exceção");

            var http = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":false,\"errors\":[\"nada\"]}"));

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, http, "https://exemplo/api"))
            {
                var hero = provedor.BaixarArte(7, TipoDeArte.Hero, TamanhoDeCapa.Miniatura, CancellationToken.None)
                                   .GetAwaiter().GetResult();

                v.Verificar("não deu certo", !hero.DeuCerto);
                v.Verificar("e o motivo é 'não encontrado', não 'resposta inválida'",
                    hero.Falha == FalhaDeCapa.NaoEncontrado, hero.Falha.ToString());
                // A mensagem aqui é a do ENVELOPE, não a de "esse jogo não tem hero" — e
                // tem que ser mesmo: serviço que respondeu success:false é serviço com
                // problema, não jogo sem arte. Trocar uma frase pela outra mandaria eu
                // procurar arte à mão para um jogo que talvez tenha, e desistir dele.
                v.Verificar("a mensagem é a de falha do serviço, não a de 'jogo sem arte'",
                    hero.Mensagem.IndexOf("não conseguiu atender", StringComparison.OrdinalIgnoreCase) >= 0,
                    hero.Mensagem);
                v.Verificar("e sem a chave dentro", !hero.Mensagem.Contains(ChaveFalsa));
            }

            // Lista vazia com success:true é o outro jeito de "esse jogo não tem".
            var vazio = new HttpSimulado(_ => HttpSimulado.Json("{\"success\":true,\"data\":[]}"));

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, vazio, "https://exemplo/api"))
            {
                var logo = provedor.BaixarArte(7, TipoDeArte.Logo, TamanhoDeCapa.Miniatura, CancellationToken.None)
                                   .GetAwaiter().GetResult();

                v.Verificar("lista vazia também é 'não encontrado'",
                    !logo.DeuCerto && logo.Falha == FalhaDeCapa.NaoEncontrado);
                v.Verificar("e a mensagem do logo é a do logo",
                    logo.Mensagem.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0, logo.Mensagem);
            }
        }

        // ---- Gravação -------------------------------------------------------------------------

        /// <summary>
        /// O teste que a spec pede com todas as letras: <b>logo png mantém alfa</b>. Um
        /// logo recodificado para jpg vira um retângulo com fundo, que é o oposto do que ele
        /// serve para fazer — e a falha é invisível até alguém abrir a tela de detalhes.
        /// </summary>
        private static void TestarLogoMantemAlfa(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("O logo chega ao disco como PNG, com o alfa intacto");

            PrepararSandbox(raizReal);

            var pngComAlfa = PngComTransparencia();
            var http = RespondendoCom(pngComAlfa, "image/png");

            var jogo = new Jogo { Id = "lego", Titulo = "LEGO Star Wars", SteamGridDbId = 99 };
            var capas = new GerenciadorDeCapas();

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, http, "https://exemplo/api"))
            {
                var resultado = capas.BaixarArtePara(jogo, provedor, TipoDeArte.Logo, CancellationToken.None)
                                     .GetAwaiter().GetResult();

                v.Verificar("baixou", resultado.DeuCerto, resultado.Mensagem);
            }

            v.Verificar("o campo logoArquivo foi preenchido", !string.IsNullOrEmpty(jogo.LogoArquivo), jogo.LogoArquivo);
            v.Verificar("com o sufixo _logo, para conviver com a capa do mesmo jogo",
                jogo.LogoArquivo?.StartsWith("lego_logo", StringComparison.Ordinal) == true, jogo.LogoArquivo);
            v.Verificar("e extensão .png — NUNCA .jpg",
                jogo.LogoArquivo?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true, jogo.LogoArquivo);

            var caminho = jogo.CaminhoLogo();
            v.Verificar("o arquivo existe", caminho is not null && File.Exists(caminho));
            if (caminho is null || !File.Exists(caminho)) return;

            // A prova de verdade: reabrir e conferir que o pixel transparente continua
            // transparente. Extensão certa com bytes recodificados passaria no teste acima.
            using (var lido = GeradorDeCapa.AbrirSemTravarArquivo(caminho))
            {
                v.Verificar("o formato em disco tem canal alfa",
                    Image.IsAlphaPixelFormat(lido.PixelFormat), lido.PixelFormat.ToString());

                using (var bitmap = new Bitmap(lido))
                {
                    v.Verificar("E O PIXEL TRANSPARENTE CONTINUA TRANSPARENTE",
                        bitmap.GetPixel(0, 0).A == 0, bitmap.GetPixel(0, 0).A.ToString());
                    v.Verificar("enquanto o pixel opaco continua opaco",
                        bitmap.GetPixel(1, 1).A == 255, bitmap.GetPixel(1, 1).A.ToString());
                }
            }
        }

        /// <summary>
        /// Hero e logo só saem com o jogo já identificado no serviço. Sem isso, o launcher
        /// estaria dando uma segunda opinião sobre qual jogo é este — e hero de um jogo com
        /// capa de outro é pior que hero nenhum.
        /// </summary>
        private static void TestarHeroPrecisaDeIdentidade(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Sem steamGridDbId, a arte de fundo nem tenta");

            PrepararSandbox(raizReal);

            var http = RespondendoCom(PngComTransparencia(), "image/png");
            var semId = new Jogo { Id = "sem-id", Titulo = "Jogo Qualquer" };
            var capas = new GerenciadorDeCapas();

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, http, "https://exemplo/api"))
            {
                var resultado = capas.BaixarArtePara(semId, provedor, TipoDeArte.Hero, CancellationToken.None)
                                     .GetAwaiter().GetResult();

                v.Verificar("recusa", !resultado.DeuCerto);
                v.Verificar("por 'não encontrado'", resultado.Falha == FalhaDeCapa.NaoEncontrado);
            }

            v.Verificar("E NÃO TOCOU NA REDE", http.Pedidos == 0, http.Pedidos.ToString());
            v.Verificar("nem gravou nada no jogo", semId.HeroArquivo is null);
        }

        // ---- Fundo desfocado ------------------------------------------------------------------

        /// <summary>
        /// "Blur gerado uma vez e reaproveitado" — o item da spec que existe porque o
        /// contrário (desfocar a cada frame) transformaria arrastar a janela numa
        /// apresentação de slides.
        /// </summary>
        private static void TestarBorraoGeradoUmaVez(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("O borrão é gerado uma vez e reaproveitado");

            PrepararSandbox(raizReal);

            var jogo = ComHeroEmDisco("moonscars");
            var borrado = jogo.CaminhoHeroDesfocado();

            v.Verificar("ainda não existe borrão em cache", !File.Exists(borrado));

            using (var primeiro = FundoDesfocado.Obter(jogo))
            {
                v.Verificar("a primeira chamada devolve imagem", primeiro is not null);
                v.Verificar("com a largura da spec (~64 px)",
                    primeiro?.Width == FundoDesfocado.LarguraDoBorrao, primeiro?.Width.ToString());
                v.Verificar("e proporção preservada (hero 400x129 -> 64x21)",
                    primeiro?.Height == 21, primeiro?.Height.ToString());
            }

            v.Verificar("E O ARQUIVO FICOU EM CACHE", File.Exists(borrado));
            if (!File.Exists(borrado)) return;

            v.Verificar("no lugar da spec: cache\\<id>_hero_blur.jpg",
                borrado.EndsWith(Path.Combine("cache", "moonscars_hero_blur.jpg"), StringComparison.OrdinalIgnoreCase),
                borrado);

            // A prova do reaproveitamento: apagar o HERO e pedir de novo. Se a segunda
            // chamada regerasse, ela falharia — não há mais de onde gerar.
            File.Delete(jogo.CaminhoHero()!);

            using (var segundo = FundoDesfocado.Obter(jogo))
            {
                v.Verificar("A SEGUNDA CHAMADA VEM DO CACHE (o hero já nem existe mais)",
                    segundo is not null);
                v.Verificar("e tem o mesmo tamanho", segundo?.Width == FundoDesfocado.LarguraDoBorrao);
            }

            // E sem hero nem cache, a resposta é "não tem" — nunca exceção.
            var semArte = new Jogo { Id = "sem-arte", Titulo = "Sem Arte" };
            using (var nada = FundoDesfocado.Obter(semArte))
                v.Verificar("jogo sem hero devolve null, sem estourar", nada is null);
        }

        /// <summary>
        /// "É invalidado quando o hero troca". Sem isto, o arquivo velho tem o nome certo e
        /// nada avisaria que ele está desatualizado — a tela desfocaria a arte anterior para
        /// sempre.
        /// </summary>
        private static void TestarBorraoInvalidadoComHeroNovo(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Hero novo joga fora o borrão velho");

            PrepararSandbox(raizReal);

            var jogo = ComHeroEmDisco("nfs");
            jogo.SteamGridDbId = 1234;

            using (var _ = FundoDesfocado.Obter(jogo)) { }

            v.Verificar("o borrão existe antes da troca", File.Exists(jogo.CaminhoHeroDesfocado()));

            // Um hero novo chegando pelo caminho normal (download).
            var http = RespondendoCom(JpegDeTeste(200, 65, Color.DarkGreen), "image/jpeg");
            var capas = new GerenciadorDeCapas();

            using (var provedor = new SteamGridDbProvider(ChaveFalsa, http, "https://exemplo/api"))
            {
                var resultado = capas.BaixarArtePara(jogo, provedor, TipoDeArte.Hero, CancellationToken.None)
                                     .GetAwaiter().GetResult();

                v.Verificar("o hero novo foi gravado", resultado.DeuCerto, resultado.Mensagem);
            }

            v.Verificar("E O BORRÃO VELHO SUMIU", !File.Exists(jogo.CaminhoHeroDesfocado()));
            v.Verificar("o campo heroArquivo aponta para o arquivo novo",
                jogo.HeroArquivo?.StartsWith("nfs_hero", StringComparison.Ordinal) == true, jogo.HeroArquivo);
            v.Verificar("com a extensão do Content-Type, não um .png chutado",
                jogo.HeroArquivo?.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true, jogo.HeroArquivo);

            // E o próximo pedido regera, agora a partir do hero novo.
            using (var novo = FundoDesfocado.Obter(jogo))
                v.Verificar("o borrão é regerado sozinho no desenho seguinte", novo is not null);

            v.Verificar("e volta para o cache", File.Exists(jogo.CaminhoHeroDesfocado()));
        }

        /// <summary>
        /// "Limpar cache remove os blurs". Barato de provar e é o tipo de coisa que quebra
        /// quando alguém decide guardar o borrão em <c>capas\</c> "porque é arte".
        /// </summary>
        private static void TestarLimpezaDeCacheLevaOsBorroes(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Limpar o cache leva os borrões junto");

            PrepararSandbox(raizReal);

            var jogo = ComHeroEmDisco("bioshock");
            using (var _ = FundoDesfocado.Obter(jogo)) { }

            v.Verificar("o borrão está no cache", File.Exists(jogo.CaminhoHeroDesfocado()));
            v.Verificar("e o hero continua em capas\\ (que a limpeza NÃO toca)",
                File.Exists(jogo.CaminhoHero()!));

            // A mesma varredura que o botão de Configurações faz.
            foreach (var arquivo in Directory.GetFiles(Caminhos.PastaCache)) File.Delete(arquivo);

            v.Verificar("O BORRÃO SOME NA LIMPEZA", !File.Exists(jogo.CaminhoHeroDesfocado()));
            v.Verificar("mas o hero continua lá — ele é arte, não cache",
                File.Exists(jogo.CaminhoHero()!));

            using (var regerado = FundoDesfocado.Obter(jogo))
                v.Verificar("e o borrão volta sozinho no desenho seguinte", regerado is not null);
        }

        // ---- Cor de acento --------------------------------------------------------------------

        /// <summary>
        /// A cor dominante não é a média, e este teste é a prova: uma imagem metade
        /// vermelha e metade azul tem MÉDIA roxa-acinzentada — uma cor que não existe em
        /// pixel nenhum dela.
        /// </summary>
        private static void TestarCorDominante(Verificador v)
        {
            v.Escrever("");
            v.Escrever("A cor de acento é a dominante, não a média");

            // 70% vermelho, 30% azul. A média daria roxo; a dominante tem que dar vermelho.
            using (var imagem = new Bitmap(100, 100))
            {
                using (var g = Graphics.FromImage(imagem))
                {
                    g.Clear(Color.FromArgb(200, 30, 30));
                    using (var pincel = new SolidBrush(Color.FromArgb(30, 30, 200)))
                        g.FillRectangle(pincel, 0, 70, 100, 30);
                }

                var dominante = CorDominante.Dominante(imagem);

                v.Verificar("achou uma dominante", dominante is not null);
                if (dominante is { } cor)
                {
                    v.Verificar("e ela é o VERMELHO da maioria, não a média roxa",
                        cor.R > 150 && cor.B < 90, $"{cor.R},{cor.G},{cor.B}");
                }
            }

            // Cinza puro não tem cor para votar: cai na reserva em vez de inventar matiz.
            using (var cinza = new Bitmap(50, 50))
            {
                using (var g = Graphics.FromImage(cinza)) g.Clear(Color.FromArgb(90, 90, 90));

                v.Verificar("imagem sem saturação nenhuma não tem dominante",
                    CorDominante.Dominante(cinza) is null);

                var reserva = Color.FromArgb(1, 2, 3);
                v.Verificar("e o acento dela cai na reserva do tema",
                    CorDominante.De(cinza, Tema.Fundo, reserva) == reserva);
            }

            v.Verificar("imagem nula não estoura e devolve a reserva",
                CorDominante.De(null, Tema.Fundo, Tema.Acento) == Tema.Acento);
        }

        /// <summary>
        /// "Capa escura resulta em acento acima do piso de contraste" — o item da spec que
        /// existe porque o azul-marinho dominante de um jogo espacial daria um acento
        /// invisível sobre o fundo quase preto do tema.
        /// </summary>
        private static void TestarPisoDeContraste(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Capa escura não faz o acento sumir");

            // Azul-marinho bem escuro: saturado o bastante para vencer o histograma, e
            // escuro o bastante para ser ilegível sobre o fundo do tema.
            var marinho = Color.FromArgb(14, 20, 60);

            v.Verificar("o azul-marinho cru NÃO passa no piso de contraste",
                CorDominante.Contraste(marinho, Tema.Fundo) < 3.0,
                CorDominante.Contraste(marinho, Tema.Fundo).ToString("F2"));

            using (var escura = new Bitmap(60, 90))
            {
                using (var g = Graphics.FromImage(escura)) g.Clear(marinho);

                var acento = CorDominante.De(escura, Tema.Fundo, Tema.Acento);

                v.Verificar("MAS O ACENTO SAI ACIMA DO PISO",
                    CorDominante.Contraste(acento, Tema.Fundo) >= 3.0,
                    CorDominante.Contraste(acento, Tema.Fundo).ToString("F2"));

                v.Verificar("e ele ainda é azulado — clarear preserva o matiz do jogo",
                    acento.B > acento.R && acento.B > acento.G, $"{acento.R},{acento.G},{acento.B}");
            }

            // Cor que já passa não pode ser mexida: clarear à toa apagaria a identidade.
            var laranjaVivo = Color.FromArgb(255, 150, 40);
            v.Verificar("cor que já contrasta passa intacta",
                CorDominante.GarantirContraste(laranjaVivo, Tema.Fundo, Tema.Acento) == laranjaVivo);

            v.Verificar("contraste de uma cor com ela mesma é 1",
                Math.Abs(CorDominante.Contraste(Tema.Fundo, Tema.Fundo) - 1.0) < 0.001);
        }

        // ---- Apoio ------------------------------------------------------------------------------

        /// <summary>Um jogo com hero de 400x129 já gravado em capas\.</summary>
        private static Jogo ComHeroEmDisco(string id)
        {
            var jogo = new Jogo { Id = id, Titulo = id, HeroArquivo = id + "_hero.jpg" };

            using (var hero = JpegBitmap(400, 129, Color.FromArgb(180, 60, 40)))
                GeradorDeCapa.SalvarJpeg(hero, jogo.CaminhoHero()!);

            return jogo;
        }

        /// <summary>
        /// Responde a listagem com uma arte só e devolve os bytes pedidos no download.
        /// Duas respostas diferentes na mesma simulação: a primeira é JSON, a segunda é a
        /// imagem — que é exatamente a sequência que o provedor faz.
        /// </summary>
        private static HttpSimulado RespondendoCom(byte[] bytes, string tipo)
            => new HttpSimulado(requisicao =>
            {
                var url = requisicao.RequestUri?.AbsoluteUri ?? "";

                if (url.Contains("/game/"))
                {
                    return HttpSimulado.Json(
                        "{\"success\":true,\"data\":[{\"id\":1,\"score\":9," +
                        "\"url\":\"https://exemplo/arte-cheia\",\"thumb\":\"https://exemplo/arte-thumb\"}]}");
                }

                return HttpSimulado.Imagem(bytes, tipo);
            });

        /// <summary>PNG 4x4 com o canto transparente e o resto opaco.</summary>
        private static byte[] PngComTransparencia()
        {
            using (var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
            using (var memoria = new MemoryStream())
            {
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 4; x++) bitmap.SetPixel(x, y, Color.FromArgb(255, 220, 40, 40));
                }

                bitmap.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));

                bitmap.Save(memoria, ImageFormat.Png);
                return memoria.ToArray();
            }
        }

        private static Bitmap JpegBitmap(int largura, int altura, Color cor)
        {
            var bitmap = new Bitmap(largura, altura);
            using (var g = Graphics.FromImage(bitmap)) g.Clear(cor);
            return bitmap;
        }

        private static byte[] JpegDeTeste(int largura, int altura, Color cor)
        {
            using (var bitmap = JpegBitmap(largura, altura, cor))
            using (var memoria = new MemoryStream())
            {
                bitmap.Save(memoria, ImageFormat.Jpeg);
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
