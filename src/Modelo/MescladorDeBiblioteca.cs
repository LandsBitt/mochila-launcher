using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mochila.Dados;
using Mochila.Scanner;
using Mochila.Util;

namespace Mochila.Modelo
{
    /// <summary>O que a mesclagem fez, para eu conferir antes de fechar a janela.</summary>
    public sealed class ResumoDaMesclagem
    {
        /// <summary>Jogos novos na biblioteca.</summary>
        public List<string> Adicionados { get; } = new List<string>();

        /// <summary>Jogos que já existiam e tiveram o executável corrigido pelo rescan.</summary>
        public List<string> ExecutavelAtualizado { get; } = new List<string>();

        /// <summary>Jogos preservados porque eu tinha fixado o executável na mão.</summary>
        public List<string> MantidosPelaMarcaDeFixado { get; } = new List<string>();

        /// <summary>Jogos que eu destravei nesta revisão — voltam a aceitar correção de rescan.</summary>
        public List<string> Destravados { get; } = new List<string>();

        /// <summary>Jogos que já estavam lá exatamente como o scanner propôs.</summary>
        public List<string> SemMudanca { get; } = new List<string>();

        /// <summary>Recusados por estarem em outro drive (quebrariam a portabilidade).</summary>
        public List<string> ForaDaRaiz { get; } = new List<string>();

        /// <summary>
        /// Jogos reconhecidos pela impressão digital depois de a pasta mudar de nome ou
        /// de lugar. Sem isso eles virariam entradas novas, e o histórico ficaria órfão.
        /// </summary>
        public List<string> ReligadosPelaImpressao { get; } = new List<string>();

        /// <summary>Jogos que eu mandei religar na janela de revisão.</summary>
        public List<string> ReligadosPorMim { get; } = new List<string>();

        public int TotalDeMudancas => Adicionados.Count + ExecutavelAtualizado.Count
                                      + ReligadosPelaImpressao.Count + ReligadosPorMim.Count;

        public string Resumir()
            => $"{Adicionados.Count} adicionado(s), {ExecutavelAtualizado.Count} atualizado(s), " +
               $"{ReligadosPelaImpressao.Count} reconhecido(s) depois de mudar de pasta, " +
               $"{ReligadosPorMim.Count} religado(s) por mim, " +
               $"{MantidosPelaMarcaDeFixado.Count} preservado(s) por estarem fixados, " +
               $"{Destravados.Count} destravado(s), " +
               $"{SemMudanca.Count} sem mudança, {ForaDaRaiz.Count} fora do HD do launcher.";
    }

    /// <summary>
    /// Leva o resultado confirmado da revisão para dentro da biblioteca.
    ///
    /// Duas regras mandam aqui:
    ///  - <c>executavelFixadoPeloUsuario</c> é intocável: se eu corrigi a escolha uma vez,
    ///    nenhum rescan futuro pode desfazer;
    ///  - nada entra com caminho absoluto. Se o jogo estiver em outro drive,
    ///    ele é recusado em vez de gravar uma letra de drive no JSON.
    /// </summary>
    public static class MescladorDeBiblioteca
    {
        public static ResumoDaMesclagem Mesclar(Biblioteca biblioteca, IEnumerable<JogoRevisado> revisados)
        {
            if (biblioteca is null) throw new ArgumentNullException(nameof(biblioteca));

            var resumo = new ResumoDaMesclagem();

            foreach (var revisado in revisados)
            {
                if (!revisado.Incluir) continue;

                if (!Caminhos.TentarParaRelativo(revisado.Escolhido.Caminho, out var executavelRelativo))
                {
                    resumo.ForaDaRaiz.Add(revisado.Titulo);
                    continue;
                }

                var impressao = ImpressaoDigital.De(revisado.Escolhido.Caminho);

                // Passo 1: o caminho. Barato e resolve o caso comum.
                var existente = biblioteca.ObterPorExecutavel(executavelRelativo)
                                ?? ObterPorPastaDoJogo(biblioteca, revisado.PastaDoJogo);

                if (existente is not null)
                {
                    // Jogo catalogado antes da fase 10 ganha a impressão dele agora, que é
                    // enquanto o caminho ainda bate. Depois de a pasta sumir, é tarde.
                    if (impressao != null) existente.Impressao = impressao;

                    AtualizarExistente(existente, revisado, executavelRelativo, resumo);
                    continue;
                }

                // Passo 2: a pasta mudou de nome ou de lugar, mas o executável é o mesmo.
                if (ObterPorImpressao(biblioteca, impressao) is { } movido)
                {
                    Religar(movido, executavelRelativo, impressao);
                    resumo.ReligadosPelaImpressao.Add(movido.Titulo);
                    continue;
                }

                // Passo 3: religação que eu marquei na revisão. Nunca acontece sozinha.
                if (biblioteca.ObterPorId(revisado.ReligarComId) is { } escolhidoPorMim)
                {
                    Religar(escolhidoPorMim, executavelRelativo, impressao);
                    resumo.ReligadosPorMim.Add(escolhidoPorMim.Titulo);
                    continue;
                }

                // Passo 4: é jogo novo mesmo.
                biblioteca.Jogos.Add(new Jogo
                {
                    Id = biblioteca.GerarId(revisado.Titulo),
                    Titulo = revisado.Titulo,
                    ExecutavelRelativo = executavelRelativo,
                    Impressao = impressao,
                    ExecutavelFixadoPeloUsuario = revisado.Fixar
                });
                resumo.Adicionados.Add(revisado.Titulo);
            }

            return resumo;
        }

