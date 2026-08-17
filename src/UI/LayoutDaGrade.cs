using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// A matemática da grade, separada do desenho.
    ///
    /// Fica fora do controle de propósito: é o que decide quais cards estão visíveis, e
    /// portanto quais imagens precisam existir na memória. Errar aqui é carregar a
    /// biblioteca inteira em bitmaps — exatamente o que não pode acontecer.
    ///
    /// Desde a fase 13 a lista pode vir em <b>duas seções</b> ("Continuar jogando" e o
    /// resto do acervo), e é por isso que toda conta daqui passou a sair de uma lista de
    /// faixas em vez de <c>indice / Colunas</c>: com seção, a linha de um índice depende
    /// de quantos cards vieram antes dele E de quantos cabeçalhos couberam pelo caminho.
    /// Uma fileira separada acima da grade custaria uma segunda virtualização; uma seção
    /// da mesma lista custa esta lista de faixas.
    /// </summary>
    public sealed class LayoutDaGrade
    {
        /// <summary>Capa 2:3, como a spec pede (600x900 reduzido).</summary>
        public const double ProporcaoDaCapa = 3.0 / 2.0;

        // Densidade: o que a grade tem para mostrar são as capas, e todo pixel gasto em
        // vão e em faixa de título é capa que deixa de caber na tela. O título ocupa uma
        // linha só (o nome inteiro fica na ToolTip do card), então a altura reservada é a
        // de uma linha de texto e mais nada.
        private const int EspacamentoPadrao = 12;
        private const int MargemPadrao = 12;

        /// <summary>Altura da faixa de título de uma seção. Zero quando não há seções.</summary>
        private const int AlturaDoCabecalhoPadrao = 34;

        /// <summary>Uma linha de cards: onde começa, quantos cards tem e em que y ela está.</summary>
        private readonly struct Faixa
        {
            public Faixa(int primeiro, int quantidade, int topo, int secao)
            {
                Primeiro = primeiro;
                Quantidade = quantidade;
                Topo = topo;
                Secao = secao;
            }

            public readonly int Primeiro;
            public readonly int Quantidade;
            public readonly int Topo;
            public readonly int Secao;
        }

        private readonly List<Faixa> _faixas = new List<Faixa>();

        /// <summary>Onde cada cabeçalho de seção é desenhado, em coordenadas do conteúdo.</summary>
        private readonly List<Rectangle> _cabecalhos = new List<Rectangle>();

        /// <summary>Quantas linhas a primeira seção ocupa. Atalho do cálculo de índice.</summary>
        private readonly int _linhasDaPrimeiraSecao;

        public LayoutDaGrade(TamanhoCard tamanho, int larguraDisponivel, int quantidade)
            : this(tamanho, larguraDisponivel, quantidade, 0)
        {
        }

        /// <param name="quantidadeNaPrimeiraSecao">
        /// Quantos dos primeiros cards formam a seção "Continuar jogando". Zero (ou o total)
        /// significa lista única, e nesse caso a geometria é exatamente a de antes da fase
        /// 13 — nenhum cabeçalho, nenhum pixel de diferença.
        /// </param>
        public LayoutDaGrade(TamanhoCard tamanho, int larguraDisponivel, int quantidade,
                             int quantidadeNaPrimeiraSecao)
        {
            Espacamento = EspacamentoPadrao;
            LarguraCard = LarguraPara(tamanho);
            AlturaCapa = (int)Math.Round(LarguraCard * ProporcaoDaCapa);
            AlturaDoTitulo = tamanho == TamanhoCard.P ? 20 : 24;
            AlturaCard = AlturaCapa + AlturaDoTitulo;
            Quantidade = Math.Max(0, quantidade);

            var util = Math.Max(LarguraCard, larguraDisponivel - (2 * MargemPadrao));
            Colunas = Math.Max(1, (util + Espacamento) / (LarguraCard + Espacamento));
            Linhas = Quantidade == 0 ? 0 : ((Quantidade - 1) / Colunas) + 1;

            // Sobra dividida nas laterais: a grade fica centralizada em vez de grudada à esquerda.
            var larguraOcupada = (Colunas * LarguraCard) + ((Colunas - 1) * Espacamento);
            Margem = Math.Max(MargemPadrao, (larguraDisponivel - larguraOcupada) / 2);

            QuantidadeNaPrimeiraSecao = Math.Max(0, Math.Min(Quantidade, quantidadeNaPrimeiraSecao));
            TemSecoes = QuantidadeNaPrimeiraSecao > 0 && QuantidadeNaPrimeiraSecao < Quantidade;
            AlturaDoCabecalho = TemSecoes ? AlturaDoCabecalhoPadrao : 0;

            _linhasDaPrimeiraSecao = TemSecoes ? ((QuantidadeNaPrimeiraSecao - 1) / Colunas) + 1 : 0;

            AlturaTotal = MontarFaixas(larguraOcupada);
        }

        /// <summary>
        /// Distribui as linhas (e os cabeçalhos) de cima para baixo. Devolve a altura total
        /// do conteúdo, que é o que define a barra de rolagem.
        /// </summary>
        private int MontarFaixas(int larguraOcupada)
        {
            if (Quantidade == 0) return 0;

            var contagens = TemSecoes
                ? new[] { QuantidadeNaPrimeiraSecao, Quantidade - QuantidadeNaPrimeiraSecao }
                : new[] { Quantidade };

            var y = Espacamento;
            var inicio = 0;

            for (var secao = 0; secao < contagens.Length; secao++)
            {
                var contagem = contagens[secao];

                if (TemSecoes)
                {
                    _cabecalhos.Add(new Rectangle(Margem, y, larguraOcupada, AlturaDoCabecalho));
                    y += AlturaDoCabecalho;
                }

                var linhas = ((contagem - 1) / Colunas) + 1;
                for (var linha = 0; linha < linhas; linha++)
                {
                    var primeiro = inicio + (linha * Colunas);
                    var quantos = Math.Min(Colunas, contagem - (linha * Colunas));

                    _faixas.Add(new Faixa(primeiro, quantos, y, secao));
                    y += AlturaDaCelula;
                }

                inicio += contagem;
            }

            return y;
        }

        public int Quantidade { get; }
        public int Colunas { get; }

        /// <summary>Total de linhas de cards, somando as duas seções.</summary>
        public int Linhas { get; }

        public int LarguraCard { get; }
        public int AlturaCard { get; }
        public int AlturaCapa { get; }
        public int AlturaDoTitulo { get; }
        public int Espacamento { get; }
        public int Margem { get; }

        /// <summary>Quantos cards formam a seção "Continuar jogando" (0 = lista única).</summary>
        public int QuantidadeNaPrimeiraSecao { get; }

        public bool TemSecoes { get; }

        /// <summary>Altura da faixa de título de cada seção. Zero sem seções.</summary>
        public int AlturaDoCabecalho { get; }

        public int Secoes => TemSecoes ? 2 : 1;

        public int AlturaDaCelula => AlturaCard + Espacamento;

        /// <summary>Altura total do conteúdo — é o que define a barra de rolagem.</summary>
        public int AlturaTotal { get; }

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

        /// <summary>A qual seção um índice pertence (0 ou 1).</summary>
        public int SecaoDoIndice(int indice)
            => TemSecoes && indice >= QuantidadeNaPrimeiraSecao ? 1 : 0;

        /// <summary>Área do cabeçalho de uma seção, em coordenadas do conteúdo. Vazia sem seções.</summary>
        public Rectangle AreaDoCabecalho(int secao)
            => secao >= 0 && secao < _cabecalhos.Count ? _cabecalhos[secao] : Rectangle.Empty;

        /// <summary>Retângulo do card em coordenadas do conteúdo (sem contar a rolagem).</summary>
        public Rectangle Celula(int indice)
        {
            if (indice < 0 || indice >= Quantidade) return Rectangle.Empty;

            var faixa = _faixas[FaixaDoIndice(indice)];
            var coluna = indice - faixa.Primeiro;

            return new Rectangle(
                Margem + (coluna * (LarguraCard + Espacamento)),
                faixa.Topo,
                LarguraCard,
                AlturaCard);
        }

        /// <summary>
        /// O y que a rolagem tem que alcançar para o card aparecer <b>com o cabeçalho da
        /// seção dele</b>, quando ele está na primeira linha de uma seção. Sem isto,
        /// selecionar o primeiro card esconderia justamente o título que explica por que
        /// aqueles cinco jogos estão ali.
        /// </summary>
        public int TopoParaRolar(int indice)
        {
            if (indice < 0 || indice >= Quantidade) return 0;

            var qual = FaixaDoIndice(indice);
            var faixa = _faixas[qual];

            var primeiraDaSecao = qual == 0 || _faixas[qual - 1].Secao != faixa.Secao;
            return faixa.Topo - (primeiraDaSecao ? AlturaDoCabecalho : 0);
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
            if (Quantidade == 0 || _faixas.Count == 0) return;

            var topo = deslocamentoY - AlturaDaCelula;
            var base_ = deslocamentoY + alturaVisivel + AlturaDaCelula;

            var achou = false;

            foreach (var faixa in _faixas)
            {
                if (faixa.Topo + AlturaCard < topo) continue;
                if (faixa.Topo > base_) break;

                if (!achou)
                {
                    primeiro = faixa.Primeiro;
                    achou = true;
                }
                ultimo = faixa.Primeiro + faixa.Quantidade - 1;
            }

            // Rolagem além do fim do conteúdo: devolve a última linha em vez de faixa
            // vazia. Uma faixa vazia soltaria todas as imagens e a tela ficaria cinza.
            if (!achou)
            {
                var fim = _faixas[_faixas.Count - 1];
                primeiro = fim.Primeiro;
                ultimo = fim.Primeiro + fim.Quantidade - 1;
            }
        }

        /// <summary>Índice do card sob o ponto (coordenadas da tela), ou -1.</summary>
        public int IndiceEm(Point ponto, int deslocamentoY)
        {
            if (Quantidade == 0) return -1;

            var y = ponto.Y + deslocamentoY;

            foreach (var faixa in _faixas)
            {
                if (y < faixa.Topo) return -1;                       // cabeçalho ou vão entre linhas
                if (y > faixa.Topo + AlturaCard) continue;

                var x = ponto.X - Margem;
                if (x < 0) return -1;

                var coluna = x / (LarguraCard + Espacamento);
                if (coluna < 0 || coluna >= faixa.Quantidade) return -1;
                if (x - (coluna * (LarguraCard + Espacamento)) > LarguraCard) return -1;

                return faixa.Primeiro + coluna;
            }

            return -1;
        }

        /// <summary>
        /// Move a seleção pelas setas. Devolve o índice novo (ou o mesmo, quando o
        /// movimento sairia da grade).
        ///
        /// Com duas seções, descer da última linha de "Continuar jogando" entra na primeira
        /// linha do acervo — a lista é uma só, e a navegação também. O que não acontece é
        /// pular de linha andando para os lados.
        /// </summary>
        public int Mover(int indiceAtual, int colunas, int linhas)
        {
            if (Quantidade == 0) return -1;
            if (indiceAtual < 0) return 0;
            if (indiceAtual >= Quantidade) indiceAtual = Quantidade - 1;

            var origem = FaixaDoIndice(indiceAtual);
            var coluna = indiceAtual - _faixas[origem].Primeiro;

            var destino = origem + linhas;
            if (destino < 0 || destino >= _faixas.Count) return indiceAtual;

            var novaColuna = coluna + colunas;
            if (novaColuna < 0 || novaColuna >= _faixas[destino].Quantidade) return indiceAtual;

            return _faixas[destino].Primeiro + novaColuna;
        }

        /// <summary>Em que linha (índice na lista de faixas) um card está.</summary>
        private int FaixaDoIndice(int indice)
        {
            if (TemSecoes && indice >= QuantidadeNaPrimeiraSecao)
            {
                return _linhasDaPrimeiraSecao + ((indice - QuantidadeNaPrimeiraSecao) / Colunas);
            }
            return indice / Colunas;
        }
    }
}
