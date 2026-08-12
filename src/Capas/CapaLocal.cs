using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Launcher.Util;

namespace Launcher.Capas
{
    /// <summary>
    /// Capa sem internet: o que já está na pasta do jogo.
    ///
    /// É o degrau que mais acerta em acervo antigo. Jogo de 2005 costuma ter um
    /// "NFSU_icon.ico" ou um "cover.jpg" largado na pasta, quase sempre com arte melhor
    /// que o ícone embutido no exe — e sem custar um byte de rede.
    /// </summary>
    public static class CapaLocal
    {
        /// <summary>Extensões que o GDI+ abre e que valem como capa.</summary>
        private static readonly string[] ExtensoesDeImagem = { ".png", ".jpg", ".jpeg", ".bmp", ".ico" };

        /// <summary>
        /// Nomes que denunciam arte de capa. Ordem = prioridade: "cover" e "box" são
        /// capa de verdade; "icon" e "logo" são o consolo.
        /// </summary>
        private static readonly string[] PalavrasDeCapa = { "cover", "box", "capa", "art", "poster", "logo", "icon" };

        /// <summary>Profundidade de busca. Capa solta mora na raiz ou logo abaixo.</summary>
        private const int ProfundidadeMaxima = 1;

        /// <summary>
        /// Procura uma imagem aproveitável na pasta do jogo. Devolve o caminho ou null.
        /// A escolha é a de maior placar: nome sugestivo, nome parecido com o da pasta e
        /// tamanho de arquivo desempatam.
        /// </summary>
        public static string? Procurar(string? pastaDoJogo, string? nomeDoJogo)
        {
            if (string.IsNullOrEmpty(pastaDoJogo) || !Directory.Exists(pastaDoJogo)) return null;

            var tokensDoJogo = Tokens.Dividir(nomeDoJogo ?? Path.GetFileName(pastaDoJogo!.TrimEnd('\\', '/')));

            string? melhor = null;
            var melhorPlacar = int.MinValue;

            foreach (var arquivo in ListarImagens(pastaDoJogo!, 0))
            {
                var placar = Pontuar(arquivo, tokensDoJogo);
                if (placar <= 0 || placar <= melhorPlacar) continue;

                melhorPlacar = placar;
                melhor = arquivo;
            }

            return melhor;
        }

        private static IEnumerable<string> ListarImagens(string pasta, int profundidade)
        {
            string[] arquivos;
            try
            {
                arquivos = Directory.GetFiles(pasta);
            }
            catch (Exception)
            {
                yield break;   // pasta protegida: não é motivo para derrubar nada
            }

            foreach (var arquivo in arquivos)
            {
                if (EhImagem(arquivo)) yield return arquivo;
            }

            if (profundidade >= ProfundidadeMaxima) yield break;

            string[] subpastas;
            try
            {
                subpastas = Directory.GetDirectories(pasta);
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var subpasta in subpastas)
            {
                foreach (var achado in ListarImagens(subpasta, profundidade + 1)) yield return achado;
            }
        }

        private static bool EhImagem(string caminho)
        {
            var extensao = Path.GetExtension(caminho).ToLowerInvariant();

            foreach (var aceita in ExtensoesDeImagem)
            {
                if (extensao == aceita) return true;
            }
            return false;
        }

        /// <summary>
        /// Placar do candidato. Zero ou menos = não serve.
        /// </summary>
        private static int Pontuar(string caminho, IReadOnlyList<string> tokensDoJogo)
        {
            var nome = Path.GetFileNameWithoutExtension(caminho);
            var compactado = Tokens.Compactar(nome);
            var placar = 0;

            // Nome sugestivo. "cover" vale mais que "icon": é capa de verdade.
            for (var i = 0; i < PalavrasDeCapa.Length; i++)
            {
                if (compactado.IndexOf(PalavrasDeCapa[i], StringComparison.Ordinal) < 0) continue;

                placar += 40 - (i * 4);
                break;
            }

            // Nome parecido com o do jogo ("NFSU_icon.ico" na pasta do Underground).
            foreach (var token in Tokens.Dividir(nome))
            {
                foreach (var doJogo in tokensDoJogo)
                {
                    if (string.Equals(token, doJogo, StringComparison.OrdinalIgnoreCase))
                    {
                        placar += 15;
                        break;
                    }
                }
            }

            try
            {
                var informacao = new FileInfo(caminho);

                // Arte de capa tem algum peso. O piso é baixo de propósito: .ico de jogo
                // antigo tem 4-15 KB, e punir isso descartaria justamente o "NFSU_icon.ico"
                // que a spec cita como exemplo bom. Abaixo de 2 KB é sprite de interface.
                if (informacao.Length >= 40 * 1024) placar += 12;
                else if (informacao.Length < 2 * 1024) placar -= 12;

                // .ico costuma ser ícone pequeno, mas em jogo antigo é o que existe.
                if (string.Equals(Path.GetExtension(caminho), ".ico", StringComparison.OrdinalIgnoreCase))
                    placar -= 6;
            }
            catch (Exception)
            {
                return 0;
            }

            return placar;
        }

        /// <summary>
        /// Carrega a imagem escolhida como bitmap, sem prender o arquivo. Devolve null se
        /// o arquivo estiver corrompido — o que acontece com .ico antigo.
        /// </summary>
        public static Bitmap? Carregar(string caminho)
        {
            try
            {
                if (string.Equals(Path.GetExtension(caminho), ".ico", StringComparison.OrdinalIgnoreCase))
                    return CarregarIcone(caminho);

                return UI.GeradorDeCapa.AbrirSemTravarArquivo(caminho);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Pega o maior quadro de um .ico — eles trazem vários tamanhos.</summary>
        private static Bitmap? CarregarIcone(string caminho)
        {
            try
            {
                using (var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var icone = new Icon(fluxo, 256, 256))
                {
                    return icone.ToBitmap();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
