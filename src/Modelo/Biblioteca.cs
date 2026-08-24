using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mochila.Dados;
using Mochila.Util;

namespace Mochila.Modelo
{
    /// <summary>
    /// Conteúdo do _mochila\biblioteca.json: a lista de jogos e as pastas que eu mando escanear.
    /// Todos os caminhos daqui são relativos à pasta do launcher.
    /// </summary>
    public sealed class Biblioteca
    {
        /// <summary>
        /// 3 desde a fase 12: tags, nota, status e os campos das fases 14 a 16, todos num
        /// bump só. Arquivo v1 e v2 continuam sendo lidos e viram v3 na primeira gravação
        /// — <see cref="Jogo.DeJson"/> cuida de cada campo.
        /// </summary>
        public const int VersaoAtual = 3;

        public int Versao { get; set; } = VersaoAtual;

        /// <summary>
        /// true quando o arquivo lido é de uma versão MAIS NOVA que este binário.
        ///
        /// Nesse caso o launcher abre para olhar e não grava mais nada — nem biblioteca,
        /// nem tempo de sessão. Regravar um arquivo que eu não entendo por inteiro é
        /// destruir o que a versão nova escreveu, em silêncio; e o saco de sobras cobre
        /// campo desconhecido, não estrutura desconhecida. A saída é atualizar o binário.
        /// </summary>
        public bool SomenteLeitura { get; private set; }

        /// <summary>Pastas raiz a escanear, relativas à pasta do launcher (ex.: "Jogos").</summary>
        public List<string> PastasEscaneadas { get; } = new List<string>();

        public List<Jogo> Jogos { get; } = new List<Jogo>();

        // ---- Consultas ---------------------------------------------------------------------

