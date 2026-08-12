using System;
using System.Collections.Generic;
using Launcher.Util;

namespace Launcher.Modelo
{
    /// <summary>
    /// Busca, ordenação e filtro de favoritos. É lógica pura sobre a lista de jogos —
    /// a grade só desenha o que sai daqui.
    /// </summary>
    public static class FiltroDaBiblioteca
    {
        /// <summary>
        /// Aplica busca, favoritos e ordenação, nessa ordem.
        ///
        /// A busca é por termos: "nfs carbon" acha "NFS Carbon" e também
        /// "Carbon (NFS)". Sem acento e sem ligar para maiúsculas, porque eu digito
        /// rápido e não vou acertar acentuação enquanto filtro.
        /// </summary>
        public static List<Jogo> Aplicar(IEnumerable<Jogo> jogos, string? busca,
                                         OrdenacaoBiblioteca ordenacao, bool somenteFavoritos)
        {
            var termos = Termos(busca);
            var resultado = new List<Jogo>();

            foreach (var jogo in jogos)
            {
                if (somenteFavoritos && !jogo.Favorito) continue;
                if (!Casa(jogo, termos)) continue;

                resultado.Add(jogo);
            }

            resultado.Sort(Comparador(ordenacao));
            return resultado;
        }

        /// <summary>Quebra o texto digitado em termos normalizados.</summary>
        public static List<string> Termos(string? busca)
        {
            var termos = new List<string>();
            if (string.IsNullOrWhiteSpace(busca)) return termos;

            foreach (var pedaco in busca!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalizado = Normalizar(pedaco);
                if (normalizado.Length > 0) termos.Add(normalizado);
            }
            return termos;
        }

        /// <summary>Todo termo digitado precisa aparecer no título.</summary>
        public static bool Casa(Jogo jogo, List<string> termos)
        {
            if (termos.Count == 0) return true;

            var titulo = Normalizar(jogo.Titulo);
            foreach (var termo in termos)
            {
                if (titulo.IndexOf(termo, StringComparison.Ordinal) < 0) return false;
            }
            return true;
        }

        private static string Normalizar(string? texto)
            => Textos.RemoverAcentos(texto).ToLowerInvariant();

        private static Comparison<Jogo> Comparador(OrdenacaoBiblioteca ordenacao) => ordenacao switch
        {
            OrdenacaoBiblioteca.MaisJogados => (a, b) =>
            {
                var porMinutos = b.SegundosJogados.CompareTo(a.SegundosJogados);
                return porMinutos != 0 ? porMinutos : PorTitulo(a, b);
            },

            // Nunca jogado vai para o fim, não para o começo.
            OrdenacaoBiblioteca.JogadosRecentemente => (a, b) =>
            {
                if (a.UltimaVezJogado is null && b.UltimaVezJogado is null) return PorTitulo(a, b);
                if (a.UltimaVezJogado is null) return 1;
                if (b.UltimaVezJogado is null) return -1;

                var porData = b.UltimaVezJogado.Value.CompareTo(a.UltimaVezJogado.Value);
                return porData != 0 ? porData : PorTitulo(a, b);
            },

            _ => PorTitulo
        };

        private static int PorTitulo(Jogo a, Jogo b)
            => string.Compare(a.Titulo, b.Titulo, StringComparison.CurrentCultureIgnoreCase);
    }
}