        /// <summary>
        /// Jogo que já está na biblioteca. Só o executável e o cadeado podem mudar —
        /// título, minutos jogados, favorito e capa são meus e ficam como estão.
        ///
        /// O cadeado da linha manda: ele vem marcado quando o jogo já estava fixado, então
        /// confirmar um rescan sem mexer em nada preserva a trava. Desmarcar é o jeito de
        /// destravar, e é uma decisão consciente, com o estado à vista na grade.
        /// </summary>
        private static void AtualizarExistente(Jogo existente, JogoRevisado revisado,
                                               string executavelRelativo, ResumoDaMesclagem resumo)
        {
            var estavaFixado = existente.ExecutavelFixadoPeloUsuario;
            var mudouExecutavel = !string.Equals(existente.ExecutavelRelativo, executavelRelativo,
                                                 StringComparison.OrdinalIgnoreCase);

            // Trava mantida e nada mudou: o rescan passou por aqui e não encostou.
            if (estavaFixado && revisado.Fixar && !mudouExecutavel)
            {
                resumo.MantidosPelaMarcaDeFixado.Add(existente.Titulo);
                return;
            }

            existente.ExecutavelFixadoPeloUsuario = revisado.Fixar;
            if (estavaFixado && !revisado.Fixar) resumo.Destravados.Add(existente.Titulo);

            if (!mudouExecutavel)
            {
                if (!resumo.Destravados.Contains(existente.Titulo)) resumo.SemMudanca.Add(existente.Titulo);
                return;
            }

            existente.ExecutavelRelativo = executavelRelativo;
            resumo.ExecutavelAtualizado.Add(existente.Titulo);
        }

        /// <summary>
        /// Move um jogo já catalogado para o caminho novo. Id, título, tempo jogado, capa
        /// e favorito ficam exatamente como estavam — o jogo é o mesmo, só a pasta mudou.
        ///
        /// A marca de executável fixado não impede a religação: ela protege a *escolha do
        /// executável*, e aqui o executável é literalmente o mesmo arquivo. O que muda é
        /// onde ele está.
        /// </summary>
        private static void Religar(Jogo jogo, string executavelRelativo, string? impressao)
        {
            jogo.ExecutavelRelativo = executavelRelativo;
            if (impressao != null) jogo.Impressao = impressao;
        }

        /// <summary>
        /// O jogo da biblioteca que tem esta impressão — e só se for exatamente um, e só
        /// se o executável dele tiver sumido do lugar antigo.
        ///
        /// As duas condições existem por casos reais:
        ///
        /// - **Dois jogos com a mesma impressão** (repack duplicado, pasta copiada): não dá
        ///   para saber qual dos dois se mudou. Cai na religação manual.
        /// - **O executável antigo ainda existe**: então nada se mudou — eu copiei a pasta,
        ///   e o que o scanner achou é a cópia. Religar aqui arrastaria o histórico do
        ///   original para a duplicata.
        /// </summary>
        private static Jogo? ObterPorImpressao(Biblioteca biblioteca, string? impressao)
        {
            if (string.IsNullOrEmpty(impressao)) return null;

            Jogo? achado = null;

            foreach (var jogo in biblioteca.Jogos)
            {
                if (!string.Equals(jogo.Impressao, impressao, StringComparison.Ordinal)) continue;

                if (achado != null) return null;    // colisão: não religa nenhum dos dois
                achado = jogo;
            }

            return achado != null && !achado.ExecutavelExiste() ? achado : null;
        }

        /// <summary>
        /// Anota, em cada linha da revisão, o jogo sumido cujo título bate com ela. É só a
        /// oferta: quem decide religar sou eu, marcando na tela.
        /// </summary>
        public static void SugerirReligacoes(Biblioteca biblioteca, IEnumerable<JogoRevisado> revisados)
        {
            var ausentes = new List<Jogo>();
            foreach (var jogo in biblioteca.Jogos)
            {
                if (!jogo.ExecutavelExiste()) ausentes.Add(jogo);
            }

            if (ausentes.Count == 0) return;

            foreach (var revisado in revisados)
            {
                if (revisado.JaNaBiblioteca) continue;

                var titulo = Textos.RemoverAcentos(revisado.Titulo).ToLowerInvariant().Trim();

                foreach (var ausente in ausentes)
                {
                    if (Textos.RemoverAcentos(ausente.Titulo).ToLowerInvariant().Trim() != titulo) continue;

                    revisado.ReligacaoPossivel = ausente;
                    break;
                }
            }
        }

        /// <summary>
        /// Acha o jogo da biblioteca que mora nesta pasta. É o que faz um rescan reconhecer
        /// o jogo que já está cadastrado mesmo quando o executável escolhido mudou.
        /// </summary>
        public static Jogo? ObterPorPastaDoJogo(Biblioteca biblioteca, string pastaDoJogo)
        {
            if (!Caminhos.TentarParaRelativo(pastaDoJogo, out var pastaRelativa)) return null;

            var prefixo = pastaRelativa.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var jogo in biblioteca.Jogos)
            {
                if (jogo.ExecutavelRelativo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
                    return jogo;
            }
            return null;
        }

        /// <summary>
        /// Registra as pastas escaneadas na biblioteca, sempre relativas. Pasta de fora
        /// da raiz é ignorada em silêncio — ela não teria como ser reescaneada no
        /// próximo PC de qualquer jeito.
        /// </summary>
        public static void RegistrarPastasEscaneadas(Biblioteca biblioteca, IEnumerable<string> pastasAbsolutas)
        {
            foreach (var pasta in pastasAbsolutas)
            {
                if (!Caminhos.TentarParaRelativo(pasta, out var relativa)) continue;

                if (!biblioteca.PastasEscaneadas.Contains(relativa, StringComparer.OrdinalIgnoreCase))
                    biblioteca.PastasEscaneadas.Add(relativa);
            }
        }
    }
}
