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

        /// <summary>O runtime DirectX gravou classes COM no HKCU para esta sessão: apagar no fim.</summary>
        private bool _runtimeRegistrou;

        /// <summary>Disparado quando o processo do jogo encerra. Vem de fora da thread da UI.</summary>
        public event EventHandler<SessaoTerminadaEventArgs>? SessaoTerminada;

        /// <summary>
        /// Disparado quando a saída rápida era um launcher próprio passando o bastão e o
        /// jogo de verdade foi encontrado. A janela deve continuar escondida.
        /// </summary>
        public event EventHandler<Jogo>? ProcessoFilhoAdotado;

        /// <summary>
        /// Um gancho da fase 15 deu errado sem impedir nada: script que sumiu, script que
        /// passou dos 30 s, prioridade que o Windows recusou, registro do DirectX portátil
        /// que o Windows negou.
        ///
        /// É recado de rodapé, nunca caixa de diálogo — e nunca vem antes do jogo. O do
        /// "antes" chega na thread de quem chamou <see cref="Lancar"/>; o do "depois" vem
        /// de fora da thread da UI, como o <see cref="SessaoTerminada"/>.
        /// </summary>
        public event EventHandler<string>? AvisoDeScript;

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

            // MontarInicio ANTES do script: ele é quem descobre que o executável sumiu, e
            // rodar o gancho de preparação de um jogo que nem vai abrir é trabalho jogado
            // fora — pior, é um .bat mexendo em arquivo por causa de um lançamento que
            // termina em caixa de erro.
            var inicio = MontarInicio(jogo);

            // O gancho de antes (fase 15). Síncrono, com teto de 30 s, e incapaz de
            // impedir o lançamento: o que der errado vira recado no rodapé.
            var antes = ScriptsDoJogo.RodarAntes(jogo.OpcoesDeExecucao.ScriptAntes, jogo.PastaDoJogo());

            // Fotografa os processos ANTES de abrir: se depois aparecer um novo dentro da
            // pasta do jogo, é o filho. Sem a foto, eu adotaria um jogo que já estava aberto.
            //
            // A foto vem DEPOIS do script de propósito: o cmd.exe do gancho é um processo
            // novo, e fotografar antes dele o deixaria de fora da lista de conhecidos.
            var anteriores = CacadorDeProcessoFilho.FotografarProcessos();

            // O DirectX portátil: registra o áudio que falta e põe a pasta das DLLs no PATH
            // que o jogo herda. Sem runtime baixado, não faz nada.
            var runtime = RuntimeDirectX.Preparar(inicio.FileName);

            Process? processo;
            try
            {
                using (runtime.AplicarNoAmbiente()) processo = Process.Start(inicio);
            }
            catch (Exception)
            {
                // UAC recusado, exe bloqueado: jogo que não abriu não deixa registro para trás.
                if (runtime.ClassesRegistradas > 0) RuntimeDirectX.DesfazerRegistro();
                throw;
            }

            if (processo is null)
            {
                if (runtime.ClassesRegistradas > 0) RuntimeDirectX.DesfazerRegistro();

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
                _runtimeRegistrou = runtime.ClassesRegistradas > 0;
            }

            // Prioridade depois do Start, porque é só aí que existe processo para elevar.
            AplicarPrioridade(processo, jogo);

            // Evento, não loop: é isto que deixa o launcher dormir enquanto o jogo roda.
            processo.EnableRaisingEvents = true;
            processo.Exited += AoSairDoJogo;

            // O recado do gancho vem por último: o jogo já está subindo, que é o que
            // importa. Avisar antes do Process.Start faria um script sumido parecer motivo
            // para o lançamento não acontecer.
            if (antes.TemAviso) AvisoDeScript?.Invoke(this, antes.Aviso);
            if (runtime.Aviso != null) AvisoDeScript?.Invoke(this, runtime.Aviso);

            // Corrida possível: processo que morreu entre o Start e o registro do evento.
            if (processo.HasExited) AoSairDoJogo(processo, EventArgs.Empty);
        }

        private void AplicarPrioridade(Process processo, Jogo jogo)
            => AplicarPrioridade(processo, jogo, aviso => AvisoDeScript?.Invoke(this, aviso));

        /// <summary>
        /// Sobe a prioridade do processo do jogo, se o jogo pedir isso (fase 15).
        ///
        /// <b>Falhar aqui não pode derrubar o lançamento</b>, e é regra da spec: elevar
        /// prioridade é a primeira coisa que uma política de grupo, um antivírus ou uma
        /// conta sem privilégio recusa. O jogo abrindo em prioridade normal é infinitamente
        /// melhor que o jogo não abrindo. Daí o catch largo: aqui não há erro que valha
        /// mais que o lançamento.
        ///
        /// "Alta" para de propósito em <c>High</c> e não chega em <c>RealTime</c>: tempo
        /// real tira ciclo do próprio teclado e do mouse, e num PC fraco — que é o alvo
        /// deste launcher — isso trava a máquina inteira em vez de acelerar o jogo.
        ///
        /// Público e estático pelo mesmo motivo de <see cref="MontarInicio"/>: o teste
        /// precisa exercer o caso que importa (um processo que já morreu) sem depender de
        /// ganhar uma corrida.
        /// </summary>
        public static void AplicarPrioridade(Process processo, Jogo jogo, Action<string>? avisar = null)
        {
            if (processo is null) throw new ArgumentNullException(nameof(processo));
            if (jogo is null) throw new ArgumentNullException(nameof(jogo));

            var prioridade = jogo.OpcoesDeExecucao.Prioridade;
            if (prioridade == PrioridadeDoProcesso.Normal) return;

            try
            {
                processo.PriorityClass = prioridade == PrioridadeDoProcesso.Alta
                    ? ProcessPriorityClass.High
                    : ProcessPriorityClass.AboveNormal;
            }
            catch (Exception erro)
            {
                // Processo que já morreu, permissão negada, política da máquina.
                avisar?.Invoke(
                    $"Não consegui pôr \"{jogo.Titulo}\" em prioridade " +
                    $"{OpcoesDeExecucao.TextoDaPrioridade(prioridade)} ({erro.Message}). " +
                    "O jogo abriu normalmente.");
            }
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

            // O filho é o jogo de verdade — é NELE que a prioridade da fase 15 importa. O
            // processo que eu lancei era o launcher próprio, e ele já morreu.
            AplicarPrioridade(filho, jogo);

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
            bool desfazerRuntime;

            lock (_trava)
            {
                _processo = null;
                _jogo = null;
                desfazerRuntime = _runtimeRegistrou;
                _runtimeRegistrou = false;
            }

            // O registro do DirectX portátil aponta para uma letra de drive. Deixá-lo depois
            // do jogo seria deixar no PC uma chave que quebra quando o HD vira outra letra.
            if (desfazerRuntime) RuntimeDirectX.DesfazerRegistro();

            // O gancho de depois (fase 15) roda aqui, com o jogo comprovadamente fora — e
            // não no Exited do processo, que ainda pode ser o launcher próprio passando o
            // bastão. Ele é disparado e esquecido: nada espera por um script de limpeza.
            //
            // Quando a fase 16 (saves portáteis, no planejamento interno) chegar, este é o ponto em que
            // ela entra ANTES desta linha: o "depois" tem que rodar com a junction já
            // desfeita, senão ele mexeria num save do outro lado do redirecionamento.
            var depois = ScriptsDoJogo.RodarDepois(jogo.OpcoesDeExecucao.ScriptDepois, jogo.PastaDoJogo());

            SessaoTerminada?.Invoke(this, new SessaoTerminadaEventArgs(jogo, inicioUtc, duracao));

            // Depois do fim da sessão, e não antes: quem ouve os dois eventos marshalla
            // cada um para a thread da UI, e a volta do jogo limpa o rodapé. Avisar antes
            // seria escrever um recado que a própria volta apagaria.
            if (depois.TemAviso) AvisoDeScript?.Invoke(this, depois.Aviso);
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
