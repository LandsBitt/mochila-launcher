using System;
using System.IO;
using System.Text;

namespace Mochila.Dados
{
    /// <summary>
    /// Leitura e escrita dos arquivos de estado do launcher.
    ///
    /// A escrita é atômica de propósito: a biblioteca é salva antes de cada jogo abrir,
    /// e um HD externo pode ser arrancado no meio da gravação. Melhor perder a última
    /// alteração do que ficar com um JSON pela metade.
    /// </summary>
    public static class ArquivoTexto
    {
        /// <summary>UTF-8 sem BOM — BOM atrapalha quem abre o JSON em editor simples.</summary>
        private static readonly UTF8Encoding Utf8SemBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static string Ler(string caminho)
        {
#if DEBUG
            Contabilizar(caminho);
#endif
            return File.ReadAllText(caminho, Utf8SemBom);
        }

#if DEBUG
        // ---- Contador de leituras (só Debug) ---------------------------------------------
        //
        // Existe por uma regra da fase 13: "sessoes.json NÃO é lido na abertura do
        // launcher". Provar isso pede saber quantas vezes cada arquivo foi lido, e como
        // toda leitura de estado passa por aqui, um contador nesta classe estática basta.
        // A alternativa seria inventar uma abstração de sistema de arquivos inteira para
        // provar uma linha — a spec proíbe explicitamente, e ela está certa.

        private static readonly object TravaDoContador = new object();

        private static readonly System.Collections.Generic.Dictionary<string, int> LeiturasPorArquivo =
            new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static void Contabilizar(string caminho)
        {
            var nome = Path.GetFileName(caminho);
            if (string.IsNullOrEmpty(nome)) return;

            lock (TravaDoContador)
            {
                LeiturasPorArquivo.TryGetValue(nome, out var quantas);
                LeiturasPorArquivo[nome] = quantas + 1;
            }
        }

        internal static void ZerarContadorDeLeituras()
        {
            lock (TravaDoContador) LeiturasPorArquivo.Clear();
        }

        /// <summary>Quantas vezes um arquivo (pelo nome, sem pasta) foi lido desde o último zero.</summary>
        internal static int LeiturasDe(string nomeDoArquivo)
        {
            lock (TravaDoContador)
                return LeiturasPorArquivo.TryGetValue(nomeDoArquivo, out var quantas) ? quantas : 0;
        }
#endif

        public static bool Existe(string? caminho) => !string.IsNullOrEmpty(caminho) && File.Exists(caminho);

        /// <summary>
        /// Grava num .tmp ao lado e só então troca pelo arquivo final, guardando a versão
        /// anterior em .bak. Se o sistema de arquivos não suportar File.Replace (alguns
        /// pendrives), cai para apagar-e-mover.
        /// </summary>
        public static void EscreverAtomico(string caminho, string conteudo)
        {
            var pasta = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

            var temporario = caminho + ".tmp";
            var backup = caminho + ".bak";

            File.WriteAllText(temporario, conteudo, Utf8SemBom);

            if (!File.Exists(caminho))
            {
                File.Move(temporario, caminho);
                return;
            }

            try
            {
                File.Replace(temporario, caminho, backup, ignoreMetadataErrors: true);
            }
            catch (Exception)
            {
                // Fallback para sistemas de arquivo sem suporte a ReplaceFile.
                TentarApagar(backup);
                TentarMover(caminho, backup);
                TentarApagar(caminho);
                File.Move(temporario, caminho);
            }
        }

        private static void TentarApagar(string caminho)
        {
            try
            {
                if (File.Exists(caminho)) File.Delete(caminho);
            }
            catch (Exception)
            {
                // Sem backup é aceitável; sem o arquivo principal, não — e esse caso o File.Move acusa.
            }
        }

        private static void TentarMover(string origem, string destino)
        {
            try
            {
                if (File.Exists(origem)) File.Move(origem, destino);
            }
            catch (Exception)
            {
            }
        }
    }
}
