using System;
using Mochila.Util;

namespace Mochila.Modelo
{
    /// <summary>
    /// Em que pé eu estou com o jogo. Entrou no schema v3 (fase 12).
    ///
    /// Cinco valores e nada mais: uma lista maior vira taxonomia que ninguém mantém, e o
    /// que eu preciso responder olhando a grade é "o que eu quero jogar" e "o que eu já
    /// zerei".
    /// </summary>
    public enum StatusDoJogo
    {
        Nenhum = 0,
        QueroJogar,
        Jogando,
        Zerado,
        Largado
    }

    /// <summary>
    /// Tradução entre o enum e o texto do JSON (e o texto da tela).
    ///
    /// O nome no arquivo é fixo e com hífen (<c>quero-jogar</c>), não
    /// <c>Enum.ToString()</c>: renomear o valor em C# um dia não pode invalidar o que já
    /// está gravado no HD.
    ///
    /// Chama-se <c>Estados</c> e não <c>Status</c> porque <c>Jogo.Status</c> é uma
    /// propriedade: dentro da classe, <c>Status.ParaTexto(...)</c> resolveria para a
    /// propriedade e não compilaria.
    /// </summary>
    public static class Estados
    {
        /// <summary>Todos, na ordem em que aparecem nos menus.</summary>
        public static readonly StatusDoJogo[] Todos =
        {
            StatusDoJogo.Nenhum, StatusDoJogo.QueroJogar, StatusDoJogo.Jogando,
            StatusDoJogo.Zerado, StatusDoJogo.Largado
        };

        public static string ParaTexto(StatusDoJogo status) => status switch
        {
            StatusDoJogo.QueroJogar => "quero-jogar",
            StatusDoJogo.Jogando => "jogando",
            StatusDoJogo.Zerado => "zerado",
            StatusDoJogo.Largado => "largado",
            _ => "nenhum"
        };

        /// <summary>Lixo no arquivo (ou status de uma versão futura) vira "nenhum".</summary>
        public static StatusDoJogo DeTexto(string? texto)
        {
            var limpo = Textos.RemoverAcentos(texto).Trim().ToLowerInvariant();

            return limpo switch
            {
                "quero-jogar" => StatusDoJogo.QueroJogar,
                "jogando" => StatusDoJogo.Jogando,
                "zerado" => StatusDoJogo.Zerado,
                "largado" => StatusDoJogo.Largado,
                _ => StatusDoJogo.Nenhum
            };
        }

        /// <summary>Como o status aparece na tela.</summary>
        public static string Descrever(StatusDoJogo status) => status switch
        {
            StatusDoJogo.QueroJogar => "Quero jogar",
            StatusDoJogo.Jogando => "Jogando",
            StatusDoJogo.Zerado => "Zerado",
            StatusDoJogo.Largado => "Largado",
            _ => "Sem status"
        };
    }
}
