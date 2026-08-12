using System;
using System.Drawing;
using System.Windows.Forms;
using Launcher.Modelo;

namespace Launcher.UI
{
    /// <summary>
    /// A matemática da grade, separada do desenho.
    ///
    /// Fica fora do controle de propósito: é o que decide quais cards estão visíveis, e
    /// portanto quais imagens precisam existir na memória. Errar aqui é carregar a
    /// biblioteca inteira em bitmaps — exatamente o que não pode acontecer.
    /// </summary>
    public sealed class LayoutDaGrade
    {
        /// <summary>Capa 2:3, como a spec pede (600x900 reduzido).</summary>
        public const double ProporcaoDaCapa = 3.0 / 2.0;

        private const int EspacamentoPadrao = 16;
        private const int MargemPadrao = 16;

        public LayoutDaGrade(TamanhoCard tamanho, int larguraDisponivel, int quantidade)
        {
            Espacamento = EspacamentoPadrao;
            LarguraCard = LarguraPara(tamanho);
            AlturaCapa = (int)Math.Round(LarguraCard * ProporcaoDaCapa);
            AlturaDoTitulo = tamanho == TamanhoCard.P ? 30 : 36;
            AlturaCard = AlturaCapa + AlturaDoTitulo;
            Quantidade = Math.Max(0, quantidade);

            var util = Math.Max(LarguraCard, larguraDisponivel - (2 * MargemPadrao));
            Colunas = Math.Max(1, (util + Espacamento) / (LarguraCard + Espacamento));
            Linhas = Quantidade == 0 ? 0 : ((Quantidade - 1) / Colunas) + 1;

            // Sobra dividida nas laterais: a grade fica centralizada em vez de grudada à esquerda.
            var larguraOcupada = (Colunas * LarguraCard) + ((Colunas - 1) * Espacamento);
            Margem = Math.Max(MargemPadrao, (larguraDisponivel - larguraOcupada) / 2);
        }

        public int Quantidade { get; }
        public int Colunas { get; }
        public int Linhas { get; }
        public int LarguraCard { get; }
        public int AlturaCard { get; }
        public int AlturaCapa { get; }
        public int AlturaDoTitulo { get; }
        public int Espacamento { get; }
        public int Margem { get; }

        public int AlturaDaCelula => AlturaCard + Espacamento;

        /// <summary>Altura total do conteúdo — é o que define a barra de rolagem.</summary>
        public int AlturaTotal => Linhas == 0 ? 0 : (Linhas * AlturaDaCelula) + Espacamento;

        /// <summary>
        /// Largura que a grade pode usar, a partir da largura EXTERNA do controle.
        ///
        /// Reserva a barra de rolagem vertical esteja ela visível ou não, de propósito.
        /// Se o cálculo dependesse da barra, existiria uma largura onde o layout entra em
        /// loop: sem barra cabe mais uma coluna, com mais uma coluna o conteúdo encurta,
        /// encurtando some a barra, sem barra cabe mais uma coluna... e a janela pisca
        /// sem parar. Reservando sempre, a conta vira ponto fixo — o preço é uma faixa de
        /// ~17 px à direita quando a barra não aparece.
        /// </summary>
        public static int LarguraUtil(int larguraExterna)
            => Math.Max(1, larguraExterna - LarguraDaBarraDeRolagem);

        /// <summary>Largura da barra vertical do sistema (acompanha o DPI).</summary>
        public static int LarguraDaBarraDeRolagem => SystemInformation.VerticalScrollBarWidth;

        public static int LarguraPara(TamanhoCard tamanho) => tamanho switch
        {
            TamanhoCard.P => 120,
            TamanhoCard.G => 220,
            _ => 168
        };

        /// <summary>Retângulo do card em coordenadas do conteúdo (sem contar a rolagem).</summary>
        public Rectangle Celula(int indice)
        {
            if (indice < 0 || indice >= Quantidade) return Rectangle.Empty;

            var coluna = indice % Colunas;
            var linha = indice / Colunas;

            return new Rectangle(
                Margem + (coluna * (LarguraCard + Espacamento)),
                Espacamento + (linha * AlturaDaCelula),
                LarguraCard,
                AlturaCard);
        }

        /// <summary>Área da capa dentro do card (o título fica embaixo dela).</summary>
        public Rectangle AreaDaCapa(Rectangle celula)
            => new Rectangle(celula.X, celula.Y, celula.Width, AlturaCapa);

        public Rectangle AreaDoTitulo(Rectangle celula)
            => new Rectangle(celula.X, celula.Y + AlturaCapa, celula.Width, AlturaDoTitulo);

        /// <summary>
        /// Faixa de índices visíveis na janela atual, com uma linha de folga em cima e
        /// embaixo para a rolagem não mostrar buraco cinza.
        /// </summary>
        public void FaixaVisivel(int deslocamentoY, int alturaVisivel, out int primeiro, out int ultimo)
        {
            primeiro = 0;
            ultimo = -1;
            if (Quantidade == 0 || Colunas == 0) return;

            var primeiraLinha = Math.Max(0, ((deslocamentoY - Espacamento) / AlturaDaCelula) - 1);
            var ultimaLinha = Math.Min(Linhas - 1, ((deslocamentoY + alturaVisivel) / AlturaDaCelula) + 1);

            primeiro = primeiraLinha * Colunas;
            ultimo = Math.Min(Quantidade - 1, ((ultimaLinha + 1) * Colunas) - 1);
        }

        /// <summary>Índice do card sob o ponto (coordenadas da tela), ou -1.</summary>
        public int IndiceEm(Point ponto, int deslocamentoY)
        {
            if (Quantidade == 0) return -1;

            var y = ponto.Y + deslocamentoY - Espacamento;
            if (y < 0) return -1;

            var linha = y / AlturaDaCelula;
            if (linha < 0 || linha >= Linhas) return -1;

            // Dentro da faixa vertical do card, e não no espaço entre linhas.
            if (y - (linha * AlturaDaCelula) > AlturaCard) return -1;

            var x = ponto.X - Margem;
            if (x < 0) return -1;

            var coluna = x / (LarguraCard + Espacamento);
            if (coluna < 0 || coluna >= Colunas) return -1;
            if (x - (coluna * (LarguraCard + Espacamento)) > LarguraCard) return -1;

            var indice = (linha * Colunas) + coluna;
            return indice < Quantidade ? indice : -1;
        }

        /// <summary>
        /// Move a seleção pelas setas. Devolve o índice novo (ou o mesmo, quando o
        /// movimento sairia da grade).
        /// </summary>
        public int Mover(int indiceAtual, int colunas, int linhas)
        {
            if (Quantidade == 0) return -1;
            if (indiceAtual < 0) return 0;

            var destino = indiceAtual + colunas + (linhas * Colunas);

            // Andar para os lados não pode pular de linha.
            if (colunas != 0 && linhas == 0)
            {
                var linhaAtual = indiceAtual / Colunas;
                if (destino < 0 || destino / Colunas != linhaAtual) return indiceAtual;
            }

            return destino < 0 || destino >= Quantidade ? indiceAtual : destino;
        }
    }
}
