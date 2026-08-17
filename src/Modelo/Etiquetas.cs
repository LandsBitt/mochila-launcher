using System;
using System.Collections.Generic;
using Mochila.Util;

namespace Mochila.Modelo
{
    /// <summary>Uma tag e quantos jogos a usam. Ordena a faixa de tags da barra.</summary>
    public sealed class ContagemDeTag
    {
        public ContagemDeTag(string tag, int quantidade)
        {
            Tag = tag;
            Quantidade = quantidade;
        }

        public string Tag { get; }

        public int Quantidade { get; }
    }

    /// <summary>
    /// As regras das tags, num lugar só.
    ///
    /// Duas decisões que valem para tudo daqui para a frente:
    ///
    /// - <b>Tag não tem espaço.</b> A busca separa termos por espaço, então uma tag
    ///   "mundo aberto" viraria dois filtros e nunca casaria com nada. Espaço vira hífen
    ///   na hora de gravar, e aí <c>#mundo-aberto</c> funciona como qualquer outra.
    /// - <b>Comparação sem acento e sem caixa.</b> Eu digito rápido enquanto filtro e não
    ///   vou acertar acentuação; a tag continua gravada como eu escrevi ("ação"), mas
    ///   <c>#acao</c> a encontra.
    /// </summary>
    public static class Etiquetas
    {
        /// <summary>
        /// Deixa a tag na forma gravável: sem espaço em volta, minúscula, e espaço interno
        /// virado hífen. Devolve null quando não sobrou nada.
        /// </summary>
        public static string? Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;

            var partes = texto!.Trim().ToLowerInvariant()
                               .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var junta = string.Join("-", partes);

            // "#corrida" digitado com o prefixo é a mesma tag que "corrida".
            junta = junta.TrimStart('#').Trim('-');

            return junta.Length == 0 ? null : junta;
        }

        /// <summary>A forma usada para comparar: normalizada e sem acento.</summary>
        public static string ParaComparar(string? tag)
            => Textos.RemoverAcentos(Normalizar(tag) ?? "").ToLowerInvariant();

        /// <summary>true se o jogo tem uma tag que começa pelo texto pedido.</summary>
        public static bool Casa(IEnumerable<string> tagsDoJogo, string pedida)
        {
            var alvo = ParaComparar(pedida);
            if (alvo.Length == 0) return false;

            foreach (var tag in tagsDoJogo)
            {
                // Prefixo, e não igualdade: a busca por texto solto já é "contém", e uma
                // regra diferente só para tag faria a lista sumir enquanto eu digito
                // "#cor" a caminho de "#corrida".
                if (ParaComparar(tag).StartsWith(alvo, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Acrescenta a tag se ela ainda não existir. Devolve true se mudou algo.</summary>
        public static bool Acrescentar(List<string> tags, string? texto)
        {
            var tag = Normalizar(texto);
            if (tag is null) return false;

            var alvo = ParaComparar(tag);
            foreach (var existente in tags)
            {
                if (ParaComparar(existente) == alvo) return false;
            }

            tags.Add(tag);
            return true;
        }

        /// <summary>Tira a tag. Devolve true se mudou algo.</summary>
        public static bool Remover(List<string> tags, string? texto)
        {
            var alvo = ParaComparar(texto);
            if (alvo.Length == 0) return false;

            var antes = tags.Count;

            for (var i = tags.Count - 1; i >= 0; i--)
            {
                if (ParaComparar(tags[i]) == alvo) tags.RemoveAt(i);
            }

            return tags.Count != antes;
        }

        /// <summary>
        /// As tags do acervo, da mais usada para a menos usada e, em empate, em ordem
        /// alfabética — a faixa da barra não pode dançar a cada F5.
        /// </summary>
        public static List<ContagemDeTag> PorFrequencia(IEnumerable<Jogo> jogos)
        {
            var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
            var comoEscritas = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var jogo in jogos)
            {
                foreach (var tag in jogo.Tags)
                {
                    var chave = ParaComparar(tag);
                    if (chave.Length == 0) continue;

                    contagem[chave] = contagem.TryGetValue(chave, out var quantas) ? quantas + 1 : 1;

                    // A primeira grafia vista é a que aparece na faixa: "ação" e "acao"
                    // contam junto, mas a tela mostra uma só.
                    if (!comoEscritas.ContainsKey(chave)) comoEscritas[chave] = Normalizar(tag) ?? chave;
                }
            }

            var lista = new List<ContagemDeTag>(contagem.Count);
            foreach (var par in contagem) lista.Add(new ContagemDeTag(comoEscritas[par.Key], par.Value));

            lista.Sort((a, b) =>
            {
                var porQuantidade = b.Quantidade.CompareTo(a.Quantidade);
                return porQuantidade != 0
                    ? porQuantidade
                    : string.Compare(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase);
            });

            return lista;
        }
    }
}
