using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mochila.Scanner
{
    /// <summary>
    /// Transforma o nome da pasta no título proposto do jogo:
    /// tira tags de repack e versão, troca "_" e "." por espaço e aplica Title Case.
    ///
    /// É só uma proposta — a janela de revisão (fase 3) deixa eu corrigir antes de gravar.
    /// </summary>
    public static class TituloDePasta
    {
        private const RegexOptions Opcoes =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        /// <summary>
        /// Conteúdo entre [] ou () só é removido quando é claramente tag de scene/repack.
        /// Apagar todo parêntese cegamente destruiria coisas legítimas como
        /// "(Black Edition)" ou "(Director's Cut)".
        /// </summary>
        private static readonly Regex TagEntreDelimitadores = new Regex(
            @"[\[\(\{]\s*(fitgirl|dodi|elamigos|el\s*amigos|rg\s*mechanics|codex|plaza|skidrow|reloaded|razor1911|" +
            @"gog|goggames|repack|rip|portable|pt[\s\-_]?br|ptbr|dublado|traduzido|multi\d*|" +
            @"x64|x86|win64|win32|v?\d+(\.\d+)+[a-z0-9]*)\s*[\]\)\}]", Opcoes);

        /// <summary>Versão solta no meio do nome: "v1.2.3", "v1.02b".</summary>
        private static readonly Regex VersaoSolta = new Regex(@"(?<![a-z0-9])v\d+(\.\d+)+[a-z0-9]*(?![a-z0-9])", Opcoes);

        /// <summary>Tags soltas, sem delimitador: "... REPACK", "Jogo PT-BR".</summary>
        private static readonly Regex TagSolta = new Regex(
            @"(?<![a-z0-9])(repack|fitgirl|dodi|elamigos|codex|plaza|skidrow|reloaded|" +
            @"pt[\-_]br|ptbr|multi\d+)(?![a-z0-9])", Opcoes);

        private static readonly Regex EspacosSeguidos = new Regex(@"\s{2,}", Opcoes);

        /// <summary>Minúsculas em Title Case, exceto quando é a primeira palavra.</summary>
        private static readonly HashSet<string> PalavrasMenores = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into",
            "nor", "of", "on", "or", "the", "to", "vs", "with",
            "e", "de", "da", "do", "das", "dos", "em", "no", "na", "para", "com"
        };

        public static string Limpar(string? nomeDaPasta)
        {
            if (string.IsNullOrWhiteSpace(nomeDaPasta)) return "";

            var texto = nomeDaPasta!.Trim();

            texto = TagEntreDelimitadores.Replace(texto, " ");
            texto = VersaoSolta.Replace(texto, " ");
            texto = TagSolta.Replace(texto, " ");

            // "_" e "." viram espaço; o hífen fica, porque separa subtítulo de verdade
            // ("Need for Speed - Underground 2").
            texto = texto.Replace('_', ' ').Replace('.', ' ');

            texto = EspacosSeguidos.Replace(texto, " ").Trim();
            texto = texto.Trim(' ', '-', '–', ',', ';');

            // Sobrou nada depois da limpeza (pasta chamada só "v1.2")? Melhor o nome cru.
            if (texto.Length == 0) return nomeDaPasta.Trim();

            return AplicarTitleCase(texto);
        }

        private static string AplicarTitleCase(string texto)
        {
            var palavras = texto.Split(' ');

            // Pasta gritada ("GRAND THEFT AUTO III") volta ao normal por inteiro. Só vale
            // preservar maiúsculas quando elas destoam do resto ("NFS Carbon").
            var tudoGritado = EstaTudoEmMaiusculas(palavras);

            var sb = new StringBuilder(texto.Length);

            for (var i = 0; i < palavras.Length; i++)
            {
                var palavra = palavras[i];
                if (palavra.Length == 0) continue;

                if (sb.Length > 0) sb.Append(' ');

                if (ManterComoEsta(palavra, tudoGritado)) sb.Append(palavra);
                else if (i > 0 && PalavrasMenores.Contains(palavra)) sb.Append(palavra.ToLowerInvariant());
                else sb.Append(Capitalizar(palavra));
            }

            return sb.ToString();
        }

        private static bool EstaTudoEmMaiusculas(string[] palavras)
        {
            var temPalavraComLetra = false;

            foreach (var palavra in palavras)
            {
                foreach (var c in palavra)
                {
                    if (char.IsLower(c)) return false;
                    if (char.IsLetter(c)) temPalavraComLetra = true;
                }
            }
            return temPalavraComLetra;
        }

        /// <summary>Numeração romana: "II", "IV", "XIII".</summary>
        private static readonly Regex NumeroRomano = new Regex(@"^[IVXLCDM]{1,8}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Siglas curtas e numeração romana ficam como estão: "NFS", "GTA", "III".
        /// Sem isso o Title Case estraga o nome ("Nfs Carbon"). Palavra longa em
        /// maiúsculas é só pasta gritada ("NEED FOR SPEED") e volta ao normal.
        /// </summary>
        private static bool ManterComoEsta(string palavra, bool tudoGritado)
        {
            var temLetra = false;
            foreach (var c in palavra)
            {
                if (char.IsLower(c)) return false;
                if (char.IsLetter(c)) temLetra = true;
            }
            if (!temLetra) return false;

            var soLetras = SomenteLetras(palavra);
            if (NumeroRomano.IsMatch(soLetras)) return true;

            return !tudoGritado && palavra.Length <= 4;
        }

        private static string SomenteLetras(string palavra)
        {
            var sb = new StringBuilder(palavra.Length);
            foreach (var c in palavra)
            {
                if (char.IsLetter(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Deixa maiúscula a primeira LETRA, não o primeiro caractere: em "(Enhanced" o
        /// primeiro caractere é o parêntese, e capitalizar ele deixaria "(enhanced".
        /// </summary>
        private static string Capitalizar(string palavra)
        {
            var sb = new StringBuilder(palavra.Length);
            var jaAchouLetra = false;

            foreach (var c in palavra)
            {
                if (!jaAchouLetra && char.IsLetter(c))
                {
                    sb.Append(char.ToUpper(c, CultureInfo.InvariantCulture));
                    jaAchouLetra = true;
                }
                else
                {
                    sb.Append(jaAchouLetra ? char.ToLowerInvariant(c) : c);
                }
            }
            return sb.ToString();
        }
    }
}
