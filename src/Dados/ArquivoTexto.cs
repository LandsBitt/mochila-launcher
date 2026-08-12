using System;
using System.IO;
using System.Text;

namespace Launcher.Dados
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

        public static string Ler(string caminho) => File.ReadAllText(caminho, Utf8SemBom);

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
