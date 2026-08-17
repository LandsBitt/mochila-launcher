using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mochila.Modelo;

namespace Mochila.Execucao
{
    /// <summary>
    /// Lança o jogo e avisa quando ele terminar.
    ///
    /// Duas regras da spec moram aqui:
    ///
    /// 1. WorkingDirectory é obrigatório e tem que ser a pasta do executável. Jogo antigo
    ///    procura assets em caminho relativo e crasha se o diretório de trabalho for outro
    ///    — é a causa número um de "abriu e fechou" em jogo de 2005.
    ///
    /// 2. O fim do jogo chega por evento (Process.Exited com EnableRaisingEvents), nunca
    ///    por loop de espera. Enquanto o jogo roda, o launcher tem que estar dormindo:
    ///    um polling de meio em meio segundo é CPU roubada de quem importa.
    /// </summary>
    public sealed class LancadorDeJogos : IDisposable
    {
        /// <summary>
        /// Espera antes de procurar o processo-filho. Dá tempo do jogo de verdade subir
        /// depois que o launcher próprio dele morreu.
        /// </summary>
        private static readonly TimeSpan EsperaAntesDeProcurarFilho = TimeSpan.FromSeconds(3);

        private readonly object _trava = new object();

        private Process? _processo;
        private Jogo? _jogo;
        private DateTime _inicioUtc;
        private HashSet<int> _processosAntesDoLancamento = new HashSet<int>();
        private System.Threading.Timer? _buscaDoFilho;

        /// <summary>Disparado quando o processo do jogo encerra. Vem de fora da thread da UI.</summary>
        public event EventHandler<SessaoTerminadaEventArgs>? SessaoTerminada;

        /// <summary>
        /// Disparado quando a saída rápida era um launcher próprio passando o bastão e o
        /// jogo de verdade foi encontrado. A janela deve continuar escondida.
        /// </summary>
        public event EventHandler<Jogo>? ProcessoFilhoAdotado;

        /// <summary>
        /// true do lançamento até eu ter certeza de que não sobrou nada rodando — inclui
        /// os 3 s de busca pelo processo-filho. É o que trava o card contra um segundo
        /// Enter: dois processos do mesmo jogo antigo escrevendo na mesma pasta SAVE\
        /// dão tela preta ou save corrompido.
        /// </summary>
        public bool JogoRodando
        {
            get { lock (_trava) return _processo != null || _jogo != null; }
        }

        public Jogo? JogoAtual
        {
            get { lock (_trava) return _jogo; }
        }

        /// <summary>
        /// Monta o ProcessStartInfo do jogo. Público e estático para o teste conferir o
        /// WorkingDirectory sem precisar abrir processo nenhum.
        /// </summary>
        public static ProcessStartInfo MontarInicio(Jogo jogo)
        {
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));

            var executavel = jogo.CaminhoExecutavel();
            if (string.IsNullOrEmpty(executavel))
                throw new ExecutavelIndisponivelException(jogo, "o caminho salvo não é válido.");

            if (!File.Exists(executavel))
                throw new ExecutavelIndisponivelException(jogo, $"não existe mais em \"{executavel}\".");

            var pasta = Path.GetDirectoryName(executavel);
            if (string.IsNullOrEmpty(pasta))
                throw new ExecutavelIndisponivelException(jogo, "não consegui descobrir a pasta do jogo.");

