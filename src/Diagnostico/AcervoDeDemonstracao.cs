// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Monta um HD de mentira com N jogos, para eu conseguir OLHAR a grade e MEDIR a
    /// memória dela antes de ter uma biblioteca de verdade catalogada.
    ///
    /// Tudo acontece numa sandbox dentro da pasta do launcher (a raiz portátil é
    /// desviada para lá com <see cref="Caminhos.DefinirPastaBase"/>), e a sandbox é
    /// apagada no fim. Nenhum arquivo real do acervo é tocado.
    ///
    /// O acervo é propositalmente irregular: jogo sem capa (para o card gerado
    /// aparecer), jogo com o executável faltando (para a faixa "não encontrado"),
    /// favoritos, títulos longos e com acento. É o que revela problema de layout.
    /// </summary>
    public static class AcervoDeDemonstracao
    {
        /// <summary>Sandbox criada ao lado do Mochila.exe. Some no fim.</summary>
        public const string NomeDaSandbox = "_demo-tmp";

        /// <summary>Um jogo a cada tantos fica sem capa — exercita o card gerado.</summary>
        private const int UmSemCapaACada = 7;

        /// <summary>Um jogo a cada tantos aponta para um exe que não existe.</summary>
        private const int UmSemExecutavelACada = 13;

        private const int UmFavoritoACada = 5;

        /// <summary>
        /// Capas-mestras geradas uma vez e copiadas para cada jogo. Gerar 200 imagens de
        /// 600x900 levaria segundos sem ensinar nada: o que interessa medir é a geração
        /// do thumbnail, e essa continua sendo uma por jogo.
        /// </summary>
        private const int QuantidadeDeMestras = 6;

        private static readonly string[] Titulos =
        {
            "Need for Speed: Most Wanted (Black Edition)",
            "Need for Speed - Underground 2",
            "NFS Carbon",
            "Hollow Knight",
            "Coração de Aço",
            "Age of Empires II: The Age of Kings",
            "Diablo II",
            "Max Payne 2: The Fall of Max Payne",
            "Stardew Valley",
            "Celeste",
            "Terraria",
            "Dead Cells",
            "Half-Life 2",
            "Grand Theft Auto: San Andreas",
            "Prince of Persia: The Sands of Time",
            "Heroes of Might and Magic III",
            "Commandos: Behind Enemy Lines",
            "RollerCoaster Tycoon 2",
            "The Binding of Isaac",
            "Hotline Miami",
            "Undertale",
            "FTL: Faster Than Light",
            "Braid",
            "Limbo",
            "Bastion",
            "Torchlight II",
            "Mirror's Edge",
            "Mafia: The City of Lost Heaven",
            "Carmageddon 2: Carpocalypse Now",
            "Worms Armageddon"
        };

        /// <summary>
        /// Cria a sandbox, gera os jogos e aponta a raiz portátil para lá.
        /// Devolve a pasta que passa a fazer o papel do HD.
        /// </summary>
        /// <param name="comHero">
        /// Também gera hero e logo para cada jogo (fase 14). Fica desligado por padrão
        /// porque triplica a arte em disco e é peso morto para quem só quer olhar a grade —
        /// mas é obrigatório no bench, onde a pergunta é justamente quanta memória a arte
        /// grande custa.
        /// </param>
        public static string Montar(int quantidade, bool comHero = false)
        {
            if (quantidade < 1) quantidade = 1;

            var sandbox = Path.Combine(Caminhos.PastaBase, NomeDaSandbox, "hd");
            Directory.CreateDirectory(sandbox);

            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();

            var mestras = GerarCapasMestras();
            (string Hero, string Logo)? artes = comHero ? GerarArteLargaMestra() : null;

            var biblioteca = new Biblioteca();
            biblioteca.PastasEscaneadas.Add("Jogos");

            for (var i = 0; i < quantidade; i++)
            {
                var jogo = CriarJogo(i, biblioteca, mestras);
                if (artes is not null) AplicarArteLarga(jogo, artes.Value);

                biblioteca.Jogos.Add(jogo);
            }

            biblioteca.Salvar();
            new Config().Salvar();

            return sandbox;
        }

        /// <summary>
        /// Inventa um histórico de sessões para a biblioteca sintética (fase 13), para eu
        /// conseguir OLHAR a tela de estatísticas — barras, mapa de calor e top 10 — antes
        /// de ter jogado de verdade.
        ///
        /// Espalha as sessões pelos últimos 14 meses de propósito: é o que faz o seletor de
        /// ano ter mais de um ano para folhear e o mapa de calor não sair com uma coluna só.
        /// Nada aqui roda fora do <c>--grade-demo</c>.
        /// </summary>
        public static void MontarHistorico()
        {
            var biblioteca = Biblioteca.Carregar();
            if (biblioteca.Jogos.Count == 0) return;

            var historico = new HistoricoDeSessoes();
            var semente = new Random(20260817);   // fixa: a demonstração é igual toda vez
            var agora = DateTime.UtcNow;

            for (var dia = 0; dia < 430; dia++)
            {
                // Uns dias sem jogo nenhum: mapa de calor todo aceso não mostra nada.
                if (semente.Next(0, 100) < 45) continue;

                var quantas = semente.Next(1, 3);
                for (var i = 0; i < quantas; i++)
                {
                    var jogo = biblioteca.Jogos[semente.Next(0, biblioteca.Jogos.Count)];

                    var inicio = agora.AddDays(-dia)
                                      .AddHours(semente.Next(-6, 7))
                                      .AddMinutes(semente.Next(0, 60));

                    historico.Sessoes.Add(new Sessao(jogo.Id, inicio, semente.Next(300, 9000)));
                }
            }

            historico.Salvar();
        }

        /// <summary>Apaga a sandbox e devolve a raiz portátil para a pasta do Mochila.exe.</summary>
        public static void Limpar()
        {
            Caminhos.RestaurarPastaBase();

            try
            {
                var pasta = Path.Combine(Caminhos.PastaBase, NomeDaSandbox);
                if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
            }
            catch (Exception)
            {
                // Sandbox presa por um arquivo ainda aberto: o próximo Montar sobrescreve.
            }
        }

        // ---- Montagem ------------------------------------------------------------------------

        private static Jogo CriarJogo(int indice, Biblioteca biblioteca, IReadOnlyList<string> mestras)
        {
            var titulo = TituloDe(indice);
            var id = biblioteca.GerarId(titulo);

            // Pasta nomeada pelo id (que já é único), para dois títulos parecidos não
            // caírem na mesma pasta.
            var relativo = Path.Combine("Jogos", id, id + ".exe");
            var pastaDoJogo = Path.Combine(Caminhos.PastaBase, "Jogos", id);

            // Um a cada tantos não tem o exe no lugar: é o card "não encontrado".
            var temExecutavel = indice % UmSemExecutavelACada != UmSemExecutavelACada - 1;
            if (temExecutavel)
            {
                Directory.CreateDirectory(pastaDoJogo);
                var exe = Path.Combine(Caminhos.PastaBase, relativo);
                if (!File.Exists(exe)) File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });   // "MZ"
            }

            var capa = indice % UmSemCapaACada == UmSemCapaACada - 1
                ? null
                : CopiarCapa(mestras[indice % mestras.Count], id);

            // Tempo e data espalhados para dar o que ordenar nos três critérios.
            var segundos = ((indice * 37) % 900) * 60;
            var diasAtras = indice % 3 == 0 ? (int?)null : (indice * 11) % 400;

            var jogo = new Jogo
            {
                Id = id,
                Titulo = titulo,
                ExecutavelRelativo = relativo,
                CapaArquivo = capa,
                SegundosJogados = segundos,
                UltimaVezJogado = diasAtras is null ? null : DateTime.UtcNow.AddDays(-diasAtras.Value),
                Favorito = indice % UmFavoritoACada == 0,

                // Campos do schema v3: sem eles não dá para olhar a faixa de tags nem a
                // busca com operadores antes de etiquetar um acervo de verdade.
                Nota = indice % 6,
                Status = StatusPossiveis[indice % StatusPossiveis.Length]
            };

            Etiquetas.Acrescentar(jogo.Tags, TagsPossiveis[indice % TagsPossiveis.Length]);
            if (indice % 3 == 0) Etiquetas.Acrescentar(jogo.Tags, TagsPossiveis[(indice + 2) % TagsPossiveis.Length]);

            return jogo;
        }

        private static readonly string[] TagsPossiveis =
        {
            "corrida", "indie", "rpg", "estrategia", "antigo", "coop", "ação"
        };

        private static readonly StatusDoJogo[] StatusPossiveis =
        {
            StatusDoJogo.Nenhum, StatusDoJogo.QueroJogar, StatusDoJogo.Zerado,
            StatusDoJogo.Nenhum, StatusDoJogo.Jogando, StatusDoJogo.Largado
        };

        private static string TituloDe(int indice)
        {
            var titulo = Titulos[indice % Titulos.Length];

            // Passou da lista: numera as repetições, para os títulos continuarem distintos.
            var volta = indice / Titulos.Length;
            return volta == 0 ? titulo : $"{titulo} — Edição {volta + 1}";
        }

        private static List<string> GerarCapasMestras()
        {
            var mestras = new List<string>(QuantidadeDeMestras);

            for (var i = 0; i < QuantidadeDeMestras; i++)
            {
                var caminho = Path.Combine(Caminhos.PastaCapas, $"_mestra-{i}.jpg");

                using (var capa = GeradorDeCapa.Gerar($"Capa {i + 1}", 600, 900))
                    GeradorDeCapa.SalvarJpeg(capa, caminho);

                mestras.Add(caminho);
            }
            return mestras;
        }

        private static string CopiarCapa(string mestra, string id)
        {
            var nome = id + ".jpg";
            File.Copy(mestra, Path.Combine(Caminhos.PastaCapas, nome), overwrite: true);
            return nome;
        }

        /// <summary>
        /// Um hero e um logo mestres, copiados para todos os jogos.
        ///
        /// <b>Os tamanhos são os reais do thumb do SteamGridDB</b>, e não números redondos:
        /// o bench existe para responder quanta memória a arte da fase 14 custa, e medir
        /// isso com uma imagem menor que a de verdade daria uma resposta tranquilizadora e
        /// errada.
        /// </summary>
        private static (string Hero, string Logo) GerarArteLargaMestra()
        {
            var hero = Path.Combine(Caminhos.PastaCapas, "_mestra-hero.jpg");

            using (var arte = GeradorDeCapa.Gerar("Hero", 640, 207))
                GeradorDeCapa.SalvarJpeg(arte, hero);

            // O logo vai como PNG com alfa de verdade: é o formato que a fase 14 grava, e
            // PNG com transparência ocupa mais memória descomprimido que o jpg equivalente.
            var logo = Path.Combine(Caminhos.PastaCapas, "_mestra-logo.png");

            using (var bitmap = new Bitmap(320, 120, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    using (var pincel = new SolidBrush(Color.FromArgb(220, 240, 200, 60)))
                        g.FillEllipse(pincel, 10, 10, 300, 100);
                }

                bitmap.Save(logo, System.Drawing.Imaging.ImageFormat.Png);
            }

            return (hero, logo);
        }

        private static void AplicarArteLarga(Jogo jogo, (string Hero, string Logo) mestras)
        {
            var hero = jogo.Id + "_hero.jpg";
            var logo = jogo.Id + "_logo.png";

            File.Copy(mestras.Hero, Path.Combine(Caminhos.PastaCapas, hero), overwrite: true);
            File.Copy(mestras.Logo, Path.Combine(Caminhos.PastaCapas, logo), overwrite: true);

            jogo.HeroArquivo = hero;
            jogo.LogoArquivo = logo;
        }
    }
}
#endif   // DEBUG
