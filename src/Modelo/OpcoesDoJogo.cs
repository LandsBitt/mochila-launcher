using System;
using System.Collections.Generic;
using Mochila.Dados;

namespace Mochila.Modelo
{
    /// <summary>Prioridade com que o processo do jogo sobe. Fase 15.</summary>
    public enum PrioridadeDoProcesso
    {
        Normal = 0,
        Acima,
        Alta
    }

    /// <summary>
    /// O que fazer em volta do lançamento: prioridade do processo e os scripts de antes e
    /// depois.
    ///
    /// <b>Está no schema desde a v3 e a tela só chega na fase 15.</b> É de propósito: o
    /// bump é único (a spec proíbe três migrações), então os campos das fases seguintes
    /// nascem aqui, gravados e relidos, mesmo sem ninguém para editá-los ainda. Um jogo
    /// com as opções no padrão grava o objeto igual ao de todo mundo — nada de campo
    /// aparecendo e sumindo do arquivo conforme a versão.
    ///
    /// Os caminhos são relativos ou com token (<c>%APPDATA%</c>), nunca absolutos: a
    /// regra de portabilidade não abre exceção para campo de fase futura.
    /// </summary>
    public sealed class OpcoesDeExecucao
    {
        public PrioridadeDoProcesso Prioridade { get; set; } = PrioridadeDoProcesso.Normal;

        /// <summary>Script .bat/.cmd rodado antes do jogo, relativo. null = nenhum.</summary>
        public string? ScriptAntes { get; set; }

        public string? ScriptDepois { get; set; }

        public bool EhPadrao => Prioridade == PrioridadeDoProcesso.Normal &&
                                ScriptAntes is null && ScriptDepois is null;

        public static OpcoesDeExecucao DeJson(Dictionary<string, object>? objeto) => new OpcoesDeExecucao
        {
            Prioridade = PrioridadeDeTexto(Json.Texto(objeto, "prioridade", null)),
            ScriptAntes = TextoOuNulo(Json.Texto(objeto, "scriptAntes", null)),
            ScriptDepois = TextoOuNulo(Json.Texto(objeto, "scriptDepois", null))
        };

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("prioridade", TextoDaPrioridade(Prioridade))
            .Add("scriptAntes", ScriptAntes)
            .Add("scriptDepois", ScriptDepois);

        public static string TextoDaPrioridade(PrioridadeDoProcesso prioridade) => prioridade switch
        {
            PrioridadeDoProcesso.Acima => "acima",
            PrioridadeDoProcesso.Alta => "alta",
            _ => "normal"
        };

        public static PrioridadeDoProcesso PrioridadeDeTexto(string? texto)
            => (texto ?? "").Trim().ToLowerInvariant() switch
            {
                "acima" => PrioridadeDoProcesso.Acima,
                "alta" => PrioridadeDoProcesso.Alta,
                _ => PrioridadeDoProcesso.Normal
            };

        internal static string? TextoOuNulo(string? texto)
            => string.IsNullOrWhiteSpace(texto) ? null : texto;
    }

    /// <summary>
    /// Backup do save deste jogo. Campo do schema v3 <b>sem tela</b>: a fase que o usa saiu
    /// deste documento e foi para o `ESPEC-v3.md`.
    ///
    /// Ele fica aqui de propósito. Tirá-lo agora exigiria um bump para devolvê-lo depois, e
    /// não repetir bump é exatamente o que o bump único da fase 12 existe para garantir.
    ///
    /// <c>Ativo</c> é o opt-in que a spec exige: sem ele ligado à mão, o launcher não
    /// copia nada de lugar nenhum. Hoje, nada o liga.
    /// </summary>
    public sealed class BackupDeSave
    {
        /// <summary>Quantas versões o rodízio guarda. O padrão da spec.</summary>
        public const int VersoesPadrao = 5;

        public bool Ativo { get; set; }

        /// <summary>Pasta do save, com token (<c>%APPDATA%\Jogo</c>) ou relativa. Nunca expandida.</summary>
        public string? Pasta { get; set; }

        public int VersoesMantidas { get; set; } = VersoesPadrao;

        public bool EhPadrao => !Ativo && Pasta is null && VersoesMantidas == VersoesPadrao;

        public static BackupDeSave DeJson(Dictionary<string, object>? objeto) => new BackupDeSave
        {
            Ativo = Json.Booleano(objeto, "ativo", false),
            Pasta = OpcoesDeExecucao.TextoOuNulo(Json.Texto(objeto, "pasta", null)),
            // Zero ou negativo apagaria tudo a cada backup: cai no padrão.
            VersoesMantidas = Math.Max(1, Json.Inteiro(objeto, "versoesMantidas", VersoesPadrao))
        };

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("ativo", Ativo)
            .Add("pasta", Pasta)
            .Add("versoesMantidas", VersoesMantidas);
    }
}
