using System;
using System.Collections.Generic;
using System.Text;

namespace Launcher.Util
{
    /// <summary>
    /// Tokenização de nomes de arquivo e de pasta, usada pelo scanner para medir
    /// o quanto o nome de um .exe parece com o nome da pasta do jogo.
    ///
    /// As regras de corte vieram de casos reais de um HD de jogos antigos:
    ///  - fronteira letra/dígito: sem isso "SPEED2.EXE" vira um token "speed2" só,
    ///    que não casa com o "Speed" de "Need for Speed - Underground 2" e o executável
    ///    certo perde os pontos de similaridade;
    ///  - camelCase: "DeadCells" -> "dead" + "cells";
    ///  - sigla seguida de palavra: "NFSCarbon" -> "nfs" + "carbon";
    ///  - tudo minúsculo no fim, porque nome em MAIÚSCULAS é comum em jogo antigo.
    /// </summary>
    public static class Tokens
    {
        /// <summary>
        /// Palavras que não dizem nada sobre a identidade do jogo. "pt" e "br" entram
        /// separados porque o "pt-br" da spec já chega quebrado pelo hífen.
        /// </summary>
        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.Ordinal)
        {
            "the", "a", "of", "edition", "black", "gold", "goty", "deluxe",
            "remastered", "repack", "portable", "pt", "br", "ptbr"
        };

        public static bool EhStopword(string token) => Stopwords.Contains(token);

        /// <summary>
        /// Quebra um texto em tokens minúsculos e sem acento. Mantém as stopwords —
        /// quem quiser sem elas usa <see cref="Significativos"/>.
        /// </summary>
        public static List<string> Dividir(string? texto)
        {
            var resultado = new List<string>();
            if (string.IsNullOrEmpty(texto)) return resultado;

            var limpo = Textos.RemoverAcentos(texto);
            var atual = new StringBuilder(limpo.Length);

            for (var i = 0; i < limpo.Length; i++)
            {
                var c = limpo[i];

                // Espaço, "_", "-", "." e qualquer outra pontuação fecham o token.
                if (!char.IsLetterOrDigit(c))
                {
                    Fechar(resultado, atual);
                    continue;
                }

                if (atual.Length > 0 && EhFronteira(limpo, i, atual[atual.Length - 1]))
                    Fechar(resultado, atual);

                atual.Append(c);
            }

            Fechar(resultado, atual);
            return resultado;
        }

        /// <summary>Tokens sem as stopwords — é o conjunto que vale pontos no scanner.</summary>
        public static HashSet<string> Significativos(string? texto)
        {
            var conjunto = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in Dividir(texto))
            {
                if (!Stopwords.Contains(token)) conjunto.Add(token);
            }
            return conjunto;
        }

        /// <summary>
        /// Todos os tokens colados, sem separador: "NFS Carbon" -> "nfscarbon".
        /// É contra essa forma que o bônus de sigla testa a subsequência.
        /// </summary>
        public static string Compactar(string? texto)
        {
            var sb = new StringBuilder();
            foreach (var token in Dividir(texto)) sb.Append(token);
            return sb.ToString();
        }

        /// <summary>
        /// true se as letras de <paramref name="agulha"/> aparecerem em ordem (não
        /// necessariamente juntas) dentro de <paramref name="palheiro"/>.
        /// Cobre siglas: "nfsc" ⊂ "nfscarbon", "gtaiv" ⊂ "grandtheftautoiv".
        /// </summary>
        public static bool EhSubsequencia(string? agulha, string? palheiro)
        {
            if (string.IsNullOrEmpty(agulha) || string.IsNullOrEmpty(palheiro)) return false;

            var i = 0;
            foreach (var c in palheiro!)
            {
                if (c == agulha![i] && ++i == agulha.Length) return true;
            }
            return false;
        }

        /// <summary>
        /// Decide se o caractere na posição <paramref name="i"/> começa um token novo,
        /// olhando para o último caractere já acumulado.
        /// </summary>
        private static bool EhFronteira(string texto, int i, char anterior)
        {
            var c = texto[i];

            // "SPEED2" -> "speed" + "2"; "2Fast" -> "2" + "fast".
            if (char.IsDigit(c) != char.IsDigit(anterior)) return true;

            // "DeadCells" -> "dead" + "cells".
            if (char.IsUpper(c) && char.IsLower(anterior)) return true;

            // "NFSCarbon" -> "nfs" + "carbon": maiúscula que inicia uma palavra minúscula.
            if (char.IsUpper(c) && char.IsUpper(anterior) &&
                i + 1 < texto.Length && char.IsLower(texto[i + 1]))
            {
                return true;
            }

            return false;
        }

        private static void Fechar(List<string> destino, StringBuilder atual)
        {
            if (atual.Length == 0) return;
            destino.Add(atual.ToString().ToLowerInvariant());
            atual.Length = 0;
        }
    }
}
