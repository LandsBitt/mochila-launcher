using System;
using Mochila.Modelo;

namespace Mochila.Scanner
{
    /// <summary>
    /// Uma linha da janela de revisão: o que o scanner propôs mais o que eu mudei.
    ///
    /// O scanner nunca grava sozinho. Tudo o que ele acha vira uma destas linhas, eu
    /// confirmo, e só aí a biblioteca é tocada.
    /// </summary>
    public sealed class JogoRevisado
    {
        private CandidatoExecutavel _escolhido;

        public JogoRevisado(JogoDetectado deteccao)
        {
            Deteccao = deteccao ?? throw new ArgumentNullException(nameof(deteccao));
            Titulo = deteccao.TituloProposto;
            _escolhido = deteccao.Escolhido;
        }

        public JogoDetectado Deteccao { get; }

        /// <summary>Checkbox da linha. Começa marcado; desmarcar deixa o jogo de fora.</summary>
        public bool Incluir { get; set; } = true;

        /// <summary>Título proposto, editável na janela.</summary>
        public string Titulo { get; set; }

        /// <summary>Executável escolhido. Trocar aqui é o que marca a escolha como minha.</summary>
        public CandidatoExecutavel Escolhido
        {
            get => _escolhido;
            set => _escolhido = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// true quando o executável mostrado não é mais o que o scanner propôs.
        /// É só informação para a tela — NÃO tranca nada. Trocar de executável e
        /// trancar contra rescan são decisões separadas de propósito: trocar por engano
        /// não pode deixar a pasta imune a rescan para sempre.
        /// </summary>
        public bool EscolhaAlterada => !ReferenceEquals(_escolhido, Deteccao.Escolhido);

        /// <summary>
        /// O cadeado da grade: vira <c>executavelFixadoPeloUsuario</c> no JSON.
        /// Marcado, nenhum rescan mexe no executável deste jogo. É ação explícita minha,
        /// visível na lista e reversível a qualquer momento — inclusive num rescan
        /// futuro, quando a heurística estiver melhor e eu quiser deixar ela corrigir.
        /// </summary>
        public bool Fixar { get; set; }

        /// <summary>true quando este jogo já está na biblioteca (rescan, não descoberta).</summary>
        public bool JaNaBiblioteca { get; set; }

        /// <summary>
        /// Jogo da biblioteca marcado como não encontrado cujo título normalizado bate
        /// com o desta linha. É só uma sugestão para a tela — pasta renomeada faz o rescan
        /// achar que descobriu um jogo novo, e este campo é o que permite oferecer
        /// "religar" em vez de "adicionar".
        /// </summary>
        public Jogo? ReligacaoPossivel { get; set; }

        /// <summary>
        /// Id do jogo com que eu mandei religar. Começa vazio de propósito: título igual
        /// não é prova, e a decisão é minha. Enquanto isto estiver nulo, a linha adiciona
        /// um jogo novo, como sempre fez.
        /// </summary>
        public string? ReligarComId { get; set; }

        /// <summary>true quando eu aceitei a religação proposta.</summary>
        public bool VaiReligar => !string.IsNullOrEmpty(ReligarComId);

        /// <summary>Linha amarela: o scanner não está seguro desta escolha.</summary>
        public bool BaixaConfianca => _escolhido.Placar < Pontuador.PlacarDeConfianca;

        public string PastaDoJogo => Deteccao.PastaDoJogo;
    }
}
