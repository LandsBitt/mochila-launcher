using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using Mochila.Util;

namespace Mochila.UI
{
    /// <summary>
    /// Fabrica as imagens dos cards: reduz a capa de verdade para o tamanho da grade e,
    /// quando não existe capa nenhuma, desenha uma.
    ///
    /// O card gerado é o último degrau do fallback da spec — nenhum card pode ficar vazio.
    /// A cor sai do hash do título, então o mesmo jogo tem sempre a mesma cor e a grade
    /// fica reconhecível mesmo sem capa baixada.
    /// </summary>
    public static class GeradorDeCapa
    {
        /// <summary>Largura das miniaturas em cache. Cobre o card G (220 px) com folga para HiDPI.</summary>
        public const int LarguraDaMiniatura = 300;

        public static int AlturaDaMiniatura => (int)Math.Round(LarguraDaMiniatura * LayoutDaGrade.ProporcaoDaCapa);

        /// <summary>
        /// Reduz uma imagem para o tamanho pedido, preenchendo a proporção 2:3 e cortando
        /// o excesso (capa de proporção estranha não fica esticada nem com tarja).
        /// </summary>
        public static Bitmap Redimensionar(Image origem, int largura, int altura)
        {
            var destino = new Bitmap(largura, altura, PixelFormat.Format24bppRgb);

            try
            {
                using (var g = Graphics.FromImage(destino))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    var escala = Math.Max((double)largura / origem.Width, (double)altura / origem.Height);
                    var larguraEscalada = (int)Math.Ceiling(origem.Width * escala);
                    var alturaEscalada = (int)Math.Ceiling(origem.Height * escala);

                    g.DrawImage(origem,
                        new Rectangle((largura - larguraEscalada) / 2, (altura - alturaEscalada) / 2,
                                      larguraEscalada, alturaEscalada));
                }
                return destino;
            }
            catch (Exception)
            {
                destino.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Card desenhado na hora: gradiente derivado do título mais o nome centralizado.
        /// Não vai para o disco — é barato de refazer e nunca fica velho quando eu renomeio
        /// o jogo ou quando a capa de verdade chega.
        /// </summary>
        public static Bitmap Gerar(string? titulo, int largura, int altura)
        {
            var bitmap = new Bitmap(largura, altura, PixelFormat.Format24bppRgb);

            try
            {
                var cor = CorDoTitulo(titulo);

                using (var g = Graphics.FromImage(bitmap))
                {
                    var area = new Rectangle(0, 0, largura, altura);

                    using (var pincel = new LinearGradientBrush(area, Clarear(cor, 0.22f), Escurecer(cor, 0.35f), 70f))
                        g.FillRectangle(pincel, area);

                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    DesenharTitulo(g, titulo, largura, altura);
                }
                return bitmap;
            }
            catch (Exception)
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static void DesenharTitulo(Graphics g, string? titulo, int largura, int altura)
        {
            var texto = string.IsNullOrWhiteSpace(titulo) ? "?" : titulo!.Trim();

            // Título longo pede fonte menor; a caixa de texto ocupa o miolo do card.
            var tamanhoDaFonte = texto.Length > 40 ? 11f : texto.Length > 22 ? 13f : 16f;
            var caixa = new RectangleF(largura * 0.1f, altura * 0.28f, largura * 0.8f, altura * 0.44f);

            using (var fonte = new Font("Segoe UI", tamanhoDaFonte * (largura / 300f), FontStyle.Bold))
            using (var formato = new StringFormat
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisWord
                   })
            {
                // Sombra antes do texto: legível mesmo quando a cor sorteada é clara.
                using (var sombra = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                {
                    var deslocada = caixa;
                    deslocada.Offset(1.5f, 1.5f);
                    g.DrawString(texto, fonte, sombra, deslocada, formato);
                }

                using (var branco = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    g.DrawString(texto, fonte, branco, caixa, formato);
            }
        }

        /// <summary>
        /// Cor estável a partir do título: mesmo jogo, mesma cor, em qualquer PC.
        /// Hash próprio porque string.GetHashCode varia entre execuções no .NET moderno.
        /// </summary>
        public static Color CorDoTitulo(string? titulo)
        {
            var normalizado = Textos.RemoverAcentos(titulo).ToLowerInvariant();

            unchecked
            {
                uint hash = 2166136261;                 // FNV-1a
                foreach (var c in normalizado)
                {
                    hash ^= c;
                    hash *= 16777619;
                }

                var matiz = hash % 360;
                return DeHsl(matiz, 0.45, 0.34);        // saturado, mas escuro: a grade é escura
            }
        }

        private static Color Clarear(Color cor, float fracao)
            => Color.FromArgb(
                Limitar(cor.R + (255 - cor.R) * fracao),
                Limitar(cor.G + (255 - cor.G) * fracao),
                Limitar(cor.B + (255 - cor.B) * fracao));

        private static Color Escurecer(Color cor, float fracao)
            => Color.FromArgb(Limitar(cor.R * (1 - fracao)), Limitar(cor.G * (1 - fracao)), Limitar(cor.B * (1 - fracao)));

        private static int Limitar(double valor) => (int)Math.Max(0, Math.Min(255, valor));

        private static Color DeHsl(double matiz, double saturacao, double luminosidade)
        {
            var c = (1 - Math.Abs((2 * luminosidade) - 1)) * saturacao;
            var x = c * (1 - Math.Abs(((matiz / 60) % 2) - 1));
            var m = luminosidade - (c / 2);

            double r, g, b;
            if (matiz < 60) { r = c; g = x; b = 0; }
            else if (matiz < 120) { r = x; g = c; b = 0; }
            else if (matiz < 180) { r = 0; g = c; b = x; }
            else if (matiz < 240) { r = 0; g = x; b = c; }
            else if (matiz < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(Limitar((r + m) * 255), Limitar((g + m) * 255), Limitar((b + m) * 255));
        }

        // ---- Disco --------------------------------------------------------------------------

        /// <summary>Salva a miniatura em jpg de qualidade 85 — o suficiente para 300 px de largura.</summary>
        public static void SalvarJpeg(Bitmap imagem, string caminho)
        {
            var pasta = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

            var codificador = ObterCodificadorJpeg();
            if (codificador is null)
            {
                imagem.Save(caminho, ImageFormat.Jpeg);
                return;
            }

            using (var parametros = new EncoderParameters(1))
            using (var qualidade = new EncoderParameter(Encoder.Quality, 85L))
            {
                parametros.Param[0] = qualidade;
                imagem.Save(caminho, codificador, parametros);
            }
        }

        private static ImageCodecInfo? ObterCodificadorJpeg()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
            }
            return null;
        }

        /// <summary>
        /// Abre uma imagem sem manter o arquivo preso. Image.FromFile trava o arquivo até
        /// o Dispose, o que impediria trocar a capa com o launcher aberto.
        /// </summary>
        public static Bitmap AbrirSemTravarArquivo(string caminho)
        {
            using (var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var original = Image.FromStream(fluxo, useEmbeddedColorManagement: false, validateImageData: false))
            {
                return new Bitmap(original);
            }
        }
    }
}
