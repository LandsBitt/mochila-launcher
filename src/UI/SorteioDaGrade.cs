using System;
using System.Collections.Generic;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// A roleta: escolhe um jogo entre os que estão à vista.
    ///
    /// Fica separado da janela porque é regra, não desenho — e regra dá para testar sem
    /// abrir janela nenhuma. Duas regras, as duas por experiência de uso:
    ///
    /// - sorteia **dentro do filtro atual**. Se eu filtrei por "corrida", o acaso é entre
    ///   corridas; sortear na biblioteca inteira ignoraria o que eu acabei de dizer que
    ///   queria;
    /// - **nunca** devolve card marcado como não encontrado. A roleta existe para eu jogar
    ///   agora, e um jogo que não abre é o oposto disso.
    /// </summary>
    public static class SorteioDaGrade
    {
        /// <summary>Um jogo dos visíveis, ou null quando nenhum deles pode ser aberto.</summary>
        public static Jogo? Escolher(IReadOnlyList<Jogo> visiveis, Random sorteio)
        {
            if (visiveis is null || visiveis.Count == 0) return null;
            if (sorteio is null) throw new ArgumentNullException(nameof(sorteio));

            var candidatos = new List<Jogo>(visiveis.Count);

            foreach (var jogo in visiveis)
            {
                if (jogo.ExecutavelExiste()) candidatos.Add(jogo);
            }

            return candidatos.Count == 0 ? null : candidatos[sorteio.Next(candidatos.Count)];
        }
    }
}
