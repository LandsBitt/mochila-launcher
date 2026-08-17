using System.Globalization;
using System.Text;

namespace Mochila.Util
{
    /// <summary>
    /// Utilitários de texto compartilhados. A normalização daqui é reaproveitada
    /// pelo scanner (fase 2), que compara nome de exe com nome de pasta.
    /// </summary>
    public static class Textos
    {
        /// <summary>"Ação" -> "Acao". Decompõe e joga fora os acentos combinantes.</summary>
        public static string RemoverAcentos(string? texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";

            var decomposto = texto!.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposto.Length);

            foreach (var c in decomposto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Gera um slug ASCII: minúsculas, sem acento, só letras/dígitos, hífen no lugar
        /// do resto. Usado nos ids dos jogos, que viram nome de arquivo de capa e cache.
        /// </summary>
        public static string Slug(string? texto)
        {
            var semAcento = RemoverAcentos(texto).ToLowerInvariant();

            var sb = new StringBuilder(semAcento.Length);
            var hifenPendente = false;

            foreach (var c in semAcento)
            {
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    if (hifenPendente && sb.Length > 0) sb.Append('-');
                    hifenPendente = false;
                    sb.Append(c);
                }
                else
                {
                    hifenPendente = true;
                }
            }
            return sb.ToString();
        }
    }
}
