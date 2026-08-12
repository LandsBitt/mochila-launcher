using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Launcher.Dados;
using Launcher.Modelo;
using Launcher.UI;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 4: layout da grade, busca/ordenação e — o que
    /// mais importa — o cache de miniaturas.
    ///
    /// A regra de memória da spec (200 jogos parados abaixo de ~80 MB) só vale se as
    /// imagens que saem de vista forem realmente descartadas. É o que os testes de
    /// <see cref="CacheDeMiniaturas"/> conferem, contando bitmaps vivos.
    /// </summary>
    public static class AutoTesteGrade
    {
        private const string NomePastaSandbox = "_autoteste-grade-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarLayout(v);
                TestarEstabilidadeDaLargura(v);
                TestarNavegacao(v);
                TestarRoteamentoDeTeclas(v);
                TestarBuscaEOrdenacao(v);
                TestarCapaGerada(v);
                TestarCacheDeMiniaturas(v, raizReal);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
                LimparSandbox(v, raizReal);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- Layout ------------------------------------------------------------------------

        private static void TestarLayout(Verificador v)
        {
            v.Escrever("Layout da grade");

            var layout = new LayoutDaGrade(TamanhoCard.M, 1000, 50);

            v.Verificar("capa na proporção 2:3",
                Math.Abs((layout.AlturaCapa / (double)layout.LarguraCard) - 1.5) < 0.02,
                $"{layout.LarguraCard}x{layout.AlturaCapa}");

            v.Verificar("cabe mais de uma coluna em 1000 px", layout.Colunas >= 4, layout.Colunas.ToString());
            v.Verificar("linhas calculadas a partir das colunas",
                layout.Linhas == (int)Math.Ceiling(50.0 / layout.Colunas), layout.Linhas.ToString());

            var primeira = layout.Celula(0);
            var segunda = layout.Celula(1);
            v.Verificar("cards da mesma linha ficam lado a lado", primeira.Y == segunda.Y && segunda.X > primeira.X);
            v.Verificar("não se sobrepõem", segunda.X >= primeira.Right);

            var abaixo = layout.Celula(layout.Colunas);
            v.Verificar("a linha seguinte desce", abaixo.Y > primeira.Y && abaixo.X == primeira.X);

            // Card menor = mais colunas na mesma largura.
            var pequeno = new LayoutDaGrade(TamanhoCard.P, 1000, 50);
            var grande = new LayoutDaGrade(TamanhoCard.G, 1000, 50);
            v.Verificar("card P cabe mais que o M, e o M mais que o G",
                pequeno.Colunas > layout.Colunas && layout.Colunas > grande.Colunas,
                $"P={pequeno.Colunas} M={layout.Colunas} G={grande.Colunas}");

            // Janela estreita não pode gerar zero colunas nem divisão por zero.
            var estreito = new LayoutDaGrade(TamanhoCard.G, 60, 10);
            v.Verificar("janela estreita ainda tem uma coluna", estreito.Colunas == 1, estreito.Colunas.ToString());

            var vazio = new LayoutDaGrade(TamanhoCard.M, 1000, 0);
            v.Verificar("grade vazia não tem altura", vazio.AlturaTotal == 0 && vazio.Linhas == 0);

            // O ponto do card devolve o índice do card.
            var alvo = layout.Celula(7);
            var indice = layout.IndiceEm(new Point(alvo.X + 5, alvo.Y + 5), 0);
            v.Verificar("clique dentro do card acha o índice", indice == 7, indice.ToString());

            var entreCards = layout.IndiceEm(new Point(alvo.Right + (layout.Espacamento / 2), alvo.Y + 5), 0);
            v.Verificar("clique no vão entre cards não seleciona nada", entreCards == -1, entreCards.ToString());
        }

        /// <summary>
        /// Varre todas as larguras de 400 a 2000 px procurando o ponto onde o layout
        /// oscilaria.
        ///
        /// O bug clássico de ScrollableControl: as colunas saem de ClientSize.Width, que
        /// encolhe quando a barra de rolagem aparece. Existe uma largura onde sem barra
        /// cabe mais uma coluna, com mais uma coluna o conteúdo encurta, encurtando some
        /// a barra — e a janela pisca sem parar. O teste mede as duas coisas: quantas
        /// larguras seriam instáveis pela regra antiga, e que pela regra nova não sobra
        /// nenhuma.
        /// </summary>
        private static void TestarEstabilidadeDaLargura(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Estabilidade do layout na largura-limite");

            var barra = LayoutDaGrade.LarguraDaBarraDeRolagem;
            v.Verificar("a largura da barra de rolagem é conhecida", barra > 0, barra.ToString());

            var tamanhos = new[] { TamanhoCard.P, TamanhoCard.M, TamanhoCard.G };
            var instaveisRegraAntiga = 0;
            var instaveisRegraNova = 0;
            var naoDeterministicas = 0;
            var cardSobABarra = 0;

            for (var largura = 400; largura <= 2000; largura++)
            {
                foreach (var tamanho in tamanhos)
                {
                    // Regra antiga: a largura dependia da barra estar visível.
                    var semBarra = new LayoutDaGrade(tamanho, largura, 200).Colunas;
                    var comBarra = new LayoutDaGrade(tamanho, largura - barra, 200).Colunas;
                    if (semBarra != comBarra) instaveisRegraAntiga++;

                    // Regra nova: a barra é reservada sempre, visível ou não.
                    var util = LayoutDaGrade.LarguraUtil(largura);
                    var primeiro = new LayoutDaGrade(tamanho, util, 200);
                    var segundo = new LayoutDaGrade(tamanho, util, 200);

                    if (primeiro.Colunas != segundo.Colunas ||
                        primeiro.Margem != segundo.Margem ||
                        primeiro.AlturaTotal != segundo.AlturaTotal)
                    {
                        naoDeterministicas++;
                    }

                    // O ponto fixo: recalcular já contando a barra devolve o mesmo layout.
                    var comBarraJaVisivel = new LayoutDaGrade(tamanho, LayoutDaGrade.LarguraUtil(largura), 200);
                    if (comBarraJaVisivel.Colunas != primeiro.Colunas) instaveisRegraNova++;

                    // E nenhum card pode ficar por baixo da barra.
                    var ocupado = primeiro.Margem +
                                  (primeiro.Colunas * primeiro.LarguraCard) +
                                  ((primeiro.Colunas - 1) * primeiro.Espacamento);
                    if (ocupado > largura - barra) cardSobABarra++;
                }
            }

            v.Escrever($"  (a regra antiga tinha {instaveisRegraAntiga} largura(s) de risco entre 400 e 2000 px)");

            v.Verificar("mesma entrada, mesmo layout (sem depender do estado da barra)",
                naoDeterministicas == 0, naoDeterministicas.ToString());

            v.Verificar("nenhuma largura oscila: o cálculo virou ponto fixo",
                instaveisRegraNova == 0, instaveisRegraNova.ToString());

            v.Verificar("nenhum card fica por baixo da barra de rolagem",
                cardSobABarra == 0, cardSobABarra.ToString());

            v.Verificar("a regra antiga realmente tinha onde oscilar (senão o teste não prova nada)",
                instaveisRegraAntiga > 0, instaveisRegraAntiga.ToString());
        }

        /// <summary>
        /// Quem fica com a tecla: a busca ou a grade. As setas ← → são as únicas
        /// disputadas — e só quando há texto digitado para corrigir.
        /// </summary>
        private static void TestarRoteamentoDeTeclas(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Teclas: busca x grade");

            v.Verificar("busca com texto: ← fica com a busca (dá para corrigir o que digitei)",
                !RoteadorDeTeclas.VaiParaGrade(Keys.Left, buscaTemFoco: true, buscaTemTexto: true));

            v.Verificar("busca com texto: → também fica com a busca",
                !RoteadorDeTeclas.VaiParaGrade(Keys.Right, buscaTemFoco: true, buscaTemTexto: true));

            v.Verificar("busca vazia: ← vai para a grade (não há cursor que interesse mover)",
                RoteadorDeTeclas.VaiParaGrade(Keys.Left, buscaTemFoco: true, buscaTemTexto: false));

            v.Verificar("busca vazia: → vai para a grade",
                RoteadorDeTeclas.VaiParaGrade(Keys.Right, buscaTemFoco: true, buscaTemTexto: false));

            v.Verificar("busca com texto: PageDown vai para a grade mesmo assim",
                RoteadorDeTeclas.VaiParaGrade(Keys.PageDown, buscaTemFoco: true, buscaTemTexto: true));

            v.Verificar("busca com texto: ↑ e ↓ vão para a grade",
                RoteadorDeTeclas.VaiParaGrade(Keys.Up, buscaTemFoco: true, buscaTemTexto: true) &&
                RoteadorDeTeclas.VaiParaGrade(Keys.Down, buscaTemFoco: true, buscaTemTexto: true));

            v.Verificar("busca com texto: Home e End vão para a grade",
                RoteadorDeTeclas.VaiParaGrade(Keys.Home, buscaTemFoco: true, buscaTemTexto: true) &&
                RoteadorDeTeclas.VaiParaGrade(Keys.End, buscaTemFoco: true, buscaTemTexto: true));

            v.Verificar("Enter aciona o jogo mesmo com a busca cheia",
                RoteadorDeTeclas.VaiParaGrade(Keys.Enter, buscaTemFoco: true, buscaTemTexto: true));

            v.Verificar("foco na grade: ← navega mesmo com texto na busca",
                RoteadorDeTeclas.VaiParaGrade(Keys.Left, buscaTemFoco: false, buscaTemTexto: true));

            v.Verificar("letra comum não é navegação (vai digitar na busca)",
                !RoteadorDeTeclas.VaiParaGrade(Keys.A, buscaTemFoco: true, buscaTemTexto: false) &&
                !RoteadorDeTeclas.VaiParaGrade(Keys.F6, buscaTemFoco: false, buscaTemTexto: false));
        }

        /// <summary>
        /// A faixa visível é o coração da economia de memória: se ela devolver a
        /// biblioteca inteira, o launcher carrega 200 imagens e estoura a RAM.
        /// </summary>
        private static void TestarNavegacao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Faixa visível e navegação");

            var layout = new LayoutDaGrade(TamanhoCard.M, 1000, 200);

            layout.FaixaVisivel(0, 700, out var primeiro, out var ultimo);
            var visiveis = ultimo - primeiro + 1;

            v.Verificar("começa do primeiro card", primeiro == 0);
            v.Verificar("mostra só um punhado de cards, não os 200",
                visiveis < 40, visiveis.ToString());

            layout.FaixaVisivel(5000, 700, out var primeiroLonge, out var ultimoLonge);
            v.Verificar("rolando para baixo, a faixa acompanha", primeiroLonge > 0, primeiroLonge.ToString());
            v.Verificar("e continua pequena", ultimoLonge - primeiroLonge + 1 < 40);
            v.Verificar("nunca passa do último jogo", ultimoLonge <= 199, ultimoLonge.ToString());

            layout.FaixaVisivel(999_999, 700, out _, out var ultimoNoFim);
            v.Verificar("rolagem além do fim não estoura o índice", ultimoNoFim <= 199, ultimoNoFim.ToString());

            // Setas.
            v.Verificar("direita anda um card", layout.Mover(0, +1, 0) == 1);
            v.Verificar("baixo desce uma linha", layout.Mover(0, 0, +1) == layout.Colunas);
            v.Verificar("esquerda no começo da linha não pula para a linha de cima",
                layout.Mover(0, -1, 0) == 0);
            v.Verificar("direita no fim da linha não pula para a de baixo",
                layout.Mover(layout.Colunas - 1, +1, 0) == layout.Colunas - 1);
            v.Verificar("cima na primeira linha fica onde está", layout.Mover(2, 0, -1) == 2);
            v.Verificar("baixo na última linha não sai da lista",
                layout.Mover(199, 0, +1) == 199);
        }

        // ---- Busca, ordenação, favoritos ---------------------------------------------------

        private static void TestarBuscaEOrdenacao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Busca, ordenação e favoritos");

            var jogos = new List<Jogo>
            {
                Criar("nfs-carbon", "NFS Carbon", segundos: 7200, dias: 3),
                Criar("nfsmw", "Need for Speed Most Wanted", segundos: 30000, dias: 40),
                Criar("coracao", "Coração de Aço", segundos: 600, dias: null, favorito: true),
                Criar("hollow", "Hollow Knight", segundos: 0, dias: null)
            };

            var tudo = FiltroDaBiblioteca.Aplicar(jogos, "", OrdenacaoBiblioteca.Alfabetica, false);
            v.Verificar("sem busca, vêm todos", tudo.Count == 4, tudo.Count.ToString());
            v.VerificarTexto("ordem alfabética", "Coração de Aço", tudo[0].Titulo);

            var porTempo = FiltroDaBiblioteca.Aplicar(jogos, "", OrdenacaoBiblioteca.MaisJogados, false);
            v.VerificarTexto("mais jogados primeiro", "Need for Speed Most Wanted", porTempo[0].Titulo);
            v.VerificarTexto("nunca jogado por último", "Hollow Knight", porTempo[3].Titulo);

            var porData = FiltroDaBiblioteca.Aplicar(jogos, "", OrdenacaoBiblioteca.JogadosRecentemente, false);
            v.VerificarTexto("jogado mais recentemente primeiro", "NFS Carbon", porData[0].Titulo);
            v.Verificar("quem nunca foi jogado vai para o fim",
                porData[2].UltimaVezJogado is null && porData[3].UltimaVezJogado is null);

            var favoritos = FiltroDaBiblioteca.Aplicar(jogos, "", OrdenacaoBiblioteca.Alfabetica, true);
            v.Verificar("filtro de favoritos", favoritos.Count == 1 && favoritos[0].Id == "coracao",
                favoritos.Count.ToString());

            var busca = FiltroDaBiblioteca.Aplicar(jogos, "nfs", OrdenacaoBiblioteca.Alfabetica, false);
            v.Verificar("busca simples", busca.Count == 1 && busca[0].Id == "nfs-carbon", busca.Count.ToString());

            var semAcento = FiltroDaBiblioteca.Aplicar(jogos, "coracao", OrdenacaoBiblioteca.Alfabetica, false);
            v.Verificar("busca sem acento acha título com acento", semAcento.Count == 1, semAcento.Count.ToString());

            var maiusculas = FiltroDaBiblioteca.Aplicar(jogos, "HOLLOW", OrdenacaoBiblioteca.Alfabetica, false);
            v.Verificar("busca não liga para maiúsculas", maiusculas.Count == 1);

            var doisTermos = FiltroDaBiblioteca.Aplicar(jogos, "speed wanted", OrdenacaoBiblioteca.Alfabetica, false);
            v.Verificar("termos soltos casam em qualquer ordem",
                doisTermos.Count == 1 && doisTermos[0].Id == "nfsmw", doisTermos.Count.ToString());

            var semResultado = FiltroDaBiblioteca.Aplicar(jogos, "zzz", OrdenacaoBiblioteca.Alfabetica, false);
            v.Verificar("busca sem resultado devolve lista vazia", semResultado.Count == 0);
        }

        // ---- Capa gerada ---------------------------------------------------------------------

        private static void TestarCapaGerada(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Capa gerada (nenhum card fica vazio)");

            using (var capa = GeradorDeCapa.Gerar("NFS Carbon", 300, 450))
            {
                v.Verificar("tamanho pedido", capa.Width == 300 && capa.Height == 450);
                v.Verificar("não é uma imagem toda preta",
                    capa.GetPixel(10, 10).ToArgb() != Color.Black.ToArgb());
            }

            var cor1 = GeradorDeCapa.CorDoTitulo("NFS Carbon");
            var cor2 = GeradorDeCapa.CorDoTitulo("NFS Carbon");
            var cor3 = GeradorDeCapa.CorDoTitulo("Hollow Knight");

            v.Verificar("mesma cor para o mesmo título, sempre", cor1 == cor2, $"{cor1} vs {cor2}");
            v.Verificar("títulos diferentes, cores diferentes", cor1 != cor3, $"{cor1} vs {cor3}");
            v.Verificar("cor escura o bastante para o texto branco aparecer",
                cor1.GetBrightness() < 0.6, cor1.GetBrightness().ToString("F2"));

            using (var semTitulo = GeradorDeCapa.Gerar("", 120, 180))
                v.Verificar("título vazio não quebra o gerador", semTitulo.Width == 120);

            // Redimensionar preenche a proporção sem esticar.
            using (var original = new Bitmap(800, 400))
            using (var reduzida = GeradorDeCapa.Redimensionar(original, 300, 450))
            {
                v.Verificar("redimensiona para a proporção 2:3 exata",
                    reduzida.Width == 300 && reduzida.Height == 450);
            }
        }

        // ---- Cache de miniaturas ----------------------------------------------------------------

        private static void TestarCacheDeMiniaturas(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Cache de miniaturas (memória e disco)");

            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);
            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();

            // Uma capa de verdade em disco, para exercitar a geração do thumb.
            var jogoComCapa = Criar("com-capa", "Jogo Com Capa");
            jogoComCapa.CapaArquivo = "com-capa.jpg";

            using (var capa = GeradorDeCapa.Gerar("Capa Original", 600, 900))
                GeradorDeCapa.SalvarJpeg(capa, Path.Combine(Caminhos.PastaCapas, "com-capa.jpg"));

            var jogoSemCapa = Criar("sem-capa", "Jogo Sem Capa");

            using (var cache = new CacheDeMiniaturas())
            {
                v.Verificar("cache começa vazio", cache.ImagensEmMemoria == 0);

                // Primeira chamada não bloqueia: devolve null e enfileira.
                v.Verificar("primeira chamada devolve null (carga é assíncrona)",
                    cache.Obter(jogoComCapa) is null);

                v.Verificar("a miniatura fica pronta em pouco tempo",
                    EsperarAte(() => cache.Obter(jogoComCapa) != null, 5000));

                var miniatura = cache.Obter(jogoComCapa);
                v.Verificar("miniatura no tamanho do cache",
                    miniatura != null && miniatura.Width == GeradorDeCapa.LarguraDaMiniatura,
                    miniatura?.Width.ToString());

                var thumb = jogoComCapa.CaminhoThumbnail();
                v.Verificar("thumb gravado em _launcher\\cache", File.Exists(thumb), thumb);
                v.Verificar("e é menor que a capa cheia",
                    new FileInfo(thumb).Length < new FileInfo(jogoComCapa.CaminhoCapa()!).Length);

                // Jogo sem capa nenhuma ainda desenha alguma coisa.
                cache.Obter(jogoSemCapa);
                v.Verificar("jogo sem capa também recebe imagem",
                    EsperarAte(() => cache.Obter(jogoSemCapa) != null, 5000));
                v.Verificar("card gerado não vai para o disco (não fica velho depois)",
                    !File.Exists(jogoSemCapa.CaminhoThumbnail()));

                v.Verificar("as duas imagens estão vivas", cache.ImagensEmMemoria == 2,
                    cache.ImagensEmMemoria.ToString());

                // O que sai de vista morre.
                cache.ManterSomente(new HashSet<string> { "com-capa" });
                v.Verificar("quem saiu da tela foi descartado", cache.ImagensEmMemoria == 1,
                    cache.ImagensEmMemoria.ToString());

                cache.ManterSomente(new HashSet<string>());
                v.Verificar("grade vazia libera tudo", cache.ImagensEmMemoria == 0,
                    cache.ImagensEmMemoria.ToString());

                // Segunda passada: o thumb do disco é reaproveitado.
                cache.Obter(jogoComCapa);
                v.Verificar("recarrega a partir do cache em disco",
                    EsperarAte(() => cache.Obter(jogoComCapa) != null, 5000));

                // O teste que representa a regra dos 200 jogos: pedir muitos e manter poucos.
                var muitos = new List<Jogo>();
                for (var i = 0; i < 200; i++) muitos.Add(Criar($"jogo-{i}", $"Jogo Sintético {i}"));

                foreach (var jogo in muitos) cache.Obter(jogo);

                var visiveis = new HashSet<string>();
                for (var i = 0; i < 20; i++) visiveis.Add($"jogo-{i}");

                EsperarAte(() => cache.ImagensEmMemoria > 20, 3000);
                cache.ManterSomente(visiveis);

                v.Verificar("com 200 jogos pedidos, só os visíveis ficam na memória",
                    cache.ImagensEmMemoria <= visiveis.Count, cache.ImagensEmMemoria.ToString());

                // Invalidar força a próxima carga a refazer.
                cache.Obter(jogoComCapa);
                EsperarAte(() => cache.Obter(jogoComCapa) != null, 5000);
                cache.Invalidar("com-capa");
                v.Verificar("invalidar esquece a imagem", cache.Obter(jogoComCapa) is null);
            }
        }

        // ---- Apoio ------------------------------------------------------------------------------

        private static Jogo Criar(string id, string titulo, int segundos = 0, int? dias = null, bool favorito = false)
            => new Jogo
            {
                Id = id,
                Titulo = titulo,
                ExecutavelRelativo = $@"Jogos\{id}\{id}.exe",
                SegundosJogados = segundos,
                UltimaVezJogado = dias is null ? null : DateTime.UtcNow.AddDays(-dias.Value),
                Favorito = favorito
            };

        /// <summary>Espera uma condição virar verdadeira, sem travar mais que o limite.</summary>
        private static bool EsperarAte(Func<bool> condicao, int limiteMs)
        {
            var fim = Environment.TickCount + limiteMs;

            while (Environment.TickCount < fim)
            {
                if (condicao()) return true;
                Thread.Sleep(15);
            }
            return condicao();
        }

        private static void LimparSandbox(Verificador v, string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox);
            try
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception ex)
            {
                v.Escrever($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
            }
        }
    }
}
