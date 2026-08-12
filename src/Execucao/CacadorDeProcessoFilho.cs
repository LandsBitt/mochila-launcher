using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Launcher.Execucao
{
    /// <summary>
    /// Procura o processo que assumiu o lugar do exe que eu lancei.
    ///
    /// O caso: jogo com launcher próprio (Riot, Ubisoft, EA, e vários indies) abre a
    /// janelinha dele, dispara o jogo de verdade e MORRE. Do lado de cá, o processo que
    /// eu acompanhava terminou em 2 segundos — e se o launcher reaparecer nessa hora, ele
    /// vem por cima de um jogo que está subindo em tela cheia. Isso é pior que o problema
    /// original.
    ///
    /// A busca é uma varredura só, feita uma vez depois da saída rápida: nada de ficar
    /// vigiando processo. O critério é o executável estar dentro da pasta do jogo — o
    /// filho quase sempre mora lá (Binaries\Win64\jogo.exe, game\jogo.exe).
    /// </summary>
    public static class CacadorDeProcessoFilho
    {
        /// <summary>
        /// Ids de processo vivos agora. Tirado antes de lançar: o que já existia não é
        /// filho de nada, e adotar o jogo que eu já tinha aberto seria pior que não adotar.
        /// </summary>
        public static HashSet<int> FotografarProcessos()
        {
            var ids = new HashSet<int>();

            foreach (var processo in Process.GetProcesses())
            {
                try
                {
                    ids.Add(processo.Id);
                }
                catch (Exception)
                {
                    // Processo morreu entre a listagem e a leitura: não interessa mesmo.
                }
                finally
                {
                    processo.Dispose();
                }
            }
            return ids;
        }

        /// <summary>
        /// Devolve o primeiro processo novo cujo executável esteja dentro da pasta do
        /// jogo, ou null. Quem receber o processo fica responsável por descartá-lo.
        /// </summary>
        public static Process? Procurar(string? pastaDoJogo, HashSet<int> idsAnteriores)
        {
            if (string.IsNullOrEmpty(pastaDoJogo)) return null;

            var prefixo = Normalizar(pastaDoJogo!);
            Process? encontrado = null;

            foreach (var processo in Process.GetProcesses())
            {
                var serve = false;

                try
                {
                    // Só o que nasceu depois do lançamento, e só uma vez.
                    if (encontrado is null && !idsAnteriores.Contains(processo.Id))
                        serve = MoraNaPasta(processo, prefixo);
                }
                catch (Exception)
                {
                    // Processo do sistema, de outro usuário ou já encerrado: ignorar. Ler
                    // o módulo principal de processo alheio dá acesso negado o tempo todo,
                    // e isso é normal — não é erro.
                }

                if (serve) encontrado = processo;
                else processo.Dispose();
            }

            return encontrado;
        }

        private static bool MoraNaPasta(Process processo, string prefixo)
        {
            var caminho = processo.MainModule?.FileName;
            if (string.IsNullOrEmpty(caminho)) return false;

            return Normalizar(Path.GetDirectoryName(caminho!) ?? "")
                .StartsWith(prefixo, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Pasta com barra no fim, para "Jogos\NFS" não casar com "Jogos\NFS2".</summary>
        private static string Normalizar(string caminho)
        {
            var texto = caminho.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return texto + Path.DirectorySeparatorChar;
        }
    }
}