            return new ProcessStartInfo
            {
                FileName = executavel!,
                Arguments = jogo.Argumentos ?? "",
                WorkingDirectory = pasta!,      // CRÍTICO: sem isso, jogo antigo não acha os assets
                UseShellExecute = true
            };
        }

        /// <summary>
        /// Inicia o jogo. Lança <see cref="ExecutavelIndisponivelException"/> se o exe
        /// sumiu (HD reorganizado) — quem chama oferece "Localizar executável...".
        /// </summary>
        public void Lancar(Jogo jogo)
        {
            if (JogoRodando) throw new InvalidOperationException("Já existe um jogo em execução.");

            var inicio = MontarInicio(jogo);

            // Fotografa os processos ANTES de abrir: se depois aparecer um novo dentro da
            // pasta do jogo, é o filho. Sem a foto, eu adotaria um jogo que já estava aberto.
            var anteriores = CacadorDeProcessoFilho.FotografarProcessos();
            var processo = Process.Start(inicio);

            if (processo is null)
            {
                // O shell atendeu sem criar processo novo (raro para .exe, acontece com
                // .lnk que reaproveita instância). Sem processo não há o que acompanhar.
                throw new ExecutavelIndisponivelException(jogo,
                    "o Windows não devolveu um processo para acompanhar.");
            }

            lock (_trava)
            {
                _processo = processo;
                _jogo = jogo;
                _inicioUtc = DateTime.UtcNow;
                _processosAntesDoLancamento = anteriores;
            }

            // Evento, não loop: é isto que deixa o launcher dormir enquanto o jogo roda.
            processo.EnableRaisingEvents = true;
            processo.Exited += AoSairDoJogo;

            // Corrida possível: processo que morreu entre o Start e o registro do evento.
            if (processo.HasExited) AoSairDoJogo(processo, EventArgs.Empty);
        }

        private void AoSairDoJogo(object? remetente, EventArgs e)
        {
            Jogo? jogo;
            Process? processo;
            TimeSpan duracao;
            DateTime inicio;

            lock (_trava)
            {
                if (_processo is null || _jogo is null) return;   // já tratado

                jogo = _jogo;
                processo = _processo;
                inicio = _inicioUtc;
                duracao = DateTime.UtcNow - _inicioUtc;

                // O jogo sai de cena só depois de eu saber se existe um filho. Zerar aqui
                // liberaria o card para um segundo lançamento em cima do jogo que está subindo.
                _processo = null;
            }

            try
            {
                processo.Exited -= AoSairDoJogo;
                processo.Dispose();     // solta o handle do processo
            }
            catch (Exception)
            {
                // Processo já recolhido pelo sistema: nada a fazer.
            }

            // Saída rápida: pode ter sido um launcher próprio passando o bastão. Antes de
            // reaparecer por cima de um jogo em tela cheia, vale conferir.
            if (duracao < ContabilizadorDeTempo.LimiteDeSaidaImediata)
            {
                AgendarBuscaDoFilho(jogo, duracao);
                return;
            }

            EncerrarSessao(jogo, inicio, duracao);
        }

        /// <summary>
        /// Uma espera única, não vigilância: um Timer que dispara uma vez, varre os
        /// processos e morre. O launcher continua dormindo o tempo todo.
        /// </summary>
        private void AgendarBuscaDoFilho(Jogo jogo, TimeSpan duracaoAteAqui)
        {
            var timer = new System.Threading.Timer(_ => ProcurarFilho(jogo, duracaoAteAqui), null,
                                                   EsperaAntesDeProcurarFilho,
                                                   System.Threading.Timeout.InfiniteTimeSpan);

            lock (_trava) _buscaDoFilho = timer;
        }

        /// <param name="duracaoDoPai">
        /// Quanto o processo lançado viveu. É essa a duração relatada quando não existe
        /// filho — contar os 3 s da busca faria "fechou em 1 s" virar "fechou em 4 s".
        /// </param>
        private void ProcurarFilho(Jogo jogo, TimeSpan duracaoDoPai)
        {
            DescartarBuscaDoFilho();

            HashSet<int> anteriores;
            DateTime inicio;

            lock (_trava)
            {
                anteriores = _processosAntesDoLancamento;
                inicio = _inicioUtc;
            }

            Process? filho = null;
            try
            {
                filho = CacadorDeProcessoFilho.Procurar(jogo.PastaDoJogo(), anteriores);
            }
            catch (Exception)
            {
                // Varredura de processos é território de acesso negado: se falhar, o
                // caminho normal (reexibir sem roubar foco) continua valendo.
            }

            if (filho is null)
            {
                // Ninguém assumiu: foi saída rápida de verdade.
                EncerrarSessao(jogo, inicio, duracaoDoPai);
                return;
            }

            lock (_trava)
            {
                _processo = filho;
                _jogo = jogo;
                // _inicioUtc continua o do lançamento: o tempo do launcher próprio conta
                // como tempo de jogo, que é o que eu esperaria ver.
            }

            try
            {
                filho.EnableRaisingEvents = true;
                filho.Exited += AoSairDoJogo;

                if (filho.HasExited) { AoSairDoJogo(filho, EventArgs.Empty); return; }
            }
            catch (Exception)
            {
                lock (_trava) { _processo = null; }
                filho.Dispose();
                EncerrarSessao(jogo, inicio, duracaoDoPai);
                return;
            }

            ProcessoFilhoAdotado?.Invoke(this, jogo);
        }

        private void EncerrarSessao(Jogo jogo, DateTime inicioUtc, TimeSpan duracao)
        {
            lock (_trava)
            {
                _processo = null;
                _jogo = null;
            }

            SessaoTerminada?.Invoke(this, new SessaoTerminadaEventArgs(jogo, inicioUtc, duracao));
        }

        private void DescartarBuscaDoFilho()
        {
            System.Threading.Timer? timer;
            lock (_trava)
            {
                timer = _buscaDoFilho;
                _buscaDoFilho = null;
            }
            timer?.Dispose();
        }

        public void Dispose()
        {
            DescartarBuscaDoFilho();

            Process? processo;

            lock (_trava)
            {
                processo = _processo;
                _processo = null;
                _jogo = null;
            }

            if (processo is null) return;

            try
            {
                processo.Exited -= AoSairDoJogo;
                processo.Dispose();     // fechar o launcher NÃO mata o jogo aberto
            }
            catch (Exception)
            {
            }
        }
    }

    public sealed class SessaoTerminadaEventArgs : EventArgs
    {
        public SessaoTerminadaEventArgs(Jogo jogo, DateTime inicioUtc, TimeSpan duracao)
        {
            Jogo = jogo;
            InicioUtc = inicioUtc.Kind == DateTimeKind.Utc ? inicioUtc : inicioUtc.ToUniversalTime();
            Duracao = duracao;
        }

        public Jogo Jogo { get; }

        /// <summary>
        /// Quando a sessão começou, em UTC. Vem daqui, e não de "agora menos a duração",
        /// porque no caso do processo-filho adotado as duas contas não dão o mesmo: o fim
        /// chega depois dos 3 s de busca pelo filho, e o histórico da fase 13 quer a hora
        /// em que eu apertei Enter.
        /// </summary>
        public DateTime InicioUtc { get; }

        public TimeSpan Duracao { get; }
    }

    /// <summary>O executável do jogo não está mais lá — HD reorganizado, pasta renomeada.</summary>
    public sealed class ExecutavelIndisponivelException : Exception
    {
        public ExecutavelIndisponivelException(Jogo jogo, string motivo)
            : base($"Não consegui abrir \"{jogo.Titulo}\": {motivo}")
        {
            Jogo = jogo;
        }

        public Jogo Jogo { get; }
    }
}
