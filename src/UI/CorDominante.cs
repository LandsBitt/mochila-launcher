using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mochila.UI
{
    /// <summary>
    /// A cor de acento tirada da arte de um jogo (fase 14).
    ///
    /// <b>Não é a cor média, e isso é regra da spec, não gosto.</b> Média de imagem
    /// colorida converge para marrom-acinzentado: some o vermelho de um jogo de corrida e
    /// o verde de um de floresta no mesmo bege sem graça, porque a média puxa tudo para o
    /// centro do cubo RGB. O que se quer é a cor que mais aparece, não a que fica no meio.
    ///
    /// O algoritmo, na ordem em que importa:
    ///
    /// 1. <b>Descarta pixel de baixa saturação.</b> Arte de jogo é cheia de preto, cinza e
    ///    branco — céu, sombra, HUD, letra. Esses pixels são a maioria e não dizem nada
    ///    sobre a identidade do jogo; deixá-los votar é a mesma armadilha da média.
    /// 2. <b>Histograma em balde grosso</b> (5 bits por canal, 32 níveis). Balde fino faria
    ///    cada gradiente virar mil cores com um voto cada e nenhuma vencer.
    /// 3. <b>Média DENTRO do balde vencedor</b>, não o centro dele. O balde diz qual região
    ///    do cubo ganhou; a média dos pixels reais que caíram lá dá a cor exata.
    /// 4. <b>Piso de contraste contra <see cref="Tema.Fundo"/>.</b> Capa escura não pode
    ///    fazer o acento sumir: o azul-marinho dominante de um jogo espacial daria um acento
    ///    invisível sobre um fundo quase preto.
    /// </summary>
    public static class CorDominante
    {
        /// <summary>
        /// Abaixo disto o pixel é cinza demais para representar o jogo. 0.25 em saturação
        /// HSV deixa passar cor lavada (pastel, arte desbotada de jogo antigo) e barra o
        /// cinza-com-um-toque-de-azul que domina tela escura.
        /// </summary>
        private const double SaturacaoMinima = 0.25;

        /// <summary>
        /// Pixel quase preto ou quase branco não vota nem quando passa na saturação: em
        /// valor muito baixo o ruído de compressão inventa matiz que não existe na arte.
        /// </summary>
        private const double ValorMinimo = 0.12;

        private const double ValorMaximo = 0.97;

        /// <summary>
        /// Razão de contraste mínima contra o fundo. 3.0 é o piso da WCAG para elemento
        /// gráfico e texto grande — que é exatamente o que o acento é aqui (borda, barra,
        /// contorno de seleção), e não corpo de texto.
        /// </summary>
        private const double ContrasteMinimo = 3.0;

        /// <summary>
        /// Quantos pixels no máximo entram na conta. Amostragem por passo fixo: hero de
        /// 1920x620 tem 1,2 milhão de pixels e a cor dominante não muda por olhar um a cada
        /// N. O teto existe para esta função nunca aparecer num profile.
        /// </summary>
        private const int AmostraMaxima = 20000;

        /// <summary>
        /// A cor de acento desta imagem, já garantida legível sobre <see cref="Tema.Fundo"/>.
        ///
        /// Imagem sem nenhuma cor saturada (arte em preto e branco, screenshot de menu) cai
        /// no acento do tema — que é o comportamento certo: inventar cor a partir de cinza
        /// seria devolver ruído com cara de decisão.
        /// </summary>
        public static Color De(Image? imagem) => De(imagem, Tema.Fundo, Tema.Acento);

        /// <summary>
        /// A mesma coisa, com fundo e reserva explícitos. Existe separado para o
        /// <c>--autoteste</c> poder provar o piso de contraste sem depender da paleta.
        /// </summary>
        public static Color De(Image? imagem, Color fundo, Color reserva)
        {
            if (imagem is null) return reserva;

            var bruta = Dominante(imagem);
            if (bruta is not { } cor) return reserva;

            return GarantirContraste(cor, fundo, reserva);
        }

        /// <summary>
        /// A cor que mais aparece entre os pixels que têm cor. null quando não sobrou
        /// nenhum — quem chama decide o que fazer com isso.
        /// </summary>
        public static Color? Dominante(Image? imagem)
        {
            if (imagem is null) return null;

            // 32 níveis por canal: 32768 baldes, um int cada. Cabe de sobra e evita alocar
            // dicionário por imagem num caminho que roda a cada troca de jogo.
            const int Niveis = 32;
            var votos = new int[Niveis * Niveis * Niveis];
            var somaR = new long[votos.Length];
            var somaG = new long[votos.Length];
            var somaB = new long[votos.Length];

            var contou = false;

            PercorrerPixels(imagem, (r, g, b) =>
            {
                if (!TemCor(r, g, b)) return;

                var balde = ((r >> 3) * Niveis + (g >> 3)) * Niveis + (b >> 3);

                votos[balde]++;
                somaR[balde] += r;
                somaG[balde] += g;
                somaB[balde] += b;
                contou = true;
            });

            if (!contou) return null;

            var vencedor = 0;
            for (var i = 1; i < votos.Length; i++)
            {
                if (votos[i] > votos[vencedor]) vencedor = i;
            }

            if (votos[vencedor] == 0) return null;

            // A média DENTRO do balde, não o centro dele: o balde é grosso de propósito, e
            // devolver o centro daria uma cor que talvez não exista em pixel nenhum da arte.
            var total = votos[vencedor];
            return Color.FromArgb((int)(somaR[vencedor] / total),
                                  (int)(somaG[vencedor] / total),
                                  (int)(somaB[vencedor] / total));
        }

        /// <summary>
        /// Empurra a cor até ela atingir o piso de contraste contra o fundo. Se nem o
        /// extremo chegar lá, devolve a reserva.
        ///
        /// <b>A direção sai do fundo, não é fixa.</b> Sobre o tema escuro a saída é clarear
        /// (escurecer afastaria do piso); sobre o tema claro da fase 17 é exatamente o
        /// contrário. Quando isto só sabia clarear, uma capa escura no tema claro empurrava
        /// o acento para o branco — e branco sobre branco nunca alcança piso nenhum, então
        /// todo jogo caía na reserva e a fase 14 sumia da tela clara.
        /// </summary>
        public static Color GarantirContraste(Color cor, Color fundo, Color reserva)
        {
            if (Contraste(cor, fundo) >= ContrasteMinimo) return cor;

            var alvo = Luminancia(fundo) > 0.5 ? 0 : 255;

            // Passos de 6% em direção ao extremo. Vinte passos chegam nele; parar no
            // primeiro que serve preserva o máximo possível do matiz original.
            for (var passo = 1; passo <= 20; passo++)
            {
                var fator = passo * 0.06;
                var ajustada = Color.FromArgb(
                    Misturar(cor.R, alvo, fator),
                    Misturar(cor.G, alvo, fator),
                    Misturar(cor.B, alvo, fator));

                if (Contraste(ajustada, fundo) >= ContrasteMinimo) return ajustada;
            }

            return reserva;
        }

        private static int Misturar(int de, int para, double fator)
            => (int)Math.Round(de + ((para - de) * fator));

        /// <summary>
        /// Razão de contraste da WCAG: (L1 + 0.05) / (L2 + 0.05), com L a luminância
        /// relativa. Vai de 1 (idênticas) a 21 (preto contra branco).
        /// </summary>
        public static double Contraste(Color a, Color b)
        {
            var la = Luminancia(a);
            var lb = Luminancia(b);

            var claro = Math.Max(la, lb);
            var escuro = Math.Min(la, lb);

            return (claro + 0.05) / (escuro + 0.05);
        }

        /// <summary>
        /// Luminância relativa da WCAG, com a linearização do sRGB. A conta ponderada
        /// (0.2126 / 0.7152 / 0.0722) existe porque o olho enxerga verde muito mais que
        /// azul — média simples diria que azul puro e verde puro brilham igual.
        /// </summary>
        private static double Luminancia(Color cor)
            => (0.2126 * Linear(cor.R)) + (0.7152 * Linear(cor.G)) + (0.0722 * Linear(cor.B));

        private static double Linear(int canal)
        {
            var v = canal / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// Saturação e valor do HSV, sem converter a cor inteira: só o que decide o voto.
        /// <c>Color.GetSaturation</c> devolve a saturação do HSL, que é outra conta e
        /// classificaria cor clara lavada como saturadíssima.
        /// </summary>
        private static bool TemCor(int r, int g, int b)
        {
            var maior = Math.Max(r, Math.Max(g, b));
            var menor = Math.Min(r, Math.Min(g, b));

            var valor = maior / 255.0;
            if (valor < ValorMinimo || valor > ValorMaximo) return false;

            if (maior == 0) return false;

            var saturacao = (maior - menor) / (double)maior;
            return saturacao >= SaturacaoMinima;
        }

        /// <summary>
        /// Varre os pixels por <c>LockBits</c>, com passo para respeitar o teto de amostra.
        ///
        /// <c>GetPixel</c> num hero de 1,2 milhão de pixels é medido em segundos — ele
        /// trava e destrava o bitmap a cada chamada. Aqui é uma trava só, leitura direta da
        /// memória, e o formato é forçado para 32bpp para não haver caso de paleta.
        /// </summary>
        private static void PercorrerPixels(Image imagem, Action<int, int, int> aoLerPixel)
        {
            using (var bitmap = new Bitmap(imagem.Width, imagem.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                    g.DrawImage(imagem, 0, 0, imagem.Width, imagem.Height);

                var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var dados = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                try
                {
                    // O passo vale para os DOIS eixos: pular só coluna ainda copiaria todas
                    // as linhas, e a cópia da linha é a parte cara deste laço.
                    var totalDePixels = bitmap.Width * (double)bitmap.Height;
                    var passo = (int)Math.Max(1, Math.Ceiling(Math.Sqrt(totalDePixels / AmostraMaxima)));

                    var linha = new byte[Math.Abs(dados.Stride)];

                    for (var y = 0; y < bitmap.Height; y += passo)
                    {
                        // IntPtr.Add, e não "+": o operador de soma em IntPtr não existe no
                        // net48, que é o alvo fixo deste projeto.
                        Marshal.Copy(IntPtr.Add(dados.Scan0, y * dados.Stride), linha, 0, linha.Length);

                        for (var x = 0; x < bitmap.Width; x += passo)
                        {
                            var i = x * 4;

                            // BGRA em memória, e o alfa manda: pixel transparente de logo
                            // não tem cor nenhuma para votar.
                            if (linha[i + 3] < 128) continue;

                            aoLerPixel(linha[i + 2], linha[i + 1], linha[i]);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(dados);
                }
            }
        }
    }
}
