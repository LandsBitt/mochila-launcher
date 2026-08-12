using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Launcher.Util
{
    /// <summary>
    /// As três medidas que dizem se o launcher está se comportando: working set,
    /// objetos GDI e objetos USER — mais o pedido ao Windows para devolver a RAM.
    ///
    /// Handle de GDI merece atenção especial: cada Bitmap não descartado gasta um, o
    /// limite é 10.000 por processo, e estourar não deixa o app lento — faz ele parar de
    /// desenhar com "Generic error in GDI+". Working set não denuncia isso, porque o
    /// vazamento vive do lado não-gerenciado.
    /// </summary>
    public static class Memoria
    {
        /// <summary>Objetos GDI do processo (bitmaps, pincéis, fontes, DCs...).</summary>
        public static int ObjetosGdi() => (int)GetGuiResources(ProcessoAtual, SinalizadorGdi);

        /// <summary>Objetos USER do processo (janelas, menus, cursores...).</summary>
        public static int ObjetosUser() => (int)GetGuiResources(ProcessoAtual, SinalizadorUser);

        public static double WorkingSetMb()
        {
            using (var processo = Process.GetCurrentProcess())
                return processo.WorkingSet64 / 1024.0 / 1024.0;
        }

        public static double HeapGerenciadoMb(bool coletando = false)
            => GC.GetTotalMemory(coletando) / 1024.0 / 1024.0;

        /// <summary>Tempo de CPU consumido pelo processo inteiro, somando todas as threads.</summary>
        public static TimeSpan CpuDoProcesso()
        {
            using (var processo = Process.GetCurrentProcess())
                return processo.TotalProcessorTime;
        }

        /// <summary>
        /// Coleta de verdade e espera os finalizadores. Usada antes de cada leitura de
        /// memória: sem isso, lixo ainda não coletado passa por vazamento.
        /// </summary>
        public static void ColetarTudo()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();       // a segunda passada recolhe o que os finalizadores soltaram
        }

        /// <summary>
        /// O que a spec manda fazer antes de sair da frente do jogo: coletar e mandar o
        /// Windows recolher o working set. Com (-1, -1) o sistema despeja as páginas que
        /// puder; elas voltam por demanda quando a janela reaparece.
        /// </summary>
        public static void DevolverRamAoSistema()
        {
            ColetarTudo();

            try
            {
                using (var processo = Process.GetCurrentProcess())
                    SetProcessWorkingSetSize(processo.Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch (Exception)
            {
                // Só uma dica ao sistema operacional: falhar aqui não quebra nada.
            }
        }

        // ---- P/Invoke -------------------------------------------------------------------

        private const uint SinalizadorGdi = 0;
        private const uint SinalizadorUser = 1;

        /// <summary>Pseudo-handle do processo atual: não precisa ser fechado.</summary>
        private static readonly IntPtr ProcessoAtual = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetGuiResources(IntPtr processo, uint sinalizadores);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr processo, IntPtr minimo, IntPtr maximo);
    }
}
