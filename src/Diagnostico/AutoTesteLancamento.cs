using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Launcher.Dados;
using Launcher.Execucao;
using Launcher.Modelo;
using Launcher.Util;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Verificação da fase 5: montagem do ProcessStartInfo, contagem de tempo e o ciclo
    /// completo com um processo de verdade.
    ///
    /// O teste do WorkingDirectory parece bobo e é o mais importante do arquivo: jogo
    /// antigo procura assets em caminho relativo, e lançar com o diretório de trabalho
    /// errado é a diferença entre abrir e fechar sozinho na cara do usuário.
    /// </summary>
    public static class AutoTesteLancamento
    {
        private const string NomePastaSandbox = "_autoteste-lancamento-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarMontagemDoInicio(v, raizReal);
                TestarContagemDeTempo(v);
                TestarMigracaoDeMinutosParaSegundos(v, raizReal);
                TestarCicloComProcessoReal(v, raizReal);
                TestarTravaDeLancamentoDuplo(v, raizReal);
                TestarAdocaoDeProcessoFilho(v, raizReal);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
                LimparSandbox(v, raizReal);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- ProcessStartInfo -------------------------------------------------------------

        private static void TestarMontagemDoInicio(Verificador v, string raizReal)
        {
            v.Escrever("Montagem do lançamento");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "Need For Speed Most Wanted");
            Directory.CreateDirectory(pastaDoJogo);

            var exe = Path.Combine(pastaDoJogo, "speed.exe");
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });

            var jogo = new Jogo
            {
                Id = "nfsmw",
                Titulo = "Need for Speed: Most Wanted",
                ExecutavelRelativo = Path.Combine("Jogos", "Need For Speed Most Wanted", "speed.exe"),
                Argumentos = "-windowed"
            };

            var inicio = LancadorDeJogos.MontarInicio(jogo);

            v.VerificarTexto("FileName é o caminho absoluto deste PC", exe, inicio.FileName);

            v.VerificarTexto("WorkingDirectory é a pasta do jogo (o item crítico da spec)",
                pastaDoJogo, inicio.WorkingDirectory);

            v.VerificarTexto("argumentos são repassados", "-windowed", inicio.Arguments);

            v.Verificar("UseShellExecute ligado (deixa o Windows resolver elevação e associação)",
                inicio.UseShellExecute);

            // Executável sumido: erro tratado, não crash.
            var sumido = new Jogo
            {
                Id = "sumido",
                Titulo = "Jogo Sumido",
                ExecutavelRelativo = Path.Combine("Jogos", "Sumido", "sumido.exe")
            };

            var lancou = true;
            try
            {
                LancadorDeJogos.MontarInicio(sumido);
            }
            catch (ExecutavelIndisponivelException)
            {
                lancou = false;
            }

            v.Verificar("executável ausente vira erro tratado, não exceção solta", !lancou);

            // Caminho absoluto salvo na biblioteca é bug de portabilidade: tem que recusar.
            var comDrive = new Jogo { Id = "x", Titulo = "X", ExecutavelRelativo = @"D:\Jogos\x.exe" };
            var recusou = false;
            try
            {
                LancadorDeJogos.MontarInicio(comDrive);
            }
            catch (ExecutavelIndisponivelException)
            {
                recusou = true;
            }

            v.Verificar("caminho com letra de drive é recusado", recusou);
        }

        // ---- Contagem de tempo -------------------------------------------------------------

        private static void TestarContagemDeTempo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Contagem de tempo");

            var agora = new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc);

            // Sessão normal.
            var jogo = new Jogo { Id = "a", Titulo = "A", SegundosJogados = 6000 };
            var normal = ContabilizadorDeTempo.Contabilizar(jogo, TimeSpan.FromMinutes(45), agora);

            v.Verificar("45 min viram 2700 s", normal.SegundosCreditados == 2700, normal.SegundosCreditados.ToString());
            v.Verificar("somados ao que já havia", jogo.SegundosJogados == 8700, jogo.SegundosJogados.ToString());
            v.Verificar("última vez jogado gravada em UTC",
                jogo.UltimaVezJogado == agora && jogo.UltimaVezJogado!.Value.Kind == DateTimeKind.Utc);
            v.Verificar("sessão normal conta", normal.ContouTempo && !normal.SaidaImediata);

            // Processo-filho: o pai morre em 2 s.
            var comLauncher = new Jogo { Id = "b", Titulo = "B", SegundosJogados = 1800 };
            var imediata = ContabilizadorDeTempo.Contabilizar(comLauncher, TimeSpan.FromSeconds(2), agora);

            v.Verificar("saída em 2 s não conta tempo", imediata.SaidaImediata && !imediata.ContouTempo);
            v.Verificar("e não mexe no tempo", comLauncher.SegundosJogados == 1800, comLauncher.SegundosJogados.ToString());
            v.Verificar("nem grava a data (eu não joguei)", comLauncher.UltimaVezJogado is null);

            // A fronteira exata dos 5 s da spec.
            var naFronteira = new Jogo { Id = "c", Titulo = "C" };
            var cinco = ContabilizadorDeTempo.Contabilizar(naFronteira, TimeSpan.FromSeconds(5), agora);
            v.Verificar("exatamente 5 s já conta", cinco.ContouTempo);

            var quaseCinco = new Jogo { Id = "d", Titulo = "D" };
            var quatro = ContabilizadorDeTempo.Contabilizar(quaseCinco, TimeSpan.FromSeconds(4.9), agora);
            v.Verificar("4,9 s não conta", quatro.SaidaImediata);

            // O motivo da troca de minuto para segundo: sessão curta não pode sumir.
            var curta = new Jogo { Id = "e", Titulo = "E" };
            var cinquenta = ContabilizadorDeTempo.Contabilizar(curta, TimeSpan.FromSeconds(50), agora);
            v.Verificar("50 s creditam 50 s (em minuto inteiro isso virava zero)",
                cinquenta.SegundosCreditados == 50, cinquenta.SegundosCreditados.ToString());
            v.Verificar("e vão para a conta do jogo", curta.SegundosJogados == 50, curta.SegundosJogados.ToString());
            v.Verificar("a data é gravada de qualquer forma", curta.UltimaVezJogado == agora);

            // Várias sessões curtas seguidas somam — antes somavam zero para sempre.
            var acumulada = new Jogo { Id = "e2", Titulo = "E2" };
            for (var i = 0; i < 10; i++)
                ContabilizadorDeTempo.Contabilizar(acumulada, TimeSpan.FromSeconds(50), agora);

            v.Verificar("10 sessões de 50 s viram 500 s (8 min)", acumulada.SegundosJogados == 500,
                acumulada.SegundosJogados.ToString());

            var maratona = new Jogo { Id = "f", Titulo = "F" };
            var horas = ContabilizadorDeTempo.Contabilizar(maratona, TimeSpan.FromHours(3.5), agora);
            v.Verificar("3h30 viram 12600 s", horas.SegundosCreditados == 12600, horas.SegundosCreditados.ToString());

            // E o que o rodapé mostra a partir disso.
            v.VerificarTexto("exibição de sessão curta", "50 s jogados", TempoDeJogo.Descrever(50));
            v.VerificarTexto("exibição em minutos", "8 min jogados", TempoDeJogo.Descrever(500));
            v.VerificarTexto("exibição em horas", "3 h 30 min jogados", TempoDeJogo.Descrever(12600));
            v.VerificarTexto("nunca jogado", "nunca jogado", TempoDeJogo.Descrever(0));

            // Favorito e capa não podem ser tocados pela contabilização.
            var favorito = new Jogo { Id = "g", Titulo = "G", Favorito = true, CapaArquivo = "g.jpg" };
            ContabilizadorDeTempo.Contabilizar(favorito, TimeSpan.FromMinutes(10), agora);
            v.Verificar("contabilizar não mexe em favorito nem capa",
                favorito.Favorito && favorito.CapaArquivo == "g.jpg");
        }

        // ---- Migração de schema ----------------------------------------------------------------

        /// <summary>
        /// Biblioteca gravada antes da troca só tem "minutosJogados". Ler isso como zero
        /// apagaria o histórico de quem já usava o launcher — a migração acontece na
        /// leitura, sem passo manual.
        /// </summary>
        private static void TestarMigracaoDeMinutosParaSegundos(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Migração da biblioteca v1 (minutos) para v2 (segundos)");

            var sandbox = PrepararSandbox(raizReal);
            var arquivo = Path.Combine(sandbox, "biblioteca-v1.json");

            File.WriteAllText(arquivo, @"{
  ""versao"": 1,
  ""pastasEscaneadas"": [""Jogos""],
  ""jogos"": [
    {
      ""id"": ""nfsmw-black"",
      ""titulo"": ""Need for Speed: Most Wanted (Black Edition)"",
      ""executavelRelativo"": ""Jogos\\NFS\\speed.exe"",
      ""minutosJogados"": 137,
      ""favorito"": true
    }
  ]
}");

            var antiga = Biblioteca.Carregar(arquivo);
            v.Verificar("biblioteca v1 continua sendo lida", antiga.Jogos.Count == 1, antiga.Jogos.Count.ToString());

            var jogo = antiga.Jogos[0];
            v.Verificar("137 minutos viraram 8220 segundos", jogo.SegundosJogados == 8220,
                jogo.SegundosJogados.ToString());
            v.VerificarTexto("e continuam aparecendo como 2 h 17 min", "2 h 17 min jogados",
                TempoDeJogo.Descrever(jogo.SegundosJogados));
            v.Verificar("o resto do jogo veio junto", jogo.Favorito && jogo.Titulo.Length > 0);

            // Regravar sobe a versão e passa a usar o campo novo.
            var regravado = Path.Combine(sandbox, "biblioteca-v2.json");
            antiga.Versao = Biblioteca.VersaoAtual;
            antiga.Salvar(regravado);

            var texto = File.ReadAllText(regravado);
            v.Verificar("o arquivo novo grava segundosJogados", texto.Contains("segundosJogados"));
            v.Verificar("e não grava mais minutosJogados", !texto.Contains("minutosJogados"));
            v.Verificar("versão marcada como 2", texto.Contains("\"versao\": 2"), Biblioteca.VersaoAtual.ToString());

            var relido = Biblioteca.Carregar(regravado);
            v.Verificar("ida e volta preserva o tempo", relido.Jogos[0].SegundosJogados == 8220,
                relido.Jogos[0].SegundosJogados.ToString());
        }

        // ---- Trava de lançamento duplo ---------------------------------------------------------

        /// <summary>
        /// Dois Enter rápidos no mesmo card abriam duas instâncias — e dois processos do
        /// mesmo jogo antigo escrevendo na mesma pasta SAVE\ dão save corrompido.
        /// </summary>
        private static void TestarTravaDeLancamentoDuplo(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Trava contra lançamento duplo");

            var sandbox = PrepararSandbox(raizReal);
            var pasta = Path.Combine(sandbox, "Jogos", "trava");
            Directory.CreateDirectory(pasta);

            var origem = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(origem) || !File.Exists(origem))
            {
                v.Escrever("  (sem executável de origem para copiar — teste pulado)");
                return;
            }

            var destino = Path.Combine(pasta, "jogo.exe");
            File.Copy(origem!, destino, overwrite: true);

            var jogo = new Jogo
            {
                Id = "trava",
                Titulo = "Jogo travado",
                ExecutavelRelativo = Caminhos.ParaRelativo(destino),
                Argumentos = "--dormir 6"
            };

            using (var lancador = new LancadorDeJogos())
            using (var terminou = new ManualResetEventSlim(false))
            {
                lancador.SessaoTerminada += (_, _) => terminou.Set();
                lancador.Lancar(jogo);

                var segundoLancamento = false;
                try
                {
                    lancador.Lancar(jogo);
                    segundoLancamento = true;
                }
                catch (InvalidOperationException)
                {
                    // Era para dar isto mesmo.
                }

                v.Verificar("o segundo lançamento é recusado enquanto o jogo roda", !segundoLancamento);
                v.Verificar("e o jogo em execução é identificável (para travar o card)",
                    lancador.JogoAtual?.Id == "trava", lancador.JogoAtual?.Id);

                terminou.Wait(TimeSpan.FromSeconds(30));

                v.Verificar("depois que encerra, o card destrava", !lancador.JogoRodando);
            }
        }

        // ---- Ciclo completo com processo real ------------------------------------------------

        /// <summary>
        /// O teste que não dá para simular: abre um processo de verdade, espera o evento
        /// de saída e confere que ele chegou por Process.Exited, não por espera ativa.
        /// </summary>
        private static void TestarCicloComProcessoReal(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Ciclo completo com processo de verdade");

            var sandbox = PrepararSandbox(raizReal);
            var pasta = Path.Combine(sandbox, "Jogos", "simulado");
            Directory.CreateDirectory(pasta);

            var origem = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(origem) || !File.Exists(origem))
            {
                v.Escrever("  (sem executável de origem para copiar — teste pulado)");
                return;
            }

            var destino = Path.Combine(pasta, "jogo.exe");
            File.Copy(origem!, destino, overwrite: true);

            // Jogo curto: sai em 1 s, abaixo do limite dos 5 s.
            ExercitarSessao(v, destino, segundos: 1, esperaContar: false);

            // Jogo "de verdade": passa dos 5 s.
            ExercitarSessao(v, destino, segundos: 6, esperaContar: true);
        }

        private static void ExercitarSessao(Verificador v, string executavel, int segundos, bool esperaContar)
        {
            var jogo = new Jogo
            {
                Id = $"simulado-{segundos}",
                Titulo = $"Simulado de {segundos}s",
                ExecutavelRelativo = Caminhos.ParaRelativo(executavel),
                Argumentos = $"--dormir {segundos}"
            };

            using (var lancador = new LancadorDeJogos())
            using (var terminou = new ManualResetEventSlim(false))
            {
                SessaoTerminadaEventArgs? sessao = null;

                lancador.SessaoTerminada += (_, e) => { sessao = e; terminou.Set(); };
                lancador.Lancar(jogo);

                v.Verificar($"[{segundos}s] o launcher sabe que tem jogo rodando", lancador.JogoRodando);

                var chegou = terminou.Wait(TimeSpan.FromSeconds(segundos + 20));

                v.Verificar($"[{segundos}s] o fim do jogo chegou pelo evento Process.Exited", chegou);
                if (!chegou || sessao is null) return;

                v.Verificar($"[{segundos}s] o launcher voltou a ficar livre", !lancador.JogoRodando);

                var duracao = sessao.Duracao;
                v.Verificar($"[{segundos}s] a duração medida bate com o jogo",
                    duracao >= TimeSpan.FromSeconds(segundos - 1), duracao.TotalSeconds.ToString("F1"));

                var resultado = ContabilizadorDeTempo.Contabilizar(jogo, duracao, DateTime.UtcNow);

                v.Verificar(esperaContar
                        ? $"[{segundos}s] a sessão conta (passou dos 5 s)"
                        : $"[{segundos}s] a sessão não conta (processo-filho assumiu)",
                    resultado.ContouTempo == esperaContar,
                    $"contou={resultado.ContouTempo}");
            }
        }

        // ---- Adoção do processo-filho ----------------------------------------------------------

        /// <summary>
        /// O caso do jogo com launcher próprio: o exe que eu lancei morre em 1 s e o jogo
        /// de verdade sobe em outro processo. O launcher NÃO pode reaparecer aí — seria
        /// uma janela por cima de um jogo em tela cheia. Tem que achar o filho, se prender
        /// nele e continuar escondido.
        /// </summary>
        private static void TestarAdocaoDeProcessoFilho(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Jogo com launcher próprio (adoção do processo-filho)");

            var sandbox = PrepararSandbox(raizReal);
            var pasta = Path.Combine(sandbox, "Jogos", "com-launcher-proprio");
            Directory.CreateDirectory(pasta);

            var origem = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(origem) || !File.Exists(origem))
            {
                v.Escrever("  (sem executável de origem para copiar — teste pulado)");
                return;
            }

            var destino = Path.Combine(pasta, "jogo.exe");
            File.Copy(origem!, destino, overwrite: true);

            var jogo = new Jogo
            {
                Id = "com-launcher",
                Titulo = "Jogo com launcher próprio",
                ExecutavelRelativo = Caminhos.ParaRelativo(destino),
                Argumentos = "--simular-launcher-proprio 9"
            };

            using (var lancador = new LancadorDeJogos())
            using (var adotou = new ManualResetEventSlim(false))
            using (var terminou = new ManualResetEventSlim(false))
            {
                SessaoTerminadaEventArgs? sessao = null;

                lancador.ProcessoFilhoAdotado += (_, _) => adotou.Set();
                lancador.SessaoTerminada += (_, e) => { sessao = e; terminou.Set(); };

                lancador.Lancar(jogo);

                // O pai morre quase imediatamente; a busca acontece 3 s depois.
                var achou = adotou.Wait(TimeSpan.FromSeconds(25));
                v.Verificar("achou o processo que o launcher próprio deixou rodando", achou);

                v.Verificar("e a sessão continua aberta (a janela segue escondida)",
                    lancador.JogoRodando);

                v.Verificar("nenhum fim de sessão foi anunciado enquanto o jogo roda",
                    !terminou.IsSet);

                var fim = terminou.Wait(TimeSpan.FromSeconds(40));
                v.Verificar("o fim chega quando o processo-filho encerra", fim);

                if (!fim || sessao is null) return;

                v.Verificar("a duração conta desde o lançamento original",
                    sessao.Duracao >= TimeSpan.FromSeconds(8), sessao.Duracao.TotalSeconds.ToString("F1"));

                var resultado = ContabilizadorDeTempo.Contabilizar(jogo, sessao.Duracao, DateTime.UtcNow);
                v.Verificar("e o tempo é contabilizado (não é mais saída rápida)",
                    resultado.ContouTempo && resultado.SegundosCreditados >= 8,
                    resultado.SegundosCreditados.ToString());

                v.Verificar("terminou tudo, o card destrava", !lancador.JogoRodando);
            }
        }

        // ---- Apoio ---------------------------------------------------------------------------

        private static string PrepararSandbox(string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);

            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();

            return sandbox;
        }

        private static void LimparSandbox(Verificador v, string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox);

            // O exe copiado pode ainda estar preso pelo processo que acabou de sair.
            for (var tentativa = 0; tentativa < 3; tentativa++)
            {
                try
                {
                    if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
                    return;
                }
                catch (Exception ex)
                {
                    if (tentativa == 2) v.Escrever($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
                    else Thread.Sleep(300);
                }
            }
        }
    }
}
