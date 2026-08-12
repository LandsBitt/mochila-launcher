// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using Launcher.Dados;
using Launcher.Modelo;
using Launcher.UI;

namespace Launcher.Diagnostico
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
        /// <summary>Sandbox criada ao lado do Launcher.exe. Some no fim.</summary>
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
        public static string Montar(int quantidade)
        {
            if (quantidade < 1) quantidade = 1;

            var sandbox = Path.Combine(Caminhos.PastaBase, NomeDaSandbox, "hd");
            Directory.CreateDirectory(sandbox);

            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();

            var mestras = GerarCapasMestras();
            var biblioteca = new Biblioteca();
            biblioteca.PastasEscaneadas.Add("Jogos");

            for (var i = 0; i < quantidade; i++)
            {
                biblioteca.Jogos.Add(CriarJogo(i, biblioteca, mestras));
            }

            biblioteca.Salvar();
            new Config().Salvar();

            return sandbox;
        }

        /// <summary>Apaga a sandbox e devolve a raiz portátil para a pasta do Launcher.exe.</summary>
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

            return new Jogo
            {
                Id = id,
                Titulo = titulo,
                ExecutavelRelativo = relativo,
                CapaArquivo = capa,
                SegundosJogados = segundos,
                UltimaVezJogado = diasAtras is null ? null : DateTime.UtcNow.AddDays(-diasAtras.Value),
                Favorito = indice % UmFavoritoACada == 0
            };
        }

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
    }
}
#endif   // DEBUG
