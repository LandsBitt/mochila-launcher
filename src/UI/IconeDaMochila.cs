using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Mochila.UI
{
    /// <summary>
    /// A mochila que identifica o launcher: barra de título, Alt+Tab, barra de tarefas,
    /// Explorer e a marca d'água da grade vazia.
    ///
    /// A arte é um PNG de 256 px embutido NO EXE como recurso, não um arquivo ao lado
    /// dele. É o que mantém a regra de portabilidade de pé — o Mochila.exe continua
    /// sendo um arquivo só — sem abrir mão de um desenho que nenhum código de GDI+
    /// desenharia à mão.
    ///
    /// O PNG sai de <c>assets\mochila-fonte.png</c> pelo <c>assets\preparar-icone.py</c>,
    /// que tira o fundo branco e recorta. Regerar depois de trocar a arte.
    ///
    /// O <c>.ico</c> que vai para o recurso do exe (o que o Explorer mostra) sai desta
    /// mesma imagem, pelo <c>--gerar-icone</c> da build de Debug: uma arte só, sem versão
    /// de arquivo que envelhece separada do resto.
    /// </summary>
    public static class IconeDaMochila
    {
        /// <summary>Tamanhos do arquivo .ico do exe. 256 é o que o Explorer usa em ícone grande.</summary>
        public static readonly int[] TamanhosDoArquivo = { 16, 20, 24, 32, 48, 64, 128, 256 };

        /// <summary>Tamanhos que a janela precisa: título (16), barra de tarefas (32), Alt+Tab (48).</summary>
        private static readonly int[] TamanhosDaJanela = { 16, 20, 24, 32, 48, 64 };

        /// <summary>Nome fixado no csproj (LogicalName), para não depender de como o MSBuild monta o caminho.</summary>
        private const string RecursoDaArte = "Mochila.mochila.png";

        private static Bitmap? _arte;
        private static bool _tentouCarregarArte;

        private static Icon? _daJanela;
        private static bool _tentouCriarIcone;

        /// <summary>
        /// A arte em 256 px, decodificada uma vez e compartilhada. Null se o recurso
        /// sumir do exe — ficar sem ícone é feio, mas não é motivo para o launcher
        /// não abrir.
        /// </summary>
        private static Bitmap? Arte
        {
            get
            {
                if (_tentouCarregarArte) return _arte;
                _tentouCarregarArte = true;

                try
                {
                    using (var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream(RecursoDaArte))
                    {
                        if (fluxo is null) return null;

                        // O Bitmap fica dono do fluxo enquanto vive; copiar para a memória
                        // primeiro deixa o recurso fechado e a imagem independente dele.
                        using (var memoria = new MemoryStream())
                        {
                            fluxo.CopyTo(memoria);
                            memoria.Position = 0;

                            using (var doArquivo = new Bitmap(memoria))
                                _arte = new Bitmap(doArquivo);
                        }
                    }
                }
                catch (Exception)
                {
                    _arte = null;
                }

                return _arte;
            }
        }

        /// <summary>
        /// O ícone das janelas, criado uma vez e compartilhado. Null se o Windows recusar
        /// os bytes — sem ícone é feio, mas não é motivo para o launcher não abrir.
        /// </summary>
        public static Icon? DaJanela
        {
            get
            {
                if (_tentouCriarIcone) return _daJanela;
                _tentouCriarIcone = true;

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

        /// <summary>A mochila em fundo transparente, no tamanho pedido.</summary>
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
        /// Desenha a mochila dentro de uma área qualquer. A área é quadrada por
        /// convenção — a arte ocupa o lado menor e fica centralizada no outro.
        /// </summary>
        public static void Desenhar(Graphics g, RectangleF area, int transparencia = 255)
        {
            var lado = Math.Min(area.Width, area.Height);
            if (lado < 4) return;

            if (Arte is not { } arte) return;

            var destino = new RectangleF(
                area.X + ((area.Width - lado) / 2),
                area.Y + ((area.Height - lado) / 2),
                lado, lado);

            var interpolacaoAnterior = g.InterpolationMode;
            var deslocamentoAnterior = g.PixelOffsetMode;

            // A arte é sempre REDUZIDA (256 px de origem), e bicúbico de alta qualidade é
            // o que segura o contorno legível em 16 px. HighQuality no PixelOffset evita
            // que a redução coma meia coluna de pixel na borda.
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            try
            {
                // TileFlipXY: sem isso o bicúbico amostra o "nada" além da borda da imagem
                // e devolve uma moldura semitransparente em volta do desenho.
                using (var atributos = new ImageAttributes())
                {
                    atributos.SetWrapMode(WrapMode.TileFlipXY);

                    if (transparencia < 255)
                    {
                        var matriz = new ColorMatrix { Matrix33 = transparencia / 255f };
                        atributos.SetColorMatrix(matriz);
                    }

                    g.DrawImage(arte,
                        new[]
                        {
                            new PointF(destino.Left, destino.Top),
                            new PointF(destino.Right, destino.Top),
                            new PointF(destino.Left, destino.Bottom),
                        },
                        new RectangleF(0, 0, arte.Width, arte.Height),
                        GraphicsUnit.Pixel,
                        atributos);
                }
            }
            finally
            {
                g.InterpolationMode = interpolacaoAnterior;
                g.PixelOffsetMode = deslocamentoAnterior;
            }
        }

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
        /// PNG na hora de gravar — é escrever bytes. O custo é buffer temporário, e nos
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
