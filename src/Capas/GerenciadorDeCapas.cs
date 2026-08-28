using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Capas
{
    /// <summary>De onde a capa veio. Aparece no rodapé quando eu quero conferir.</summary>
    public enum OrigemDaCapa
    {
        Nenhuma = 0,
        Online,
        ArquivoLocal,
        PastaDoJogo,
        IconeDoExecutavel,
        AreaDeTransferencia
    }

    /// <summary>
    /// Grava capa no lugar certo e mantém a biblioteca coerente com o disco.
    ///
    /// Regra que atravessa tudo: quem troca a capa também tem que invalidar a miniatura
    /// em cache, senão a grade continua desenhando a arte velha até alguém apagar o
    /// _mochila\cache na mão.
    ///
    /// A cadeia de fallback da spec (arquivo solto na pasta -> ícone do exe -> card
    /// gerado) vive aqui, e a parte online entra por <see cref="ICapaProvider"/> — trocar
    /// o provedor não encosta nesta classe.
    /// </summary>
    public sealed class GerenciadorDeCapas
    {
        private readonly CacheDeMiniaturas? _miniaturas;

        public GerenciadorDeCapas(CacheDeMiniaturas? miniaturas = null)
        {
            _miniaturas = miniaturas;
        }

        // ---- Online ---------------------------------------------------------------------------

        /// <summary>
        /// Busca e aplica a capa de um jogo. Com o provedor não configurado, devolve
        /// <see cref="FalhaDeCapa.SemChave"/> sem tocar na rede — chave vazia é caminho
        /// manual, não erro.
        /// </summary>
        public async Task<ResultadoDeCapa<string>> BaixarPara(Jogo jogo, ICapaProvider provedor,
                                                              CancellationToken cancelamento)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));
            if (provedor is null) throw new ArgumentNullException(nameof(provedor));

            if (!provedor.Configurado)
                return ResultadoDeCapa<string>.Erro(FalhaDeCapa.SemChave,
                    "Busca online desligada (sem chave configurada).");

            // Id já conhecido: pula a busca e vai direto às capas.
            var id = jogo.SteamGridDbId;

            if (id is null)
            {
                var busca = await provedor.BuscarJogo(jogo.Titulo, cancelamento).ConfigureAwait(false);
                if (!busca.DeuCerto) return ResultadoDeCapa<string>.Erro(busca.Falha, busca.Mensagem);

                id = busca.Valor![0].Id;
            }

            var capa = await provedor.BaixarCapa(id.Value, TamanhoDeCapa.Miniatura, cancelamento)
                                     .ConfigureAwait(false);

            if (!capa.DeuCerto) return ResultadoDeCapa<string>.Erro(capa.Falha, capa.Mensagem);

            try
            {
                var arquivo = GravarBytes(jogo, capa.Valor!.Bytes, capa.Valor.Extensao);

                // Guardado para não repetir a busca na próxima vez.
                jogo.SteamGridDbId = id;

                return ResultadoDeCapa<string>.Certo(arquivo);
            }
            catch (Exception erro)
            {
                return ResultadoDeCapa<string>.Erro(FalhaDeCapa.RespostaInvalida,
                    $"Não consegui gravar a capa: {erro.Message}");
            }
        }

        /// <summary>
        /// Busca e aplica o hero ou o logo de um jogo (fase 14).
        ///
        /// <b>Nunca busca o jogo pelo título.</b> Só age com <see cref="Jogo.SteamGridDbId"/>
        /// já conhecido, que é o que a capa deixa gravado. Sem ele, isto devolveria uma
        /// segunda opinião sobre qual jogo é este — e hero de um jogo com capa de outro é
        /// pior que hero nenhum.
        ///
        /// Jogo sem hero ou sem logo no acervo é <see cref="FalhaDeCapa.NaoEncontrado"/> e
        /// não é erro: é o normal para jogo antigo, que é metade deste acervo.
        /// </summary>
        public async Task<ResultadoDeCapa<string>> BaixarArtePara(Jogo jogo, ICapaProvider provedor,
                                                                  TipoDeArte tipo, CancellationToken cancelamento)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));
            if (provedor is null) throw new ArgumentNullException(nameof(provedor));

            if (tipo == TipoDeArte.Capa)
                return await BaixarPara(jogo, provedor, cancelamento).ConfigureAwait(false);

            if (!provedor.Configurado)
                return ResultadoDeCapa<string>.Erro(FalhaDeCapa.SemChave,
                    "Busca online desligada (sem chave configurada).");

            if (jogo.SteamGridDbId is not { } id)
                return ResultadoDeCapa<string>.Erro(FalhaDeCapa.NaoEncontrado,
                    "Este jogo ainda não foi identificado no serviço — baixe a capa primeiro.");

            // Miniatura, como a spec manda: o thumb do hero já tem largura de sobra para o
            // fundo desfocado, e a resolução cheia de 1920x620 custaria banda e memória para
            // acabar reduzida a 64 px de largura.
            var arte = await provedor.BaixarArte(id, tipo, TamanhoDeCapa.Miniatura, cancelamento)
                                     .ConfigureAwait(false);

            if (!arte.DeuCerto) return ResultadoDeCapa<string>.Erro(arte.Falha, arte.Mensagem);

            try
            {
                return ResultadoDeCapa<string>.Certo(
                    GravarArte(jogo, tipo, arte.Valor!.Bytes, arte.Valor.Extensao));
            }
            catch (Exception erro)
            {
                return ResultadoDeCapa<string>.Erro(FalhaDeCapa.RespostaInvalida,
                    $"Não consegui gravar a arte: {erro.Message}");
            }
        }

        // ---- Manual ---------------------------------------------------------------------------

        /// <summary>Aplica uma imagem de arquivo (arrastada, escolhida ou colada).</summary>
        public string AplicarDeArquivo(Jogo jogo, string caminhoDaImagem)
        {
            using (var imagem = CapaLocal.Carregar(caminhoDaImagem))
            {
                if (imagem is null)
                    throw new InvalidOperationException($"Não consegui abrir \"{Path.GetFileName(caminhoDaImagem)}\" como imagem.");

                return AplicarImagem(jogo, imagem);
            }
        }

        /// <summary>
        /// Grava um bitmap como capa do jogo, já na proporção 2:3. Sempre jpg: a imagem
        /// veio de mim (arquivo, área de transferência, ícone), não do provedor, e aqui
        /// não há formato original a preservar.
        /// </summary>
        public string AplicarImagem(Jogo jogo, Image imagem)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));
            if (imagem is null) throw new ArgumentNullException(nameof(imagem));

            Caminhos.GarantirEstrutura();

            var nome = jogo.Id + ".jpg";
            var destino = Path.Combine(Caminhos.PastaCapas, nome);

            using (var capa = GeradorDeCapa.Redimensionar(imagem, LarguraDaCapa, AlturaDaCapa))
                GeradorDeCapa.SalvarJpeg(capa, destino);

            ApagarOutrasExtensoes(jogo.Id, nome);
            DefinirCapa(jogo, nome);

            return nome;
        }

        // ---- Fallback local --------------------------------------------------------------------

        /// <summary>
        /// Cadeia da spec, sem rede: imagem solta na pasta do jogo, senão ícone do
        /// executável. O terceiro degrau (card desenhado) não grava arquivo — a grade
        /// desenha na hora, e assim ele nunca fica velho quando a capa de verdade chegar.
        /// </summary>
        public OrigemDaCapa AplicarFallbackLocal(Jogo jogo)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));

            var daPasta = CapaLocal.Procurar(jogo.PastaDoJogo(), jogo.Titulo);
            if (daPasta != null)
            {
                using (var imagem = CapaLocal.Carregar(daPasta))
                {
                    if (imagem != null)
                    {
                        AplicarImagem(jogo, imagem);
                        return OrigemDaCapa.PastaDoJogo;
                    }
                }
            }

            using (var icone = ExtratorDeIcone.Extrair(jogo.CaminhoExecutavel()))
            {
                if (icone != null)
                {
                    AplicarImagem(jogo, icone);
                    return OrigemDaCapa.IconeDoExecutavel;
                }
            }

            return OrigemDaCapa.Nenhuma;
        }

        // ---- Apoio ------------------------------------------------------------------------------

        /// <summary>Capa em tamanho cheio, como a spec pede.</summary>
        public const int LarguraDaCapa = 600;

        public const int AlturaDaCapa = 900;

        /// <summary>
        /// Grava os bytes como vieram, preservando o formato original (PNG continua PNG).
        /// Recodificar para jpg perderia qualidade sem ganhar nada.
        /// </summary>
        private string GravarBytes(Jogo jogo, byte[] bytes, string extensao)
        {
            Caminhos.GarantirEstrutura();

            var nome = jogo.Id + extensao;
            var destino = Path.Combine(Caminhos.PastaCapas, nome);

            File.WriteAllBytes(destino, bytes);

            ApagarOutrasExtensoes(jogo.Id, nome);
            DefinirCapa(jogo, nome);

            return nome;
        }

        /// <summary>
        /// Grava hero ou logo em <c>capas\</c>, com sufixo no nome para conviver com a capa
        /// do mesmo jogo (<c>&lt;id&gt;.jpg</c>, <c>&lt;id&gt;_hero.jpg</c>,
        /// <c>&lt;id&gt;_logo.png</c>).
        ///
        /// Os bytes vão como vieram: recodificar o logo mataria o alfa, que é a razão de o
        /// arquivo existir.
        /// </summary>
        private string GravarArte(Jogo jogo, TipoDeArte tipo, byte[] bytes, string extensao)
        {
            Caminhos.GarantirEstrutura();

            var sufixo = tipo == TipoDeArte.Hero ? "_hero" : "_logo";

            // O logo só é pedido em PNG, mas o Content-Type é de quem responde, não de quem
            // pergunta: se vier outra coisa, o arquivo leva a extensão de verdade em vez de
            // um .png mentiroso que o GDI+ abriria e o resto do mundo não.
            var nome = jogo.Id + sufixo + extensao;
            var destino = Path.Combine(Caminhos.PastaCapas, nome);

            File.WriteAllBytes(destino, bytes);

            ApagarOutrasExtensoes(jogo.Id + sufixo, nome);

            if (tipo == TipoDeArte.Hero)
            {
                jogo.HeroArquivo = nome;
                InvalidarHeroDesfocado(jogo);
            }
            else
            {
                jogo.LogoArquivo = nome;
            }

            return nome;
        }

        /// <summary>
        /// Joga fora o fundo desfocado derivado do hero antigo.
        ///
        /// É o mesmo cuidado que <see cref="DefinirCapa"/> tem com a miniatura, pelo mesmo
        /// motivo: sem isto, trocar o hero deixaria a tela de detalhes desfocando a arte
        /// anterior até alguém limpar o cache na mão — e o arquivo velho tem o nome certo,
        /// então nada avisaria que ele está desatualizado.
        /// </summary>
        public void InvalidarHeroDesfocado(Jogo jogo)
        {
            try
            {
                var borrado = jogo.CaminhoHeroDesfocado();
                if (File.Exists(borrado)) File.Delete(borrado);
            }
            catch (Exception)
            {
                // Preso por um desenho em andamento: a próxima limpeza de cache resolve.
            }
        }

        /// <summary>
        /// Troca a capa e joga fora a miniatura antiga. Sem isso, a grade continuaria
        /// mostrando a arte anterior.
        /// </summary>
        private void DefinirCapa(Jogo jogo, string nomeDoArquivo)
        {
            jogo.CapaArquivo = nomeDoArquivo;

            try
            {
                var thumb = jogo.CaminhoThumbnail();
                if (File.Exists(thumb)) File.Delete(thumb);
            }
            catch (Exception)
            {
                // Thumb preso por outra coisa: a data de modificação da capa nova já força
                // a regeneração no próximo desenho.
            }

            _miniaturas?.Invalidar(jogo.Id);
        }

        /// <summary>
        /// Um jogo tem uma capa só. Trocando de png para jpg (ou o contrário), o arquivo
        /// antigo vira lixo que confunde na hora de olhar a pasta.
        /// </summary>
        private static void ApagarOutrasExtensoes(string id, string nomeAtual)
        {
            foreach (var extensao in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" })
            {
                var nome = id + extensao;
                if (string.Equals(nome, nomeAtual, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var caminho = Path.Combine(Caminhos.PastaCapas, nome);
                    if (File.Exists(caminho)) File.Delete(caminho);
                }
                catch (Exception)
                {
                    // Arquivo preso: não é motivo para falhar a troca de capa.
                }
            }
        }
    }
}
