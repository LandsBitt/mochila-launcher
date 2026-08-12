using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Dados;
using Launcher.Modelo;
using Launcher.UI;

namespace Launcher.Capas
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
    /// _launcher\cache na mão.
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
