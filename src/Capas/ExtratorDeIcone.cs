using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Mochila.Capas
{
    /// <summary>
    /// Ícone de dentro do .exe — o penúltimo degrau do fallback, antes do card desenhado.
    ///
    /// Icon.ExtractAssociatedIcon devolve 32x32, que fica lamentável esticado num card de
    /// 300 px. ExtractIconEx entrega o ícone "grande" (tipicamente 256x256 em jogo
    /// moderno, 48x48 em jogo antigo), então tentamos ele primeiro e caímos para o
    /// pequeno só se não houver nada melhor.
    /// </summary>
    public static class ExtratorDeIcone
    {
        public static Bitmap? Extrair(string? caminhoDoExe)
        {
            if (string.IsNullOrEmpty(caminhoDoExe) || !File.Exists(caminhoDoExe)) return null;

            return ExtrairGrande(caminhoDoExe!) ?? ExtrairAssociado(caminhoDoExe!);
        }

        private static Bitmap? ExtrairGrande(string caminho)
        {
            var grandes = new IntPtr[1];
            var pequenos = new IntPtr[1];

            try
            {
                if (ExtractIconEx(caminho, 0, grandes, pequenos, 1) <= 0) return null;

                var alvo = grandes[0] != IntPtr.Zero ? grandes[0] : pequenos[0];
                if (alvo == IntPtr.Zero) return null;

                using (var icone = Icon.FromHandle(alvo))
                    return new Bitmap(icone.ToBitmap());
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                // Handle de ícone é recurso do sistema: não devolver vaza GDI, e o
                // launcher já provou que sabe segurar handle constante.
                foreach (var handle in grandes) if (handle != IntPtr.Zero) DestroyIcon(handle);
                foreach (var handle in pequenos) if (handle != IntPtr.Zero) DestroyIcon(handle);
            }
        }

        private static Bitmap? ExtrairAssociado(string caminho)
        {
            try
            {
                using (var icone = Icon.ExtractAssociatedIcon(caminho))
                    return icone is null ? null : new Bitmap(icone.ToBitmap());
            }
            catch (Exception)
            {
                return null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string arquivo, int indice,
                                                IntPtr[] grandes, IntPtr[] pequenos, int quantidade);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icone);
    }
}
