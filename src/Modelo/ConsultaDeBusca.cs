using System;
using System.Collections.Generic;
using System.Globalization;
using Mochila.Util;

namespace Mochila.Modelo
{
    /// <summary>
    /// O que eu digitei na barra de busca, já entendido.
    ///
    /// A linguagem inteira cabe em três prefixos e texto solto:
    ///
    /// <code>
    /// #corrida        tag
    /// @zerado         status
    /// *4              nota 4 ou mais
    /// most wanted     texto no título
    /// </code>
    ///
    /// Tudo combina com tudo, e tudo é E: <c>#corrida @zerado *4</c> é "corrida, zerado e
    /// com nota alta". Nada casando devolve lista vazia — é resultado, não erro.
    ///
    /// <b>Tag e status casam por prefixo</b>, de propósito: a busca por texto solto já é
    /// "contém", e uma regra diferente só para os operadores faria a grade esvaziar
    /// enquanto eu digito <c>#cor</c> a caminho de <c>#corrida</c>.
    ///
    /// Operador sem conteúdo (<c>#</c>, <c>@</c>, <c>*</c> sozinhos) é ignorado: é o
    /// estado normal do primeiro caractere que eu digito, e esvaziar a tela nesse instante
    /// seria hostil.
    /// </summary>
    public sealed class ConsultaDeBusca
    {
        private static readonly char[] Separadores = { ' ', '\t' };

        public List<string> Termos { get; } = new List<string>();

        public List<string> Tags { get; } = new List<string>();

        /// <summary>
        /// Status pedidos, como texto do arquivo ("quero-jogar"). Guardado como texto, e
        /// não como enum, para <c>@zer</c> continuar sendo uma busca por prefixo válida
        /// em vez de virar "status desconhecido".
        /// </summary>
        public List<string> Status { get; } = new List<string>();

        /// <summary>0 = sem filtro de nota.</summary>
        public int NotaMinima { get; private set; }

        public bool Vazia => Termos.Count == 0 && Tags.Count == 0 && Status.Count == 0 && NotaMinima == 0;

        /// <summary>true se a consulta usa algum operador (e não só texto de título).</summary>
        public bool TemOperador => Tags.Count > 0 || Status.Count > 0 || NotaMinima > 0;

        public static ConsultaDeBusca Analisar(string? texto)
        {
            var consulta = new ConsultaDeBusca();
            if (string.IsNullOrWhiteSpace(texto)) return consulta;

            foreach (var pedaco in texto!.Split(Separadores, StringSplitOptions.RemoveEmptyEntries))
            {
                var resto = pedaco.Substring(1);

                switch (pedaco[0])
                {
                    case '#':
                        if (Etiquetas.Normalizar(resto) is { } tag) consulta.Tags.Add(tag);
                        break;

                    case '@':
                        var status = Normalizar(resto);
                        if (status.Length > 0) consulta.Status.Add(status);
                        break;

                    case '*':
                        if (int.TryParse(resto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nota))
                            consulta.NotaMinima = Math.Max(0, Math.Min(5, nota));
                        break;

                    default:
                        var termo = Normalizar(pedaco);
                        if (termo.Length > 0) consulta.Termos.Add(termo);
                        break;
                }
            }

            return consulta;
        }

        public bool Casa(Jogo jogo)
        {
            if (jogo is null) return false;

            if (Termos.Count > 0)
            {
                var titulo = Normalizar(jogo.Titulo);
                foreach (var termo in Termos)
                {
                    if (titulo.IndexOf(termo, StringComparison.Ordinal) < 0) return false;
                }
            }

            foreach (var tag in Tags)
            {
                if (!Etiquetas.Casa(jogo.Tags, tag)) return false;
            }

            // Um jogo tem um status só, então dois @ diferentes não casam com nada. É a
            // resposta certa: eu pedi duas coisas que não podem ser verdade juntas.
            var doJogo = Estados.ParaTexto(jogo.Status);
            foreach (var pedido in Status)
            {
                if (!doJogo.StartsWith(pedido, StringComparison.Ordinal)) return false;
            }

            return jogo.Nota >= NotaMinima;
        }

        private static string Normalizar(string? texto)
            => Textos.RemoverAcentos(texto).ToLowerInvariant().Trim();
    }
}
