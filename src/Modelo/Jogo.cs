using System;
using System.Collections.Generic;
using System.IO;
using Launcher.Dados;

namespace Launcher.Modelo
{
    /// <summary>
    /// Um jogo da biblioteca.
    ///
    /// O caminho do executável é guardado SEMPRE relativo à pasta do launcher
    /// (ex.: "Jogos\Antigos\Carros\Need For Speed Most Wanted Black Edition\speed.exe").
    /// A resolução para caminho absoluto acontece só em memória, via <see cref="CaminhoExecutavel"/>.
    /// </summary>
    public sealed class Jogo
    {
        /// <summary>Identificador estável, em slug (ex.: "nfsmw-black"). Nomeia capa e thumb.</summary>
        public string Id { get; set; } = "";

        public string Titulo { get; set; } = "";

        /// <summary>Caminho do .exe relativo à pasta do launcher. Nunca absoluto.</summary>
        public string ExecutavelRelativo { get; set; } = "";

        public string Argumentos { get; set; } = "";

        /// <summary>Nome do arquivo dentro de _launcher\capas (ex.: "nfsmw-black.jpg"), ou null.</summary>
        public string? CapaArquivo { get; set; }

        /// <summary>Id do jogo no SteamGridDB, guardado para não repetir a busca.</summary>
        public int? SteamGridDbId { get; set; }

        /// <summary>
        /// Tempo total jogado, em SEGUNDOS.
        ///
        /// Era minuto e virou segundo porque minuto inteiro perde dado: uma sessão de 50
        /// segundos creditava zero e sumia para sempre, e dez sessões curtas continuavam
        /// somando nada. A exibição continua em minutos — ver <see cref="Util.TempoDeJogo"/>.
        /// </summary>
        public int SegundosJogados { get; set; }

        /// <summary>Sempre em UTC.</summary>
        public DateTime? UltimaVezJogado { get; set; }

        public bool Favorito { get; set; }

        /// <summary>
        /// true quando eu corrigi o executável na mão. Rescans NUNCA podem sobrescrever
        /// o executável de um jogo com essa marca.
        /// </summary>
        public bool ExecutavelFixadoPeloUsuario { get; set; }

        // ---- Caminhos derivados (nada disso vai para o JSON) ------------------------------

        /// <summary>Caminho absoluto do executável neste PC, ou null se o relativo for inválido.</summary>
        public string? CaminhoExecutavel() => Caminhos.ParaAbsolutoOuNulo(ExecutavelRelativo);

        /// <summary>Pasta do jogo — vira WorkingDirectory na hora de lançar (fase 5).</summary>
        public string? PastaDoJogo()
        {
            var exe = CaminhoExecutavel();
            return string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
        }

        public bool ExecutavelExiste()
        {
            var exe = CaminhoExecutavel();
            return !string.IsNullOrEmpty(exe) && File.Exists(exe);
        }

        /// <summary>Caminho absoluto da capa em tamanho cheio, ou null se o jogo não tem capa.</summary>
        public string? CaminhoCapa()
            => string.IsNullOrEmpty(CapaArquivo) ? null : Path.Combine(Caminhos.PastaCapas, CapaArquivo!);

        /// <summary>Caminho do thumbnail em cache (gerado sob demanda na fase 4).</summary>
        public string CaminhoThumbnail() => Path.Combine(Caminhos.PastaCache, $"{Id}_thumb.jpg");

        // ---- JSON --------------------------------------------------------------------------

        public static Jogo DeJson(Dictionary<string, object> objeto) => new Jogo
        {
            Id = Json.Texto(objeto, "id", "") ?? "",
            Titulo = Json.Texto(objeto, "titulo", "") ?? "",
            ExecutavelRelativo = Caminhos.ParaRelativoMigrando(Json.Texto(objeto, "executavelRelativo", "")),
            Argumentos = Json.Texto(objeto, "argumentos", "") ?? "",
            CapaArquivo = Json.Texto(objeto, "capaArquivo", null),
            SteamGridDbId = Json.InteiroOpcional(objeto, "steamGridDbId"),
            // Migração v1 -> v2: biblioteca antiga só tem minutosJogados. Ler o campo
            // velho como fallback preserva o histórico de quem já usava o launcher.
            SegundosJogados = Json.Inteiro(objeto, "segundosJogados", Json.Inteiro(objeto, "minutosJogados", 0) * 60),
            UltimaVezJogado = Json.DataOpcional(objeto, "ultimaVezJogado"),
            Favorito = Json.Booleano(objeto, "favorito", false),
            ExecutavelFixadoPeloUsuario = Json.Booleano(objeto, "executavelFixadoPeloUsuario", false)
        };

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("id", Id)
            .Add("titulo", Titulo)
            .Add("executavelRelativo", ExecutavelRelativo)
            .Add("argumentos", Argumentos)
            .Add("capaArquivo", string.IsNullOrEmpty(CapaArquivo) ? null : CapaArquivo)
            .Add("steamGridDbId", SteamGridDbId)
            .Add("segundosJogados", SegundosJogados)
            .Add("ultimaVezJogado", Json.FormatarData(UltimaVezJogado))
            .Add("favorito", Favorito)
            .Add("executavelFixadoPeloUsuario", ExecutavelFixadoPeloUsuario);
    }
}
