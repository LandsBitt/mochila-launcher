using System;
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
        /// O conjunto de caracteres que um <c>id</c> de jogo pode ter: <c>^[a-z0-9_-]+$</c>.
        ///
        /// O invariante é o <b>conjunto</b>, não "ser idêntico à saída de <see cref="Slug"/>":
        /// o id é nome de arquivo em dois lugares (<c>&lt;id&gt;_thumb.jpg</c> e a capa), e
        /// vira nome de <b>pasta</b> quando os saves portáteis chegarem (planejamento interno).
        /// Dois-pontos num nome de arquivo no Windows vira alternate data stream, e a falha
        /// não é bonita: é estranha.
        ///
        /// O sublinhado entra porque a v3 (emuladores) vai gerar id composto — e a regra
        /// escrita assim agora não precisa ser reaberta lá.
        /// </summary>
        public static bool EhIdValido(string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            foreach (var c in id!)
            {
                var permitido = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
                if (!permitido) return false;
            }

            // Ponto e espaço já caem no conjunto acima, inclusive no fim — a regra do Windows
            // de que nome não termina em ponto nem espaço fica satisfeita por construção.
            return !EhNomeReservadoDoWindows(id!);
        }

        /// <summary>
        /// Os nomes que o Windows reserva para dispositivos desde o DOS. <c>con</c>,
        /// <c>nul</c>, <c>prn</c>, <c>aux</c>, <c>com1</c>–<c>com9</c> e <c>lpt1</c>–<c>lpt9</c>
        /// não podem ser nome de arquivo <b>nem de pasta</b>, e o <c>id</c> é nome de
        /// arquivo hoje (<c>&lt;id&gt;_thumb.jpg</c>) e de pasta quando os saves portáteis
        /// chegarem.
        ///
        /// A falha é do tipo que não se entende olhando: criar a pasta devolve acesso
        /// negado, e um jogo chamado "Con" (existe: <i>Con Man</i>) chegaria nisso sozinho.
        /// </summary>
        public static bool EhNomeReservadoDoWindows(string? nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;

            // A reserva vale para o nome antes do primeiro ponto — e id não tem ponto, mas a
            // regra fica escrita do jeito certo para quando alguém reaproveitar isto.
            var baseDoNome = nome!.Split('.')[0].ToLowerInvariant();

            switch (baseDoNome)
            {
                case "con":
                case "prn":
                case "aux":
                case "nul":
                    return true;
            }

            if (baseDoNome.Length == 4 &&
                (baseDoNome.StartsWith("com", StringComparison.Ordinal) ||
                 baseDoNome.StartsWith("lpt", StringComparison.Ordinal)))
            {
                return baseDoNome[3] is >= '1' and <= '9';
            }

            return false;
        }

        /// <summary>
        /// Conserta um id que veio de fora (arquivo editado à mão, biblioteca de outra
        /// ferramenta) para caber no conjunto permitido, preservando quem já é válido.
        ///
        /// Diferente de <see cref="Slug"/> em um ponto que importa: o sublinhado sobrevive,
        /// senão todo id composto seria reescrito na primeira leitura.
        /// </summary>
        public static string SanearId(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (EhIdValido(id)) return id!;

            var semAcento = RemoverAcentos(id).ToLowerInvariant();
            var sb = new StringBuilder(semAcento.Length);
            var hifenPendente = false;

            foreach (var c in semAcento)
            {
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
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

            var saneado = sb.ToString();

            // Nome de dispositivo do DOS não pode virar pasta nem arquivo. O sufixo é feio e
            // é uma vez só na vida daquele jogo — melhor que uma pasta que se recusa a existir.
            if (EhNomeReservadoDoWindows(saneado)) saneado += "-jogo";

            return saneado;
        }

        /// <summary>
        /// <b>Sanear é idempotente:</b> sanear o que já foi saneado devolve a mesma coisa.
        ///
        /// Não é preciosismo. O <c>id</c> é saneado em toda leitura da biblioteca, e ele
        /// nomeia a capa e o thumbnail daquele jogo. Se a segunda passada mudasse o
        /// resultado, cada abertura do launcher apontaria para um arquivo diferente e a arte
        /// "sumiria" a cada vez — sem erro nenhum na tela.
        /// </summary>
        public static bool SaneamentoEhEstavel(string? id) => SanearId(SanearId(id)) == SanearId(id);

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
