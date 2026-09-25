using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// Guarda em memória só as miniaturas que estão à vista, e as carrega fora da thread
    /// da UI.
    ///
    /// É a peça que segura a memória do launcher. A regra é simples: quem sai da tela é
    /// descartado (<see cref="ManterSomente"/>), quem entra é pedido para a thread de
    /// carga e desenhado como retângulo cinza enquanto não chega. Com isso a RAM depende
    /// de quantos cards cabem na janela, não do tamanho da biblioteca.
    /// </summary>
    public sealed class CacheDeMiniaturas : IDisposable
    {
        private sealed class Pedido
        {
            public string Id = "";
            public string? CaminhoDaCapa;
            public string CaminhoDoThumb = "";
            public string Titulo = "";
        }

        private readonly Dictionary<string, Bitmap> _prontas = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _naFila = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<Pedido> _fila = new LinkedList<Pedido>();
        private readonly object _trava = new object();

        private readonly Thread _carregador;
        private bool _encerrando;

        /// <summary>Size.Empty = entrega a miniatura como está no disco. Ver <see cref="TamanhoDeDesenho"/>.</summary>
        private Size _tamanhoDeDesenho = Size.Empty;

        public CacheDeMiniaturas()
        {
            _carregador = new Thread(Rodar)
            {
                IsBackground = true,
                Name = "carga-de-miniaturas",
                // Abaixo do normal: a UI e, mais tarde, o jogo têm prioridade sobre isto.
                Priority = ThreadPriority.BelowNormal
            };
            _carregador.Start();
        }

        /// <summary>Disparado quando uma miniatura fica pronta. Vem da thread de carga.</summary>
        public event EventHandler? ImagemPronta;

        /// <summary>Quantas imagens estão vivas na memória agora.</summary>
        public int ImagensEmMemoria
        {
            get { lock (_trava) return _prontas.Count; }
        }

        /// <summary>Quantas miniaturas foram geradas do zero (para conferir o cache em disco).</summary>
        public int MiniaturasGeradas { get; private set; }

        // Medição da thread de carga: sem número não dá para saber se a lentidão é
        // decodificação, gravação do thumb ou espera na fila.
        private long _ticksDeCarga;
        private long _ticksDeGravacao;

        public int Carregadas { get; private set; }

        public double MillisegundosDeCarga => _ticksDeCarga * 1000.0 / Stopwatch.Frequency;

        public double MillisegundosDeGravacao => _ticksDeGravacao * 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Tamanho em que a grade desenha a capa. Com ele definido, a thread de carga já
        /// entrega o bitmap nesse tamanho exato e em 32bpp PArgb.
        ///
        /// É o que tira o stutter da rolagem: antes, cada frame redimensionava as capas de
        /// 300 px para o card com interpolação bilinear, a partir de 24bpp — o caminho mais
        /// lento que o GDI+ tem. Em tamanho 1:1 e PArgb, o DrawImage vira uma cópia de
        /// memória. De brinde, o bitmap do card M ocupa menos que o de 300 px.
        ///
        /// Trocar o tamanho descarta o que está pronto: tudo volta sozinho no próximo desenho.
        /// </summary>
        public Size TamanhoDeDesenho
        {
            get { lock (_trava) return _tamanhoDeDesenho; }
            set
            {
                List<Bitmap> descartar;

                lock (_trava)
                {
                    if (_tamanhoDeDesenho == value) return;

                    _tamanhoDeDesenho = value;
                    descartar = new List<Bitmap>(_prontas.Values);
                    _prontas.Clear();
                    _fila.Clear();
                    _naFila.Clear();
                }

                foreach (var bitmap in descartar) bitmap.Dispose();
            }
        }

        /// <summary>
        /// Devolve a miniatura se já estiver carregada; senão devolve null e enfileira a
        /// carga. Chamada durante o desenho, então não pode fazer I/O.
        /// </summary>
        public Bitmap? Obter(Jogo jogo)
        {
            lock (_trava)
            {
                if (_prontas.TryGetValue(jogo.Id, out var pronta)) return pronta;

                if (_naFila.Add(jogo.Id))
                {
                    // AddFirst: o último pedido é o que o usuário está olhando agora.
                    _fila.AddFirst(new Pedido
                    {
                        Id = jogo.Id,
                        CaminhoDaCapa = jogo.CaminhoCapa(),
                        CaminhoDoThumb = jogo.CaminhoThumbnail(),
                        Titulo = jogo.Titulo
                    });
                    Monitor.Pulse(_trava);
                }
                return null;
            }
        }

        /// <summary>
        /// Descarta tudo que não está na lista de visíveis. Chamada no fim de cada
        /// desenho — é o Dispose() dos bitmaps que saíram de vista.
        /// </summary>
        public void ManterSomente(ICollection<string> idsVisiveis)
        {
            var descartar = new List<Bitmap>();

            lock (_trava)
            {
                var mortos = new List<string>();
                foreach (var par in _prontas)
                {
                    if (!idsVisiveis.Contains(par.Key)) mortos.Add(par.Key);
                }

                foreach (var id in mortos)
                {
                    descartar.Add(_prontas[id]);
                    _prontas.Remove(id);
                }

                // Pedido de card que já saiu da tela não interessa mais.
                var no = _fila.First;
                while (no != null)
                {
                    var proximo = no.Next;
                    if (!idsVisiveis.Contains(no.Value.Id))
                    {
                        _naFila.Remove(no.Value.Id);
                        _fila.Remove(no);
                    }
                    no = proximo;
                }
            }

            // Fora da trava: Dispose de bitmap pode demorar e não precisa segurar a fila.
            foreach (var bitmap in descartar) bitmap.Dispose();
        }

        /// <summary>Esquece a miniatura de um jogo (capa trocada, título editado).</summary>
        public void Invalidar(string id)
        {
            Bitmap? antiga = null;

            lock (_trava)
            {
                if (_prontas.TryGetValue(id, out antiga)) _prontas.Remove(id);
                _naFila.Remove(id);
            }

            antiga?.Dispose();
        }

        // ---- Thread de carga --------------------------------------------------------------

        private void Rodar()
        {
            while (true)
            {
                Pedido pedido;
                Size alvo;

                lock (_trava)
                {
                    while (!_encerrando && _fila.Count == 0) Monitor.Wait(_trava);
                    if (_encerrando) return;

                    pedido = _fila.First!.Value;
                    _fila.RemoveFirst();
                    alvo = _tamanhoDeDesenho;
                }

                var inicio = Stopwatch.GetTimestamp();
                var imagem = ProntaParaDesenho(Carregar(pedido), alvo);
                _ticksDeCarga += Stopwatch.GetTimestamp() - inicio;
                Carregadas++;

                var aproveitada = false;
                lock (_trava)
                {
                    _naFila.Remove(pedido.Id);

                    // Se o card saiu da tela (ou mudou de tamanho) enquanto carregava, joga
                    // fora na hora.
                    if (!_encerrando && alvo == _tamanhoDeDesenho && !_prontas.ContainsKey(pedido.Id))
                    {
                        _prontas[pedido.Id] = imagem;
                        aproveitada = true;
                    }
                }

                if (!aproveitada)
                {
                    imagem.Dispose();
                    continue;
                }

                ImagemPronta?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Ordem da spec: usa o thumb do cache; se não existe, gera a partir da capa e
        /// grava no cache; sem capa, desenha o card na hora (e não guarda em disco, para
        /// não ficar velho quando a capa de verdade chegar).
        /// </summary>
        private Bitmap Carregar(Pedido pedido)
        {
            try
            {
                var temCapa = !string.IsNullOrEmpty(pedido.CaminhoDaCapa) && File.Exists(pedido.CaminhoDaCapa);

                if (temCapa)
                {
                    if (ThumbEstaAtualizado(pedido))
                    {
                        try
                        {
                            return GeradorDeCapa.AbrirSemTravarArquivo(pedido.CaminhoDoThumb);
                        }
                        catch (Exception)
                        {
                            // Cache corrompido: refaz a partir da capa.
                        }
                    }

                    return GerarDaCapa(pedido);
                }
            }
            catch (Exception)
            {
                // Qualquer problema de disco cai no card gerado: card vazio, nunca.
            }

            return GeradorDeCapa.Gerar(pedido.Titulo, GeradorDeCapa.LarguraDaMiniatura, GeradorDeCapa.AlturaDaMiniatura);
        }

        /// <summary>
        /// Reamostra uma vez, aqui fora da UI, o que a grade reamostraria a cada frame.
        /// O thumb já tem a proporção do card, então basta esticar.
        /// </summary>
        private static Bitmap ProntaParaDesenho(Bitmap origem, Size alvo)
        {
            if (alvo.Width <= 0 || alvo.Height <= 0) return origem;

            Bitmap? destino = null;
            try
            {
                destino = new Bitmap(alvo.Width, alvo.Height, PixelFormat.Format32bppPArgb);

                using (var g = Graphics.FromImage(destino))
                using (var atributos = new ImageAttributes())
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // Sem isto a borda da imagem puxa pixel transparente de fora e escurece.
                    atributos.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(origem, new Rectangle(Point.Empty, alvo),
                        0, 0, origem.Width, origem.Height, GraphicsUnit.Pixel, atributos);
                }

                origem.Dispose();
                return destino;
            }
            catch (Exception)
            {
                // Sem memória para a cópia: desenha a original, só que mais devagar.
                destino?.Dispose();
                return origem;
            }
        }

        private bool ThumbEstaAtualizado(Pedido pedido)
        {
            if (!File.Exists(pedido.CaminhoDoThumb)) return false;

            try
            {
                // Capa mais nova que o thumb significa capa trocada: refazer.
                return File.GetLastWriteTimeUtc(pedido.CaminhoDoThumb) >=
                       File.GetLastWriteTimeUtc(pedido.CaminhoDaCapa!);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Bitmap GerarDaCapa(Pedido pedido)
        {
            using (var original = GeradorDeCapa.AbrirSemTravarArquivo(pedido.CaminhoDaCapa!))
            {
                var miniatura = GeradorDeCapa.Redimensionar(original,
                    GeradorDeCapa.LarguraDaMiniatura, GeradorDeCapa.AlturaDaMiniatura);

                try
                {
                    var inicio = Stopwatch.GetTimestamp();
                    GeradorDeCapa.SalvarJpeg(miniatura, pedido.CaminhoDoThumb);
                    _ticksDeGravacao += Stopwatch.GetTimestamp() - inicio;
                    MiniaturasGeradas++;
                }
                catch (Exception)
                {
                    // Sem poder gravar o cache (HD protegido contra escrita), segue em memória.
                }

                return miniatura;
            }
        }

        public void Dispose()
        {
            lock (_trava)
            {
                _encerrando = true;
                _fila.Clear();
                _naFila.Clear();
                Monitor.PulseAll(_trava);
            }

            // Espera curta: a thread é background e morre com o processo de qualquer jeito.
            _carregador.Join(500);

            lock (_trava)
            {
                foreach (var imagem in _prontas.Values) imagem.Dispose();
                _prontas.Clear();
            }
        }
    }
}