        public Jogo? ObterPorId(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Jogos.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Procura por executável — é assim que o rescan sabe que um jogo já existe.</summary>
        public Jogo? ObterPorExecutavel(string? executavelRelativo)
        {
            if (string.IsNullOrWhiteSpace(executavelRelativo)) return null;

            var alvo = Caminhos.NormalizarRelativo(executavelRelativo);
            return Jogos.FirstOrDefault(j => string.Equals(j.ExecutavelRelativo, alvo, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gera um id único a partir do título. Se já existir, acrescenta "-2", "-3"...
        /// Título só de símbolos cai para "jogo".
        /// </summary>
        public string GerarId(string? titulo)
        {
            var baseId = Textos.Slug(titulo);
            if (string.IsNullOrEmpty(baseId)) baseId = "jogo";

            if (ObterPorId(baseId) is null) return baseId;

            for (var n = 2; n < 10_000; n++)
            {
                var candidato = $"{baseId}-{n.ToString(CultureInfo.InvariantCulture)}";
                if (ObterPorId(candidato) is null) return candidato;
            }
            var sufixoAleatorio = Guid.NewGuid().ToString("N").Substring(0, 6);
            return $"{baseId}-{sufixoAleatorio}";
        }

        /// <summary>
        /// Confere as invariantes de portabilidade. Devolve a lista de problemas
        /// (vazia = tudo certo). Chamado antes de salvar.
        /// </summary>
        public List<string> Validar()
        {
            var problemas = new List<string>();
            var idsVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pasta in PastasEscaneadas)
            {
                if (!Caminhos.EhRelativoValido(pasta))
                    problemas.Add($"Pasta escaneada não é um caminho relativo válido: \"{pasta}\"");
            }

            foreach (var jogo in Jogos)
            {
                if (string.IsNullOrEmpty(jogo.Id))
                    problemas.Add($"Jogo \"{jogo.Titulo}\" está sem id.");
                else if (!idsVistos.Add(jogo.Id))
                    problemas.Add($"Id duplicado: \"{jogo.Id}\".");
                else if (!Textos.EhIdValido(jogo.Id))
                {
                    // O id é nome de ARQUIVO em dois lugares (<id>_thumb.jpg e a capa).
                    // Um ":" aí vira alternate data stream no Windows e a falha não é
                    // bonita, é estranha. Ver Textos.EhIdValido.
                    problemas.Add(
                        $"Jogo \"{jogo.Titulo}\": id \"{jogo.Id}\" tem caractere que não pode virar " +
                        "nome de arquivo ou pasta (só a-z, 0-9, hífen e sublinhado).");
                }

                if (!Caminhos.EhRelativoValido(jogo.ExecutavelRelativo))
                {
                    problemas.Add(
                        $"Jogo \"{jogo.Titulo}\": executável não é um caminho relativo válido " +
                        $"(\"{jogo.ExecutavelRelativo}\"). Caminho com letra de drive quebra a portabilidade.");
                }

                // Os scripts da fase 15 entram na validação AGORA, e não quando a tela
                // chegar. O motivo está na própria spec: Validar só conferia
                // ExecutavelRelativo e PastasEscaneadas, então um caminho absoluto gravado
                // em qualquer outro campo passava batido — e caminho absoluto no
                // biblioteca.json é a regra inegociável do projeto sendo quebrada em
                // silêncio, num campo que ninguém olha.
                foreach (var script in new[] { jogo.OpcoesDeExecucao.ScriptAntes, jogo.OpcoesDeExecucao.ScriptDepois })
                {
                    if (script is not null && !Caminhos.EhRelativoValido(script))
                    {
                        problemas.Add(
                            $"Jogo \"{jogo.Titulo}\": script \"{script}\" não é um caminho relativo válido. " +
                            "Script fora do HD do launcher não existe no próximo PC.");
                    }
                }
            }

            return problemas;
        }

        // ---- Persistência --------------------------------------------------------------------

        /// <summary>Carrega _mochila\biblioteca.json. Arquivo ausente devolve biblioteca vazia.</summary>
        public static Biblioteca Carregar() => Carregar(Caminhos.ArquivoBiblioteca);

        public static Biblioteca Carregar(string caminhoArquivo)
        {
            if (!ArquivoTexto.Existe(caminhoArquivo)) return new Biblioteca();

            string conteudo;
            try
            {
                conteudo = ArquivoTexto.Ler(caminhoArquivo);
            }
            catch (Exception ex)
            {
                throw new DadosCorrompidosException(caminhoArquivo, "não foi possível ler o arquivo", ex);
            }

            Dictionary<string, object>? raiz;
            try
            {
                raiz = Json.ComoObjeto(Json.Analisar(conteudo));
            }
            catch (Exception ex)
            {
                throw new DadosCorrompidosException(caminhoArquivo, "JSON inválido", ex);
            }

            if (raiz is null)
                throw new DadosCorrompidosException(caminhoArquivo, "conteúdo não é um objeto JSON", null);

            var versao = Json.Inteiro(raiz, "versao", VersaoAtual);
            var biblioteca = new Biblioteca
            {
                Versao = versao,
                SomenteLeitura = versao > VersaoAtual
            };

            if (raiz.TryGetValue("pastasEscaneadas", out var pastas))
            {
                foreach (var item in Json.ComoLista(pastas))
                {
                    // Migra pasta gravada de forma absoluta ("D:\Jogos") para relativa.
                    var relativa = Caminhos.ParaRelativoMigrando(Convert.ToString(item, CultureInfo.InvariantCulture));
                    if (relativa.Length > 0) biblioteca.PastasEscaneadas.Add(relativa);
                }
            }

            if (raiz.TryGetValue("jogos", out var jogos))
            {
                foreach (var item in Json.ComoLista(jogos))
                {
                    if (Json.ComoObjeto(item) is { } objetoJogo)
                        biblioteca.Jogos.Add(Jogo.DeJson(objetoJogo));
                }
            }

            return biblioteca;
        }

        public void Salvar() => Salvar(Caminhos.ArquivoBiblioteca);

        public void Salvar(string caminhoArquivo)
        {
            // Arquivo mais novo que o binário: não grava, e não é erro. Quem chama continua
            // funcionando (favoritar redesenha, lançar abre o jogo) — só nada persiste. O
            // aviso de que isso está acontecendo é da janela, e está no rodapé o tempo todo.
            if (SomenteLeitura) return;

            // Gravou uma vez, está no formato novo: é assim que a migração 1 -> 2 -> 3
            // acontece, sem passo separado e sem arquivo intermediário.
            Versao = VersaoAtual;

            var problemas = Validar();
            if (problemas.Count > 0)
            {
                throw new InvalidOperationException(
                    "A biblioteca não pode ser salva porque violaria as regras de portabilidade:" +
                    Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", problemas));
            }

            ArquivoTexto.EscreverAtomico(caminhoArquivo, Json.Escrever(ParaJson()));
        }

        public JsonObjeto ParaJson() => new JsonObjeto()
            .Add("versao", Versao)
            .Add("pastasEscaneadas", PastasEscaneadas.Cast<object?>().ToList())
            .Add("jogos", Jogos.Select(j => (object?)j.ParaJson()).ToList());
    }

    /// <summary>Arquivo de estado ilegível ou fora do formato esperado.</summary>
    public sealed class DadosCorrompidosException : Exception
    {
        public DadosCorrompidosException(string caminho, string motivo, Exception? interna)
            : base($"Arquivo \"{caminho}\" não pôde ser lido ({motivo}).", interna)
        {
            Caminho = caminho;
        }

        public string Caminho { get; }
    }
}
