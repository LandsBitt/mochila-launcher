using System;
using Mochila.Modelo;

namespace Mochila.Execucao
{
    /// <summary>
    /// A regra de quanto tempo contar numa sessão. Separada do processo de propósito:
    /// é regra de negócio, e regra dá para testar sem abrir jogo nenhum.
    /// </summary>
    public static class ContabilizadorDeTempo
    {
        /// <summary>
        /// Abaixo disso, o jogo não rodou: ele largou um processo-filho e o pai morreu na
        /// hora. É comum em jogo com launcher próprio, e contar esse tempo poluiria a
        /// estatística com sessões de 2 segundos.
        /// </summary>
        public static readonly TimeSpan LimiteDeSaidaImediata = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Credita a sessão no jogo. Devolve o que aconteceu, para a janela decidir o que
        /// mostrar. Só mexe no jogo quando a sessão conta.
        /// </summary>
        public static ResultadoDaSessao Contabilizar(Jogo jogo, TimeSpan duracao, DateTime agoraUtc)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));

            if (duracao < LimiteDeSaidaImediata)
                return new ResultadoDaSessao(duracao, contouTempo: false, segundosCreditados: 0);

            // Segundo é a unidade guardada justamente para não perder sessão curta: 50 s
            // creditavam zero quando o campo era minuto inteiro, e sumiam para sempre.
            var segundos = (int)Math.Round(duracao.TotalSeconds, MidpointRounding.AwayFromZero);

            jogo.SegundosJogados += segundos;
            jogo.UltimaVezJogado = agoraUtc;

            return new ResultadoDaSessao(duracao, contouTempo: true, segundosCreditados: segundos);
        }

        /// <summary>
        /// O registro que vai para o <c>sessoes.json</c> da fase 13, ou null quando a
        /// sessão não conta.
        ///
        /// Existe para a regra dos 5 segundos ter <b>um</b> dono: quem decide se conta
        /// tempo é <see cref="Contabilizar"/>, e o histórico apenas obedece. Duas cópias
        /// dessa condição sairiam de sincronia no dia em que o limite mudasse, e o sintoma
        /// seria uma sessão de 3 s no histórico somando zero no acumulado — impossível de
        /// explicar olhando a tela.
        /// </summary>
        public static Sessao? ParaHistorico(Jogo jogo, DateTime inicioUtc, ResultadoDaSessao? resultado)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));
            if (resultado is null || !resultado.ContouTempo) return null;

            return new Sessao(jogo.Id, inicioUtc, resultado.SegundosCreditados);
        }
    }

    /// <summary>O que uma sessão rendeu. Imutável: quem lê não muda nada sem querer.</summary>
    public sealed class ResultadoDaSessao
    {
        public ResultadoDaSessao(TimeSpan duracao, bool contouTempo, int segundosCreditados)
        {
            Duracao = duracao;
            ContouTempo = contouTempo;
            SegundosCreditados = segundosCreditados;
        }

        public TimeSpan Duracao { get; }

        public bool ContouTempo { get; }

        public int SegundosCreditados { get; }

        /// <summary>true quando o processo morreu rápido demais para ter sido o jogo.</summary>
        public bool SaidaImediata => !ContouTempo;
    }
}
