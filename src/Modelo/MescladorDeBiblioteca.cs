using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Launcher.Dados;
using Launcher.Scanner;

namespace Launcher.Modelo
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

        public int TotalDeMudancas => Adicionados.Count + ExecutavelAtualizado.Count;

        public string Resumir()
            => $"{Adicionados.Count} adicionado(s), {ExecutavelAtualizado.Count} atualizado(s), " +
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

                var existente = biblioteca.ObterPorExecutavel(executavelRelativo)
                                ?? ObterPorPastaDoJogo(biblioteca, revisado.PastaDoJogo);

                if (existente is not null)
                {
                    AtualizarExistente(existente, revisado, executavelRelativo, resumo);
                    continue;
                }

                biblioteca.Jogos.Add(new Jogo
                {
                    Id = biblioteca.GerarId(revisado.Titulo),
                    Titulo = revisado.Titulo,
                    ExecutavelRelativo = executavelRelativo,
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
