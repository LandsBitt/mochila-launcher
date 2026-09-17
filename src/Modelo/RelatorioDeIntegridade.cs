using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mochila.Dados;
using Mochila.Scanner;

namespace Mochila.Modelo
{
    /// <summary>O tipo de problema que o relatório encontrou. Cada um tem sua ação.</summary>
    public enum TipoDeProblema
    {
        /// <summary>O executável salvo não está mais lá. HD reorganizado, pasta renomeada.</summary>
        JogoNaoEncontrado,

        /// <summary>A biblioteca aponta para uma capa, hero ou logo que não existe (ou está vazia).</summary>
        ArteQuebrada,

        /// <summary>Uma pasta com cara de jogo que nenhum card da biblioteca representa.</summary>
        PastaNova
    }

    /// <summary>Um item da lista: o que está errado, com quem, e onde.</summary>
    public sealed class ProblemaDeIntegridade
    {
        public ProblemaDeIntegridade(TipoDeProblema tipo, string descricao, string alvo, Jogo? jogo = null)
        {
            Tipo = tipo;
            Descricao = descricao ?? "";
            Alvo = alvo ?? "";
            Jogo = jogo;
        }

        public TipoDeProblema Tipo { get; }

        /// <summary>A linha que aparece na tela.</summary>
        public string Descricao { get; }

        /// <summary>O caminho envolvido: o exe, o arquivo de arte, ou a pasta nova.</summary>
        public string Alvo { get; }

        /// <summary>O jogo, quando o problema tem dono. null nas pastas novas.</summary>
        public Jogo? Jogo { get; }

        public override string ToString() => Descricao;
    }

    /// <summary>
    /// O relatório de integridade do acervo (fase 17).
    ///
    /// <b>O launcher já sabia de tudo isto — ele só nunca contou.</b> Card sem arte, jogo
    /// que não abre mais e pasta que apareceu no HD depois do último scan são coisas que
    /// aparecem uma de cada vez, no meio do uso, e por isso nunca viram uma decisão. Juntas
    /// numa lista, viram: "7 jogos não encontrados, 3 artes quebradas, 2 pastas novas".
    ///
    /// Duas regras mandam aqui:
    ///
    /// 1. <b>Contar não conserta nada.</b> Este arquivo inteiro é leitura: nada é gravado,
    ///    apagado, religado ou baixado. Quem age é a tela, item por item, quando eu mandar.
    ///    Um relatório que conserta sozinho é um relatório em que eu não posso clicar sem
    ///    medo — e a primeira coisa que se faz com um é rodá-lo para ver.
    ///
    /// 2. <b>É caro, então é sob demanda e em thread.</b> Achar pasta nova é reusar o
    ///    scanner da fase 2 no acervo inteiro; isso é trabalho de disco de verdade e não
    ///    tem nada que fazer no caminho de abrir a grade.
    /// </summary>
    public sealed class RelatorioDeIntegridade
    {
        private readonly List<ProblemaDeIntegridade> _problemas = new List<ProblemaDeIntegridade>();

        public IReadOnlyList<ProblemaDeIntegridade> Problemas => _problemas;

        public int JogosNaoEncontrados { get; private set; }

        public int ArtesQuebradas { get; private set; }

        public int PastasNovas { get; private set; }

        /// <summary>
        /// true quando as pastas novas NÃO foram procuradas — sem pasta escaneada
        /// cadastrada, ou porque a varredura falhou.
        ///
        /// Existe para a tela não escrever "0 pastas novas" quando a verdade é "não olhei".
        /// Zero medido e zero por falta de medição são coisas diferentes, e confundi-las é
        /// como um relatório perde a serventia.
        /// </summary>
        public bool PastasNaoVarridas { get; private set; }

        public bool TudoCerto => _problemas.Count == 0;

        /// <summary>A linha de manchete, no formato que a spec pede.</summary>
        public string Resumo()
        {
            if (TudoCerto)
            {
                return PastasNaoVarridas
                    ? "Nenhum problema nos jogos catalogados. (Não há pasta escaneada para procurar novidade.)"
                    : "Está tudo certo: nenhum jogo sumido, nenhuma arte quebrada, nenhuma pasta nova.";
            }

            var partes = new List<string>
            {
                $"{JogosNaoEncontrados} jogo(s) não encontrado(s)",
                $"{ArtesQuebradas} arte(s) quebrada(s)"
            };

            partes.Add(PastasNaoVarridas
                ? "pastas novas não verificadas"
                : $"{PastasNovas} pasta(s) nova(s) desde o último scan");

            return string.Join(", ", partes.ToArray()) + ".";
        }

        // ---- Montagem ------------------------------------------------------------------------

        /// <summary>
        /// Varre a biblioteca e o disco e devolve o que está errado. <b>Não muda nada.</b>
        /// </summary>
        /// <param name="disco">
        /// null usa o disco de verdade. O parâmetro existe para o <c>--autoteste</c> montar
        /// um acervo simulado sem depender do HD.
        /// </param>
        public static RelatorioDeIntegridade Montar(Biblioteca biblioteca, Config config,
                                                    ISistemaDeArquivos? disco = null,
                                                    IProgress<string>? progresso = null,
                                                    CancellationToken cancelamento = default)
        {
            if (biblioteca is null) throw new ArgumentNullException(nameof(biblioteca));
            if (config is null) throw new ArgumentNullException(nameof(config));

            var relatorio = new RelatorioDeIntegridade();

            relatorio.ConferirJogos(biblioteca, progresso, cancelamento);
            relatorio.ProcurarPastasNovas(biblioteca, config, disco ?? new SistemaDeArquivosReal(),
                                          progresso, cancelamento);

            return relatorio;
        }

