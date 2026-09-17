using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mochila.Dados;
using Mochila.Util;

namespace Mochila.Modelo
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

        /// <summary>
        /// Identidade do executável — ver <see cref="Scanner.ImpressaoDigital"/>. Serve
        /// para o rescan reconhecer este jogo depois de a pasta ser movida ou renomeada.
        ///
        /// Campo aditivo: entrou na fase 10 com a biblioteca ainda em <c>versao: 2</c>.
        /// Ausente ou null significa "sem impressão" (biblioteca antiga, ou leitura que
        /// falhou) — nunca string vazia.
        /// </summary>
        public string? Impressao { get; set; }

        /// <summary>Nome do arquivo dentro de _mochila\capas (ex.: "nfsmw-black.jpg"), ou null.</summary>
        public string? CapaArquivo { get; set; }

        /// <summary>Arte larga de fundo (1920x620). Schema v3; quem preenche é a fase 14.</summary>
        public string? HeroArquivo { get; set; }

        /// <summary>Logo com transparência. Schema v3; quem preenche é a fase 14.</summary>
        public string? LogoArquivo { get; set; }

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

        // ---- Campos do schema v3 (fase 12) --------------------------------------------------

        /// <summary>
        /// Minhas etiquetas ("corrida", "ea"). Gravadas normalizadas — ver
        /// <see cref="Etiquetas"/>. Lista vazia, nunca null.
        /// </summary>
        public List<string> Tags { get; } = new List<string>();

        /// <summary>0 (sem nota) a 5. Fora da faixa é grampeado na leitura.</summary>
        public int Nota { get; set; }

        public StatusDoJogo Status { get; set; } = StatusDoJogo.Nenhum;

        /// <summary>Prioridade e scripts. Tela só na fase 15 — ver <see cref="OpcoesDeExecucao"/>.</summary>
        public OpcoesDeExecucao OpcoesDeExecucao { get; set; } = new OpcoesDeExecucao();

        /// <summary>Backup do save. Campo do v3 sem tela: a fase dele voltou para o planejamento interno.</summary>
        public BackupDeSave BackupDeSave { get; set; } = new BackupDeSave();

        /// <summary>
        /// O saco de sobras: todo campo do arquivo que este binário não conhece, guardado
        /// como veio e regravado no fim do objeto.
        ///
        /// Meia hora de trabalho que evita perda silenciosa de dado. Sem isto, abrir um
        /// <c>biblioteca.json</c> gravado por uma versão mais nova e salvar qualquer coisa
        /// (favoritar um jogo, contar uma sessão) apagaria os campos que ela criou — sem
        /// erro, sem aviso, e sem chance de recuperar.
        /// </summary>
        public Dictionary<string, object?> Sobras { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

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

        /// <summary>Arte larga de fundo em disco, ou null quando este jogo não tem uma.</summary>
        public string? CaminhoHero()
            => string.IsNullOrEmpty(HeroArquivo) ? null : Path.Combine(Caminhos.PastaCapas, HeroArquivo!);

        /// <summary>Logo com transparência em disco, ou null quando este jogo não tem um.</summary>
        public string? CaminhoLogo()
            => string.IsNullOrEmpty(LogoArquivo) ? null : Path.Combine(Caminhos.PastaCapas, LogoArquivo!);

        /// <summary>O fundo desfocado derivado do hero. Existir em disco é assunto do cache.</summary>
        public string CaminhoHeroDesfocado() => Caminhos.ArquivoHeroDesfocado(Id);

        // ---- JSON --------------------------------------------------------------------------

        /// <summary>
        /// Tudo que este binário sabe ler. O que não estiver aqui vira sobra e volta
        /// intacto para o disco.
        ///
        /// <c>minutosJogados</c> entra na lista mesmo não sendo campo do v3: ele é lido
        /// pela migração logo abaixo, e deixá-lo cair no saco de sobras o faria ser
        /// regravado para sempre ao lado do campo que o substituiu.
        /// </summary>
        private static readonly HashSet<string> ChavesConhecidas = new HashSet<string>(StringComparer.Ordinal)
        {
            "id", "titulo", "executavelRelativo", "impressao", "argumentos",
            "capaArquivo", "heroArquivo", "logoArquivo", "steamGridDbId",
            "segundosJogados", "minutosJogados", "ultimaVezJogado", "favorito",
            "executavelFixadoPeloUsuario", "tags", "nota", "status",
            "opcoesDeExecucao", "backupDeSave"
        };

        public static Jogo DeJson(Dictionary<string, object> objeto)
        {
            var jogo = new Jogo
            {
                // Saneado na leitura, como os caminhos são: o id vira nome de arquivo em
                // <id>_thumb.jpg e na capa, e um id com espaço ou ":" vindo de arquivo
                // editado à mão não pode chegar lá. Dois-pontos em nome de arquivo no
                // Windows vira alternate data stream, e a falha não é bonita, é estranha.
                // Ver Textos.EhIdValido para o conjunto permitido e o porquê dele.
                Id = Textos.SanearId(Json.Texto(objeto, "id", "")),
                Titulo = Json.Texto(objeto, "titulo", "") ?? "",
                ExecutavelRelativo = Caminhos.ParaRelativoMigrando(Json.Texto(objeto, "executavelRelativo", "")),
                Argumentos = Json.Texto(objeto, "argumentos", "") ?? "",
                Impressao = TextoOuNulo(Json.Texto(objeto, "impressao", null)),
                CapaArquivo = Json.Texto(objeto, "capaArquivo", null),
                HeroArquivo = TextoOuNulo(Json.Texto(objeto, "heroArquivo", null)),
                LogoArquivo = TextoOuNulo(Json.Texto(objeto, "logoArquivo", null)),
                SteamGridDbId = Json.InteiroOpcional(objeto, "steamGridDbId"),
                // Migração v1 -> v2: biblioteca antiga só tem minutosJogados. Ler o campo
                // velho como fallback preserva o histórico de quem já usava o launcher.
                SegundosJogados = Json.Inteiro(objeto, "segundosJogados", Json.Inteiro(objeto, "minutosJogados", 0) * 60),
                UltimaVezJogado = Json.DataOpcional(objeto, "ultimaVezJogado"),
                Favorito = Json.Booleano(objeto, "favorito", false),
                ExecutavelFixadoPeloUsuario = Json.Booleano(objeto, "executavelFixadoPeloUsuario", false),

                // Campos do v3. Ausentes (biblioteca v1 ou v2) caem no padrão, que é
                // exatamente "não tenho essa informação" — nada a migrar.
                Nota = Math.Max(0, Math.Min(5, Json.Inteiro(objeto, "nota", 0))),
                Status = Estados.DeTexto(Json.Texto(objeto, "status", null)),
                OpcoesDeExecucao = OpcoesDeExecucao.DeJson(Json.ComoObjeto(Bruto(objeto, "opcoesDeExecucao"))),
                BackupDeSave = BackupDeSave.DeJson(Json.ComoObjeto(Bruto(objeto, "backupDeSave")))
            };

            foreach (var item in Json.ComoLista(Bruto(objeto, "tags")))
                Etiquetas.Acrescentar(jogo.Tags, Convert.ToString(item, CultureInfo.InvariantCulture));

            foreach (var par in objeto)
            {
                if (!ChavesConhecidas.Contains(par.Key)) jogo.Sobras[par.Key] = par.Value;
            }

            return jogo;
        }

        public JsonObjeto ParaJson()
        {
            var json = new JsonObjeto()
                .Add("id", Id)
                .Add("titulo", Titulo)
                .Add("executavelRelativo", ExecutavelRelativo)
                .Add("impressao", TextoOuNulo(Impressao))
                .Add("argumentos", Argumentos)
                .Add("capaArquivo", string.IsNullOrEmpty(CapaArquivo) ? null : CapaArquivo)
                .Add("heroArquivo", TextoOuNulo(HeroArquivo))
                .Add("logoArquivo", TextoOuNulo(LogoArquivo))
                .Add("steamGridDbId", SteamGridDbId)
                .Add("segundosJogados", SegundosJogados)
                .Add("ultimaVezJogado", Json.FormatarData(UltimaVezJogado))
                .Add("favorito", Favorito)
                .Add("executavelFixadoPeloUsuario", ExecutavelFixadoPeloUsuario)
                .Add("tags", Tags.Cast<object?>().ToList())
                .Add("nota", Nota)
                .Add("status", Estados.ParaTexto(Status))
                .Add("opcoesDeExecucao", OpcoesDeExecucao.ParaJson())
                .Add("backupDeSave", BackupDeSave.ParaJson());

            // As sobras vão todas no fim do objeto, depois dos campos conhecidos.
            //
            // A ORDEM ENTRE ELAS não é garantida: Dictionary não é coleção ordenada, e na
            // prática ele preserva a inserção só enquanto ninguém remove nada. Isso é
            // aceitável aqui — o que o saco de sobras promete é não PERDER campo, e é isso
            // que ele cumpre. Se um dia o diff limpo entre gravações passar a importar, o
            // conserto é trocar o tipo por uma lista de pares, não confiar neste comentário.
            foreach (var par in Sobras) json.Add(par.Key, Json.ParaEscrita(par.Value));

            return json;
        }

        private static object? Bruto(Dictionary<string, object> objeto, string chave)
            => objeto.TryGetValue(chave, out var valor) ? valor : null;

        /// <summary>
        /// Texto vazio e texto ausente são a mesma coisa para os campos opcionais: quem
        /// consome trata nulo como "não tem", e string vazia viraria um terceiro estado
        /// sem significado.
        /// </summary>
        private static string? TextoOuNulo(string? texto)
            => string.IsNullOrWhiteSpace(texto) ? null : texto;
    }
}
