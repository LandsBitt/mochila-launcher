using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher.UI
{
    /// <summary>
    /// O gamepad que identifica o launcher: barra de título, Alt+Tab, barra de tarefas,
    /// Explorer e a marca d'água da grade vazia.
    ///
    /// É desenhado por código, não carregado de um arquivo, por dois motivos. O primeiro
    /// é portabilidade: nada de asset solto ao lado do exe. O segundo é nitidez — um PNG
    /// único esticado para 16 px vira borrão, enquanto aqui cada tamanho é redesenhado com
    /// o nível de detalhe que cabe nele.
    ///
    /// O <c>.ico</c> que vai para o recurso do exe (o que o Explorer mostra) sai deste
    /// mesmo código, pelo <c>--gerar-icone</c> da build de Debug: um desenho só, sem
    /// versão de arquivo que envelhece separada do código.
    /// </summary>
    public static class IconeDoLauncher
    {
        /// <summary>Tamanhos do arquivo .ico do exe. 256 é o que o Explorer usa em ícone grande.</summary>
        public static readonly int[] TamanhosDoArquivo = { 16, 20, 24, 32, 48, 64, 128, 256 };

        /// <summary>Tamanhos que a janela precisa: título (16), barra de tarefas (32), Alt+Tab (48).</summary>
        private static readonly int[] TamanhosDaJanela = { 16, 20, 24, 32, 48, 64 };

        private static Icon? _daJanela;
        private static bool _tentouCriar;

        /// <summary>
        /// O ícone das janelas, criado uma vez e compartilhado. Null se o Windows recusar
        /// os bytes — sem ícone é feio, mas não é motivo para o launcher não abrir.
        /// </summary>
        public static Icon? DaJanela
        {
            get
            {
                if (_tentouCriar) return _daJanela;
                _tentouCriar = true;

                try
                {
                    using (var fluxo = new MemoryStream(MontarIcoSemCompressao(TamanhosDaJanela)))
                        _daJanela = new Icon(fluxo);
                }
                catch (Exception)
                {
                    _daJanela = null;
                }

                return _daJanela;
            }
        }

        // ---- Desenho -------------------------------------------------------------------------

        /// <summary>O gamepad em fundo transparente, no tamanho pedido.</summary>
        public static Bitmap Desenhar(int tamanho)
        {
            var bitmap = new Bitmap(tamanho, tamanho, PixelFormat.Format32bppArgb);

            try
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    Desenhar(g, new RectangleF(0, 0, tamanho, tamanho));
                }
                return bitmap;
            }
            catch (Exception)
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Desenha o gamepad dentro de uma área qualquer. A área é quadrada por
        /// convenção — o controle ocupa a largura inteira e fica centralizado na altura.
        /// </summary>
        public static void Desenhar(Graphics g, RectangleF area, int transparencia = 255)
        {
            var lado = Math.Min(area.Width, area.Height);
            if (lado < 4) return;

            // Tudo abaixo está em fração do lado: um desenho só serve de 16 a 256 px.
            float X(float f) => area.X + ((area.Width - lado) / 2) + (f * lado);
            float Y(float f) => area.Y + ((area.Height - lado) / 2) + (f * lado);

            var detalhado = lado >= 24;

            using (var corpo = MontarCorpo(X, Y))
            {
                var caixa = corpo.GetBounds();

                using (var pincel = new LinearGradientBrush(
                           RectangleF.Inflate(caixa, 1, 1),
                           Color.FromArgb(transparencia, 138, 176, 255),
                           Color.FromArgb(transparencia, 118, 92, 240),
                           55f))
                {
                    g.FillPath(pincel, corpo);
                }

                // Contorno escuro: o ícone também aparece sobre fundo claro (Explorer).
                using (var caneta = new Pen(Color.FromArgb(transparencia * 60 / 255, 12, 14, 32), Math.Max(1f, lado / 64f)))
                    g.DrawPath(caneta, corpo);
            }

            var tinta = Color.FromArgb(transparencia, 22, 24, 40);

            DesenharDirecional(g, X, Y, lado, tinta);
            DesenharBotoes(g, X, Y, lado, tinta, detalhado);
        }

        /// <summary>
        /// A silhueta: dois punhos redondos ligados por um meio afunilado. A cintura — em
        /// cima e embaixo — é o que faz a forma ler como "controle" e não como "cápsula",
        /// e é o único detalhe que ainda sobrevive a 16 px.
        /// </summary>
        private static GraphicsPath MontarCorpo(Func<float, float> x, Func<float, float> y)
        {
            var caminho = new GraphicsPath();

            var largura = x(0.46f) - x(0f);
            var altura = y(0.50f) - y(0f);

            // Punho esquerdo (meia-lua, de baixo para cima), cintura de cima, punho
            // direito (de cima para baixo) e cintura de baixo fechando a figura.
            //
            // As duas cinturas são de propósito desiguais: a de cima é funda (é onde ficam
            // os gatilhos num controle de verdade) e a de baixo quase reta. Simétricas,
            // a silhueta virava gravata-borboleta.
            caminho.AddArc(x(0.01f), y(0.25f), largura, altura, 90, 180);
            caminho.AddBezier(x(0.24f), y(0.25f), x(0.38f), y(0.38f), x(0.62f), y(0.38f), x(0.76f), y(0.25f));
            caminho.AddArc(x(0.53f), y(0.25f), largura, altura, 270, 180);
            caminho.AddBezier(x(0.76f), y(0.75f), x(0.62f), y(0.70f), x(0.38f), y(0.70f), x(0.24f), y(0.75f));
            caminho.CloseFigure();

            return caminho;
        }

        /// <summary>Cruz direcional no punho esquerdo.</summary>
        private static void DesenharDirecional(Graphics g, Func<float, float> x, Func<float, float> y, float lado, Color tinta)
        {
            var centroX = x(0.24f);
            var centroY = y(0.50f);
            var braco = lado * 0.105f;
            var grossura = Math.Max(1.5f, lado * 0.072f);

            using (var pincel = new SolidBrush(tinta))
            {
                g.FillRectangle(pincel, centroX - braco, centroY - (grossura / 2), braco * 2, grossura);
                g.FillRectangle(pincel, centroX - (grossura / 2), centroY - braco, grossura, braco * 2);
            }
        }

        /// <summary>Botões de ação no punho direito. Em 16 px só cabem dois.</summary>
        private static void DesenharBotoes(Graphics g, Func<float, float> x, Func<float, float> y, float lado,
                                           Color tinta, bool detalhado)
        {
            var centroX = x(0.76f);
            var centroY = y(0.50f);
            var distancia = lado * (detalhado ? 0.095f : 0.075f);
            var raio = lado * (detalhado ? 0.048f : 0.055f);

            using (var pincel = new SolidBrush(tinta))
            {
                if (detalhado)
                {
                    Ponto(g, pincel, centroX, centroY - distancia, raio);
                    Ponto(g, pincel, centroX, centroY + distancia, raio);
                }

                Ponto(g, pincel, centroX - distancia, centroY + (detalhado ? 0 : distancia), raio);
                Ponto(g, pincel, centroX + distancia, centroY - (detalhado ? 0 : distancia), raio);
            }
        }

        private static void Ponto(Graphics g, Brush pincel, float centroX, float centroY, float raio)
            => g.FillEllipse(pincel, centroX - raio, centroY - raio, raio * 2, raio * 2);

        // ---- Arquivo .ico ---------------------------------------------------------------------

        /// <summary>
        /// Monta um .ico com um PNG por tamanho. PNG dentro de ICO é o formato que o
        /// Windows usa desde o Vista, e é o que mantém o recurso em dezenas de KB em vez
        /// de centenas — só o quadro de 256 px, sem compressão, seria 256 KB.
        ///
        /// É o formato do ARQUIVO, gerado uma vez em tempo de desenvolvimento. O ícone da
        /// janela usa <see cref="MontarIcoSemCompressao"/>, que não paga o codec.
        /// </summary>
        public static byte[] MontarIco(IReadOnlyList<int> tamanhos)
        {
            var imagens = new List<byte[]>(tamanhos.Count);

            foreach (var tamanho in tamanhos)
            {
                using (var bitmap = Desenhar(tamanho))
                using (var memoria = new MemoryStream())
                {
                    bitmap.Save(memoria, ImageFormat.Png);
                    imagens.Add(memoria.ToArray());
                }
            }

            return Empacotar(tamanhos, imagens);
        }

        /// <summary>
        /// O mesmo .ico, com os quadros em BMP cru em vez de PNG.
        ///
        /// É o que a janela usa: sem compressão, montar o ícone não depende do codec de
        /// PNG em tempo de execução — é escrever bytes. O custo é buffer temporário, e nos
        /// seis tamanhos da janela ele dá ~35 KB, que somem no primeiro GC.
        /// </summary>
        public static byte[] MontarIcoSemCompressao(IReadOnlyList<int> tamanhos)
        {
            var imagens = new List<byte[]>(tamanhos.Count);

            foreach (var tamanho in tamanhos)
            {
                using (var bitmap = Desenhar(tamanho))
                    imagens.Add(MontarDib(bitmap));
            }

            return Empacotar(tamanhos, imagens);
        }

        /// <summary>
        /// Um quadro no formato que o ICO herdou do BMP: cabeçalho com a altura dobrada
        /// (a metade de baixo seria a máscara), pixels BGRA de baixo para cima e a máscara
        /// zerada — quem resolve a transparência é o canal alfa, não ela.
        /// </summary>
        private static byte[] MontarDib(Bitmap bitmap)
        {
            var largura = bitmap.Width;
            var altura = bitmap.Height;
            var bytesDaMascara = ((largura + 31) / 32) * 4 * altura;

            using (var saida = new MemoryStream())
            using (var escritor = new BinaryWriter(saida))
            {
                escritor.Write(40);                      // tamanho do BITMAPINFOHEADER
                escritor.Write(largura);
                escritor.Write(altura * 2);              // imagem + máscara
                escritor.Write((short)1);                // planos
                escritor.Write((short)32);               // bits por pixel
                escritor.Write(0);                       // sem compressão
                escritor.Write(largura * altura * 4);
                escritor.Write(0);                       // resolução horizontal (indiferente)
                escritor.Write(0);                       // resolução vertical
                escritor.Write(0);                       // cores usadas
                escritor.Write(0);                       // cores importantes

                var dados = bitmap.LockBits(new Rectangle(0, 0, largura, altura),
                                            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var linha = new byte[largura * 4];

                    // De baixo para cima: é a ordem que o formato pede.
                    for (var y = altura - 1; y >= 0; y--)
                    {
                        Marshal.Copy(dados.Scan0 + (y * dados.Stride), linha, 0, linha.Length);
                        escritor.Write(linha);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(dados);
                }

                escritor.Write(new byte[bytesDaMascara]);

                escritor.Flush();
                return saida.ToArray();
            }
        }

        /// <summary>Cabeçalho do .ico e os quadros já codificados, na ordem dos tamanhos.</summary>
        private static byte[] Empacotar(IReadOnlyList<int> tamanhos, List<byte[]> imagens)
        {
            using (var saida = new MemoryStream())
            using (var escritor = new BinaryWriter(saida))
            {
                escritor.Write((short)0);                    // reservado
                escritor.Write((short)1);                    // 1 = ícone
                escritor.Write((short)tamanhos.Count);

                var deslocamento = 6 + (16 * tamanhos.Count);

                for (var i = 0; i < tamanhos.Count; i++)
                {
                    // 256 px é gravado como 0: o campo tem um byte só.
                    var medida = (byte)(tamanhos[i] >= 256 ? 0 : tamanhos[i]);

                    escritor.Write(medida);                  // largura
                    escritor.Write(medida);                  // altura
                    escritor.Write((byte)0);                 // cores da paleta (0 = sem paleta)
                    escritor.Write((byte)0);                 // reservado
                    escritor.Write((short)1);                // planos
                    escritor.Write((short)32);               // bits por pixel
                    escritor.Write(imagens[i].Length);
                    escritor.Write(deslocamento);

                    deslocamento += imagens[i].Length;
                }

                foreach (var imagem in imagens) escritor.Write(imagem);

                escritor.Flush();
                return saida.ToArray();
            }
        }
    }
}