        private void ConferirJogos(Biblioteca biblioteca, IProgress<string>? progresso,
                                   CancellationToken cancelamento)
        {
            foreach (var jogo in biblioteca.Jogos)
            {
                cancelamento.ThrowIfCancellationRequested();
                progresso?.Report($"Conferindo \"{jogo.Titulo}\"...");

                if (!jogo.ExecutavelExiste())
                {
                    JogosNaoEncontrados++;
                    _problemas.Add(new ProblemaDeIntegridade(TipoDeProblema.JogoNaoEncontrado,
                        $"\"{jogo.Titulo}\" — o executável não está em {jogo.ExecutavelRelativo}",
                        jogo.ExecutavelRelativo, jogo));
                }

                ConferirArte(jogo, "capa", jogo.CapaArquivo, jogo.CaminhoCapa());
                ConferirArte(jogo, "arte de fundo", jogo.HeroArquivo, jogo.CaminhoHero());
                ConferirArte(jogo, "logo", jogo.LogoArquivo, jogo.CaminhoLogo());
            }
        }

        /// <summary>
        /// Um arquivo de arte está quebrado quando a biblioteca aponta para ele e ele não
        /// está lá — ou está lá com zero byte, que é como termina um download interrompido
        /// pelo HD ser desconectado no meio.
        ///
        /// Jogo <b>sem</b> capa não é problema nenhum: é o estado normal de metade de um
        /// acervo antigo, e a grade desenha a capa de reserva. O que se conta aqui é a
        /// promessa quebrada — o card que diz ter arte e não tem.
        /// </summary>
        private void ConferirArte(Jogo jogo, string qual, string? arquivo, string? caminho)
        {
            if (string.IsNullOrEmpty(arquivo)) return;

            var quebrada = false;

            try
            {
                quebrada = caminho is null || !File.Exists(caminho) || new FileInfo(caminho).Length == 0;
            }
            catch (Exception)
            {
                // Não deu para olhar (HD desconectado no meio da varredura): não vale
                // acusar de quebrado o que talvez esteja inteiro.
                return;
            }

            if (!quebrada) return;

            ArtesQuebradas++;
            _problemas.Add(new ProblemaDeIntegridade(TipoDeProblema.ArteQuebrada,
                $"\"{jogo.Titulo}\" — {qual} some do disco ({arquivo})",
                arquivo!, jogo));
        }

        /// <summary>
        /// Pastas com cara de jogo que nenhum card representa.
        ///
        /// Quem responde isso é o <b>scanner da fase 2</b>, o mesmo do F6, com o mesmo
        /// filtro de exclusão do <c>config.json</c>. Reimplementar aqui uma varredura mais
        /// simples produziria uma segunda opinião sobre o que é um jogo — e duas heurísticas
        /// divergentes é como o relatório passaria a acusar "pasta nova" para coisa que o
        /// F6 descarta, toda vez, sem conserto possível.
        ///
        /// A comparação é por PASTA, não por executável: um jogo cujo exe eu troquei à mão
        /// continua sendo o mesmo jogo, e ele não pode reaparecer aqui como novidade.
        /// </summary>
        private void ProcurarPastasNovas(Biblioteca biblioteca, Config config, ISistemaDeArquivos disco,
                                         IProgress<string>? progresso, CancellationToken cancelamento)
        {
            var raizes = new List<string>();

            foreach (var relativa in biblioteca.PastasEscaneadas)
            {
                if (Caminhos.ParaAbsolutoOuNulo(relativa) is { } absoluta) raizes.Add(absoluta);
            }

            if (raizes.Count == 0)
            {
                PastasNaoVarridas = true;
                return;
            }

            var jaCatalogadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var jogo in biblioteca.Jogos)
            {
                if (jogo.PastaDoJogo() is { } pasta) jaCatalogadas.Add(Normalizar(pasta));
            }

            List<JogoDetectado> encontrados;
            try
            {
                var scanner = new ScannerDeJogos(disco, FiltroDeExclusao.De(config));
                var relato = new Progress<ProgressoScan>(p => progresso?.Report($"Varrendo {p.PastaAtual}..."));

                encontrados = scanner.Escanear(raizes, relato, cancelamento);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // O HD saiu no meio, ou uma pasta negou acesso. O que já foi conferido nos
                // jogos continua valendo — e a tela diz que esta parte não foi olhada, em
                // vez de mostrar um zero que eu leria como "nenhuma novidade".
                PastasNaoVarridas = true;
                return;
            }

            foreach (var achado in encontrados)
            {
                cancelamento.ThrowIfCancellationRequested();

                if (jaCatalogadas.Contains(Normalizar(achado.PastaDoJogo))) continue;

                PastasNovas++;

                var relativa = Caminhos.TentarParaRelativo(achado.PastaDoJogo, out var caminho)
                    ? caminho
                    : achado.PastaDoJogo;

                _problemas.Add(new ProblemaDeIntegridade(TipoDeProblema.PastaNova,
                    $"\"{achado.TituloProposto}\" — pasta no HD sem card na biblioteca ({relativa})",
                    relativa));
            }
        }

        private static string Normalizar(string caminho)
        {
            try
            {
                return Path.GetFullPath(caminho).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return caminho.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}
