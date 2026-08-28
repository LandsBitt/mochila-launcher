using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// O fundo desfocado que a tela de detalhes desenha atrás de tudo (fase 14).
    ///
    /// <b>O borrão não é convolução, e não é feito a cada frame.</b> As duas coisas juntas
    /// são a razão de esta classe existir: um gaussiano de verdade sobre 1920x620, repetido
    /// a cada <c>OnPaint</c>, transformaria arrastar a janela numa apresentação de slides.
    ///
    /// O truque é o mais velho que existe e o resultado é indistinguível neste tamanho:
    /// reduzir o hero a <see cref="LarguraDoBorrao"/> pixels de largura joga fora todo o
    /// detalhe, e desenhar essa miniatura <b>esticada</b> com interpolação bilinear devolve
    /// exatamente o degradê suave que se quer. O custo do desfoque vira o custo de um
    /// <c>DrawImage</c> de imagem minúscula.
    ///
    /// O arquivo em cache guarda a versão <b>reduzida</b>, não a esticada — são 2 KB em vez
    /// de centenas, e quem estica é o desenho.
    /// </summary>
    public static class FundoDesfocado
    {
        /// <summary>
        /// Largura da miniatura borrada. ~64 px é o número da spec, e ele tem motivo: menos
        /// que isso apaga a divisão de cor entre metades da arte (céu contra chão vira uma
        /// mancha só); mais que isso começa a deixar forma reconhecível aparecer atrás do
        /// texto e atrapalhar a leitura.
        /// </summary>
        public const int LarguraDoBorrao = 64;

        /// <summary>
        /// A miniatura borrada deste jogo, gerando-a se ainda não existir em disco.
        ///
        /// Devolve null quando o jogo não tem hero — que é o caso comum e não é erro. Nunca
        /// lança: fundo é enfeite, e enfeite não pode ser motivo de a tela não abrir.
        ///
        /// <b>Quem recebe é dono do bitmap</b> e tem que dar Dispose, como no resto da UI
        /// deste projeto.
        /// </summary>
        public static Bitmap? Obter(Jogo? jogo)
        {
            if (jogo is null) return null;

            var borrado = jogo.CaminhoHeroDesfocado();

            try
            {
                if (File.Exists(borrado)) return GeradorDeCapa.AbrirSemTravarArquivo(borrado);
            }
            catch (Exception)
            {
                // Cache ilegível (gravação interrompida, disco com problema): regera abaixo.
            }

            return Gerar(jogo);
        }

        /// <summary>
        /// Reduz o hero e grava o resultado no cache. Separado de <see cref="Obter"/> para o
        /// <c>--autoteste</c> conseguir provar que a segunda chamada NÃO passa por aqui.
        /// </summary>
        public static Bitmap? Gerar(Jogo? jogo)
        {
            if (jogo?.CaminhoHero() is not { } hero) return null;

            try
            {
                if (!File.Exists(hero)) return null;

                using (var arte = GeradorDeCapa.AbrirSemTravarArquivo(hero))
                {
                    if (arte.Width <= 0 || arte.Height <= 0) return null;

                    var altura = Math.Max(1, (int)Math.Round(
                        LarguraDoBorrao * (arte.Height / (double)arte.Width)));

                    var pequeno = new Bitmap(LarguraDoBorrao, altura);

                    using (var g = Graphics.FromImage(pequeno))
                    {
                        // HighQualityBicubic na REDUÇÃO é o que faz cada pixel do resultado
                        // ser a média de muitos da origem — ou seja, é aqui que o borrão
                        // acontece de verdade. NearestNeighbor devolveria uma amostragem
                        // pontilhada, e esticá-la daria quadradão em vez de degradê.
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(arte, new Rectangle(0, 0, LarguraDoBorrao, altura));
                    }

                    try
                    {
                        Dados.Caminhos.GarantirEstrutura();
                        GeradorDeCapa.SalvarJpeg(pequeno, jogo.CaminhoHeroDesfocado());
                    }
                    catch (Exception)
                    {
                        // Sem poder gravar o cache, o fundo ainda aparece — só é refeito na
                        // próxima abertura. HD somente-leitura não pode custar o enfeite.
                    }

                    return pequeno;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Desenha o fundo esticado na área pedida, com um véu escuro por cima.
        ///
        /// <b>O véu não é opcional.</b> Sem ele o texto branco da ficha cai em cima de uma
        /// arte que pode ser clara em metade da tela, e a legibilidade passa a depender de
        /// qual jogo está selecionado — que é justamente o tipo de bug que só aparece no
        /// acervo de outra pessoa.
        /// </summary>
        public static void Desenhar(Graphics g, Bitmap? fundo, Rectangle area, Color veu, int opacidadeDoVeu)
        {
            if (g is null || area.Width <= 0 || area.Height <= 0) return;

            if (fundo is not null)
            {
                var interpolacaoAnterior = g.InterpolationMode;

                // Bilinear na AMPLIAÇÃO: é ela que transforma os 64 px num degradê contínuo.
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;

                // Sem isto o GDI+ desenha a borda da imagem esticada com meio pixel de
                // transparência, e aparece uma linha clara na moldura do painel.
                using (var atributos = new System.Drawing.Imaging.ImageAttributes())
                {
                    atributos.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(fundo, area, 0, 0, fundo.Width, fundo.Height, GraphicsUnit.Pixel, atributos);
                }

                g.InterpolationMode = interpolacaoAnterior;
            }

            var alfa = Math.Max(0, Math.Min(255, opacidadeDoVeu));
            if (alfa == 0) return;

            using (var pincel = new SolidBrush(Color.FromArgb(alfa, veu)))
                g.FillRectangle(pincel, area);
        }
    }
}
