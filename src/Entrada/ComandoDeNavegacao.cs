namespace Mochila.Entrada
{
    /// <summary>
    /// O que o launcher faz, dito sem falar de hardware.
    ///
    /// A grade nunca vê botão de Xbox: ela recebe estes comandos e mais nada. É o que
    /// mantém a porta aberta para uma fonte de entrada nova — HID de DualShock, um dia —
    /// sem encostar em quem desenha.
    /// </summary>
    public enum ComandoDeNavegacao
    {
        Nenhum = 0,
        Cima,
        Baixo,
        Esquerda,
        Direita,
        Confirmar,
        Voltar,
        Favoritar,
        Detalhes,
        PaginaAnterior,
        PaginaSeguinte,
        Menu,
        TrocarOrdenacao,
        SaltoAlfabetico,

        // Os dois abaixo entraram na fase 9, quando o teclado passou a falar por comandos:
        // Home e End existem desde a fase 4 e não tinham como ser ditos aqui. Não há botão
        // de controle mapeado para eles — o que é normal, o mapa não precisa ser sobrejetor.
        Primeiro,
        Ultimo
    }

    /// <summary>
    /// Onde o comando cai. O mesmo botão físico vira comando diferente em cada contexto,
    /// e é justamente por isso que a tradução mora num lugar só
    /// (<see cref="RoteadorDeComandos"/>).
    /// </summary>
    public enum ContextoDeNavegacao
    {
        Grade = 0,

        /// <summary>Tela de detalhes — chega na fase 11.</summary>
        Detalhes,

        /// <summary>Big Picture — chega na fase 18.</summary>
        ModoTV
    }

    /// <summary>
    /// O vocabulário físico do controle. Só a tabela do roteador conhece estes nomes;
    /// deste enum para dentro do launcher, tudo já é <see cref="ComandoDeNavegacao"/>.
    ///
    /// Direção é botão aqui porque, para quem navega, d-pad e analógico são a mesma
    /// coisa — a diferença morre no <see cref="GamepadNavegacao"/>.
    /// </summary>
    public enum BotaoDoGamepad
    {
        Nenhum = 0,
        Cima,
        Baixo,
        Esquerda,
        Direita,
        A,
        B,
        X,
        Y,
        OmbroEsquerdo,
        OmbroDireito,

        /// <summary>LT e RT juntos: os dois fazem a mesma coisa, não vale separar.</summary>
        Gatilhos,

        Start,
        Back
    }
}
