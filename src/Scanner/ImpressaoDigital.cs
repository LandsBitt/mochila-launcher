using System;
using System.Globalization;
using System.IO;

namespace Mochila.Scanner
{
    /// <summary>
    /// A identidade de um executável, para reconhecer um jogo que mudou de pasta.
    ///
    /// O caminho é uma identidade ruim: reorganizar <c>Jogos\Antigos\</c> — que é
    /// exatamente o que se faz num HD desses — faz o rescan criar um jogo novo e abandonar
    /// o velho, com tempo jogado, capa e favorito junto. O executável é uma identidade boa:
    /// renomear a pasta não muda um byte dele.
    ///
    /// Formato: <c>nome|tamanho|hash dos primeiros 64 KB</c>.
    ///
    /// Não é hash criptográfico e não precisa ser — é identidade, não assinatura. FNV-1a
    /// de 64 bits, implementado à mão em vinte linhas, porque <c>SHA1Managed</c> lança em
    /// máquina com FIPS ligado e um fallback entre dois algoritmos seria pior que inútil:
    /// as impressões novas sairiam num algoritmo, nenhuma casaria com as já gravadas no
    /// outro, e como o caminho velho é justamente o que sumiu não haveria como recalcular
    /// a antiga. O reconhecimento pararia de funcionar naquela máquina, em silêncio.
    /// </summary>
    public static class ImpressaoDigital
    {
        /// <summary>
        /// 64 KB pegam o cabeçalho PE e o início do código — sobra para separar
        /// executáveis diferentes. Ler o arquivo inteiro seria 6 MB × 200 jogos por scan,
        /// para responder a mesma pergunta.
        /// </summary>
        public const int BytesLidos = 65536;

        // Constantes oficiais do FNV-1a de 64 bits. Ficam aqui, e não "no algoritmo",
        // porque FNV-1 (multiplica e depois XOR) e a versão de 32 bits produzem outro
        // valor — e trocar sem querer invalidaria toda impressão já gravada.
        private const ulong BaseDoFnv = 14695981039346656037;
        private const ulong PrimoDoFnv = 1099511628211;

        /// <summary>
        /// FNV-1a de 64 bits: XOR **antes** da multiplicação. Estável entre execuções,
        /// entre máquinas e entre versões — é o que permite comparar a impressão gravada
        /// meses atrás com a calculada agora.
        /// </summary>
        public static ulong Hash(byte[] dados, int quantidade)
        {
            if (dados is null) throw new ArgumentNullException(nameof(dados));

            var limite = Math.Min(quantidade, dados.Length);
            var hash = BaseDoFnv;

            for (var i = 0; i < limite; i++)
            {
                hash ^= dados[i];
                hash *= PrimoDoFnv;     // overflow de ulong é o comportamento esperado aqui
            }

            return hash;
        }

        /// <summary>Monta a chave a partir das partes. Público para o teste não precisar de arquivo.</summary>
        public static string Montar(string nomeDoArquivo, long tamanho, ulong hash)
            => string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:x16}",
                             (nomeDoArquivo ?? "").ToLowerInvariant(), tamanho, hash);

        /// <summary>
        /// Calcula a impressão de um arquivo. Devolve null quando não dá para ler —
        /// arquivo em uso, permissão negada, HD que sumiu no meio. Nulo significa "sem
        /// impressão", e o jogo cai na religação manual; nunca aborta o scan.
        /// </summary>
        public static string? De(string? caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return null;

            try
            {
                var info = new FileInfo(caminho!);
                if (!info.Exists) return null;

                var buffer = new byte[BytesLidos];
                int lidos;

                // FileShare.ReadWrite: o jogo pode estar aberto na hora do scan.
                using (var fluxo = new FileStream(caminho!, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete, 4096))
                {
                    lidos = LerTudoQueDer(fluxo, buffer);
                }

                // Arquivo menor que 64 KB: hasheia o que existir. O tamanho no meio da
                // chave continua desempatando.
                return Montar(Path.GetFileName(caminho!), info.Length, Hash(buffer, lidos));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Um Read só não garante o buffer cheio: o contrato de Stream permite devolver
        /// menos, e num HD externo isso acontece. Sem o laço, a mesma cópia do mesmo
        /// arquivo poderia gerar impressões diferentes.
        /// </summary>
        private static int LerTudoQueDer(Stream fluxo, byte[] buffer)
        {
            var total = 0;

            while (total < buffer.Length)
            {
                var lidos = fluxo.Read(buffer, total, buffer.Length - total);
                if (lidos <= 0) break;
                total += lidos;
            }

            return total;
        }
    }
}
