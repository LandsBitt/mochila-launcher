using System;
using System.Runtime.InteropServices;

namespace Mochila.Entrada
{
    /// <summary>
    /// Estado cru de um controle, sem interpretação nenhuma: é o que a DLL devolveu.
    /// Deadzone, repeat e mapeamento são problema do <see cref="GamepadNavegacao"/>.
    /// </summary>
    public struct EstadoBrutoDeGamepad
    {
        /// <summary>
        /// Contador que o XInput só incrementa quando o estado muda de verdade. Igual ao
        /// da leitura anterior significa "nada aconteceu" — e nada aconteceu se resolve
        /// sem calcular coisa nenhuma.
        /// </summary>
        public uint Pacote;

        public ushort Botoes;
        public byte GatilhoEsquerdo;
        public byte GatilhoDireito;
        public short AnalogicoEsquerdoX;
        public short AnalogicoEsquerdoY;
    }

    /// <summary>
    /// De onde vem o estado bruto. Existe como interface por um motivo prático: sem ela,
    /// nada do gamepad entraria no <c>--autoteste</c>, porque todo teste dependeria de ter
    /// um controle plugado na máquina.
    /// </summary>
    public interface IEstadoBrutoDeGamepad
    {
        /// <summary>Quantos slots existem para varrer. O XInput são 4.</summary>
        int Slots { get; }

        /// <summary>false = nada conectado naquele slot (ou a leitura falhou).</summary>
        bool TentarLer(int slot, out EstadoBrutoDeGamepad estado);
    }

    /// <summary>
    /// A fonte de verdade: XInput por P/Invoke, sem NuGet e sem DirectInput.
    ///
    /// A DLL é resolvida em tempo de execução (<c>LoadLibrary</c> + <c>GetProcAddress</c>)
    /// em vez de <c>DllImport</c> fixo, porque <c>xinput1_4.dll</c> não existe em Windows
    /// antigo e um <c>DllImport</c> que não resolve derruba o método inteiro. Se nenhuma
    /// das duas carregar, <see cref="Criar"/> devolve null e a sessão simplesmente não tem
    /// suporte a controle — sem exceção, sem caixa de erro, e o teclado continua igual.
    ///
    /// Vibração (<c>XInputSetState</c>) não é usada de propósito: não serve para nada aqui
    /// e acorda o device à toa.
    /// </summary>
    public sealed class EntradaXInput : IEstadoBrutoDeGamepad
    {
        private const int ErrorSuccess = 0;

        /// <summary>ERROR_DEVICE_NOT_CONNECTED. É retorno normal, não é falha.</summary>
        private const int ErrorDeviceNotConnected = 1167;

        private static readonly string[] DllsCandidatas = { "xinput1_4.dll", "xinput9_1_0.dll" };

        private readonly XInputGetStateDelegate _lerEstado;

        private EntradaXInput(XInputGetStateDelegate lerEstado) => _lerEstado = lerEstado;

        public int Slots => 4;

        /// <summary>O nome da DLL que atendeu. Só para diagnóstico.</summary>
        public string DllUsada { get; private set; } = "";

        /// <summary>
        /// Devolve a fonte, ou null se esta máquina não tem XInput. Nunca lança:
        /// launcher que não abre porque a máquina é antiga demais para gamepad seria
        /// trocar um recurso a mais por um launcher a menos.
        /// </summary>
        public static EntradaXInput? Criar()
        {
            foreach (var nome in DllsCandidatas)
            {
                try
                {
                    var modulo = LoadLibrary(nome);
                    if (modulo == IntPtr.Zero) continue;

                    var funcao = GetProcAddress(modulo, "XInputGetState");
                    if (funcao == IntPtr.Zero)
                    {
                        FreeLibrary(modulo);
                        continue;
                    }

                    // O módulo fica carregado para sempre de propósito: o delegate aponta
                    // para dentro dele, e liberar a DLL com o delegate vivo é ponteiro solto.
                    var delegado = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(funcao);
                    return new EntradaXInput(delegado) { DllUsada = nome };
                }
                catch (Exception)
                {
                    // DLL corrompida, política de segurança bloqueando o carregamento:
                    // tenta a próxima, e sem nenhuma delas o launcher segue sem controle.
                }
            }

            return null;
        }

        public bool TentarLer(int slot, out EstadoBrutoDeGamepad estado)
        {
            estado = default;

            if (slot < 0 || slot >= Slots) return false;

            XINPUT_STATE cru;
            int resultado;

            try
            {
                resultado = _lerEstado((uint)slot, out cru);
            }
            catch (Exception)
            {
                // Chamada nativa que falhou não pode virar tela de erro: para o launcher,
                // é a mesma coisa que não ter controle nenhum.
                return false;
            }

            // ERROR_DEVICE_NOT_CONNECTED (1167) é o caso esperado do slot vazio, e qualquer
            // outro código dá no mesmo aqui: não há estado para ler.
            if (resultado != ErrorSuccess) return false;

            estado = new EstadoBrutoDeGamepad
            {
                Pacote = cru.dwPacketNumber,
                Botoes = cru.Gamepad.wButtons,
                GatilhoEsquerdo = cru.Gamepad.bLeftTrigger,
                GatilhoDireito = cru.Gamepad.bRightTrigger,
                AnalogicoEsquerdoX = cru.Gamepad.sThumbLX,
                AnalogicoEsquerdoY = cru.Gamepad.sThumbLY
            };
            return true;
        }

        // ---- Interop --------------------------------------------------------------------------

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int XInputGetStateDelegate(uint indiceDoUsuario, out XINPUT_STATE estado);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;      // o analógico direito não navega nesta fase
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string nome);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr modulo, string funcao);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr modulo);
    }

    /// <summary>Bits de <c>XINPUT_GAMEPAD.wButtons</c>, com os nomes da documentação.</summary>
    internal static class BitsDoXInput
    {
        public const ushort DPadCima = 0x0001;
        public const ushort DPadBaixo = 0x0002;
        public const ushort DPadEsquerda = 0x0004;
        public const ushort DPadDireita = 0x0008;
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;
        public const ushort OmbroEsquerdo = 0x0100;
        public const ushort OmbroDireito = 0x0200;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;
    }
}
