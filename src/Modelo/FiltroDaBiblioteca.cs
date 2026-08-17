using System;
using System.Collections.Generic;
using Mochila.Util;

namespace Mochila.Modelo
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
        /// rápido e não vou acertar acentuação enquanto filtro. Desde a fase 12 ela
        /// entende operadores — quem sabe lê-los é a <see cref="ConsultaDeBusca"/>.
        /// </summary>
        public static List<Jogo> Aplicar(IEnumerable<Jogo> jogos, string? busca,
                                         OrdenacaoBiblioteca ordenacao, bool somenteFavoritos)
            => Aplicar(jogos, ConsultaDeBusca.Analisar(busca), ordenacao, somenteFavoritos);

        public static List<Jogo> Aplicar(IEnumerable<Jogo> jogos, ConsultaDeBusca consulta,
                                         OrdenacaoBiblioteca ordenacao, bool somenteFavoritos)
        {
            var resultado = new List<Jogo>();

            foreach (var jogo in jogos)
            {
                if (somenteFavoritos && !jogo.Favorito) continue;
                if (!consulta.Casa(jogo)) continue;

                resultado.Add(jogo);
            }

            resultado.Sort(Comparador(ordenacao));
            return resultado;
        }

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
