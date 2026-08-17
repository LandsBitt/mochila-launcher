using System.Windows.Forms;
using Mochila.Entrada;

namespace Mochila.UI
{
    /// <summary>
    /// Decide quem fica com a tecla: a grade ou a caixa de busca.
    ///
    /// Existe separado da janela porque é regra, não desenho — e regra dá para testar
    /// sem abrir janela nenhuma.
    ///
    /// A regra em uma frase: navegar vence, menos quando ← → seriam a única forma de
    /// corrigir o que eu acabei de digitar. Digitar "resient", ver o erro e não
    /// conseguir voltar o cursor é irritante o bastante para abrir essa exceção; com a
    /// busca vazia não existe cursor que interesse mover, então a grade leva.
    /// </summary>
    public static class RoteadorDeTeclas
    {
        /// <summary>
        /// true quando a tecla deve ir para a grade (navegar/acionar), false quando deve
        /// seguir o caminho normal — em geral, editar o texto da busca.
        /// </summary>
        public static bool VaiParaGrade(Keys chave, bool buscaTemFoco, bool buscaTemTexto)
        {
            switch (chave)
            {
                // Sem conflito real: dentro de uma caixa de texto de uma linha, essas
                // teclas não fazem nada que eu vá sentir falta.
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                case Keys.Enter:
                    return true;

                // As únicas disputadas: só cedem quando há texto para editar.
                case Keys.Left:
                case Keys.Right:
                    return !(buscaTemFoco && buscaTemTexto);

                default:
                    return false;
            }
        }

        /// <summary>
        /// A tecla dita em <see cref="ComandoDeNavegacao"/> — o mesmo vocabulário que o
        /// gamepad usa desde a fase 8.
        ///
        /// Isto é o que fecha a fase 9: antes, o teclado chamava método na grade e o
        /// controle mandava comando, e cada tecla nova precisava ser lembrada nos dois
        /// lugares. Agora as duas fontes produzem comando, e a grade só conhece comando.
        ///
        /// Só navegação entra aqui. F5 e F6 continuam fora de propósito: recarregar e
        /// escanear não são navegação, não têm botão no controle e nunca precisaram de
        /// dois caminhos.
        /// </summary>
        public static ComandoDeNavegacao Comando(Keys chave)
        {
            switch (chave)
            {
                case Keys.Left: return ComandoDeNavegacao.Esquerda;
                case Keys.Right: return ComandoDeNavegacao.Direita;
                case Keys.Up: return ComandoDeNavegacao.Cima;
                case Keys.Down: return ComandoDeNavegacao.Baixo;
                case Keys.PageUp: return ComandoDeNavegacao.PaginaAnterior;
                case Keys.PageDown: return ComandoDeNavegacao.PaginaSeguinte;
                case Keys.Home: return ComandoDeNavegacao.Primeiro;
                case Keys.End: return ComandoDeNavegacao.Ultimo;
                case Keys.Enter: return ComandoDeNavegacao.Confirmar;
                default: return ComandoDeNavegacao.Nenhum;
            }
        }
    }
}
