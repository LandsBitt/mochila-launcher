using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mochila.UI
{
    /// <summary>
    /// Cria um <c>.lnk</c> na área de trabalho, via COM do próprio Windows (IShellLink) —
    /// sem NuGet e sem script.
    ///
    /// **Esta é uma das duas únicas coisas do launcher que escrevem fora da pasta dele**, e
    /// por isso só acontece sob confirmação explícita. O atalho aponta para caminho
    /// absoluto, o que é inevitável: um <c>.lnk</c> com caminho relativo não abre nada.
    /// Isso não fere a regra de portabilidade porque o atalho é descartável e vive fora do
    /// HD — o que o launcher grava *no disco dele* continua sendo tudo relativo.
    /// </summary>
    public static class AtalhosDoWindows
    {
        /// <summary>
        /// Grava o atalho e devolve o caminho dele. Lança se o COM recusar — quem chama
        /// transforma isso em mensagem.
        /// </summary>
        public static string CriarNaAreaDeTrabalho(string nome, string executavel, string? argumentos, string? pastaDeTrabalho)
        {
            var area = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(area))
                throw new InvalidOperationException("Não consegui achar a área de trabalho deste usuário.");

            return CriarEm(area, nome, executavel, argumentos, pastaDeTrabalho);
        }

        /// <summary>
        /// O mesmo atalho, numa pasta qualquer. Separado para o auto-teste conseguir
        /// gravar e reler um <c>.lnk</c> dentro da sandbox, sem sujar a área de trabalho
        /// de quem roda os testes.
        /// </summary>
        public static string CriarEm(string pasta, string nome, string executavel,
                                     string? argumentos, string? pastaDeTrabalho)
        {
            if (string.IsNullOrWhiteSpace(executavel))
                throw new ArgumentException("Executável vazio.", nameof(executavel));

            var destino = CaminhoLivre(pasta, LimparNomeDeArquivo(nome));

            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(executavel);
                link.SetArguments(argumentos ?? "");

                // Mesmo motivo do lançamento normal: jogo antigo procura assets em caminho
                // relativo e crasha se o diretório de trabalho for outro.
                if (!string.IsNullOrEmpty(pastaDeTrabalho)) link.SetWorkingDirectory(pastaDeTrabalho!);

                link.SetIconLocation(executavel, 0);
                link.SetDescription($"Abrir {nome}");

                ((IPersistFile)link).Save(destino, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }

            return destino;
        }

        /// <summary>
        /// Para onde um <c>.lnk</c> aponta, ou null se não der para resolver.
        ///
        /// Arrastar um atalho para a janela é o jeito natural de catalogar um jogo, e o
        /// que precisa ir para a biblioteca é o alvo — gravar o próprio <c>.lnk</c> deixaria
        /// a biblioteca dependendo de um arquivo que vive fora do HD.
        /// </summary>
        public static string? AlvoDe(string caminhoDoAtalho)
        {
            if (string.IsNullOrEmpty(caminhoDoAtalho) || !File.Exists(caminhoDoAtalho)) return null;

            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(caminhoDoAtalho, 0);

                var alvo = new StringBuilder(260);
                link.GetPath(alvo, alvo.Capacity, IntPtr.Zero, 0);

                var texto = alvo.ToString();
                return string.IsNullOrEmpty(texto) ? null : texto;
            }
            catch (Exception)
            {
                // Atalho quebrado, para item virtual, ou COM indisponível: quem chama avisa.
                return null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        /// <summary>Já existe um atalho com esse nome? Vira "Nome (2).lnk".</summary>
        private static string CaminhoLivre(string pasta, string nome)
        {
            var candidato = Path.Combine(pasta, nome + ".lnk");

            for (var n = 2; File.Exists(candidato) && n < 100; n++)
                candidato = Path.Combine(pasta, $"{nome} ({n}).lnk");

            return candidato;
        }

        private static string LimparNomeDeArquivo(string nome)
        {
            var limpo = new StringBuilder(nome.Length);
            var invalidos = Path.GetInvalidFileNameChars();

            foreach (var c in nome)
            {
                limpo.Append(Array.IndexOf(invalidos, c) >= 0 ? ' ' : c);
            }

            var texto = limpo.ToString().Trim();
            return texto.Length == 0 ? "Jogo" : texto;
        }

        // ---- COM -------------------------------------------------------------------------------
        //
        // A ordem dos métodos é a ordem da vtable: trocar duas linhas aqui não dá erro de
        // compilação, dá chamada no método errado em tempo de execução.

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arquivo, int tamanho,
                         IntPtr dadosDoArquivo, int sinalizadores);
            void GetIDList(out IntPtr lista);
            void SetIDList(IntPtr lista);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder nome, int tamanho);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string nome);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pasta, int tamanho);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pasta);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder argumentos, int tamanho);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string argumentos);
            void GetHotkey(out short tecla);
            void SetHotkey(short tecla);
            void GetShowCmd(out int comando);
            void SetShowCmd(int comando);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icone, int tamanho,
                                 out int indice);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icone, int indice);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string caminho, int reservado);
            void Resolve(IntPtr janela, int sinalizadores);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string caminho);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid classe);

            [PreserveSig]
            int IsDirty();

            void Load([MarshalAs(UnmanagedType.LPWStr)] string arquivo, uint modo);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string? arquivo,
                      [MarshalAs(UnmanagedType.Bool)] bool lembrar);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string arquivo);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string arquivo);
        }
    }
}
