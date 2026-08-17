using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Mochila.Util;

namespace Mochila.Scanner
{
    /// <summary>
    /// O sistema de pontuação: dado o conjunto de executáveis de uma pasta de jogo,
    /// decide qual é o executável principal.
    ///
    /// Cada pasta é avaliada isoladamente. Pastas irmãs com estrutura idêntica são a
    /// regra num HD de jogos antigos (a mesma engine repete server.dll, dinput8.dll,
    /// filelist.txt em todas), então nada aqui pode deduzir o executável de uma pasta
    /// a partir da vizinha.
    /// </summary>
    public static class Pontuador
    {
        /// <summary>Placar mínimo para a linha não ser marcada como baixa confiança na revisão.</summary>
        public const int PlacarDeConfianca = 30;

        private const long UmMegabyte = 1024 * 1024;
        private const long CemKilobytes = 100 * 1024;

        /// <summary>Teto do bônus de tokens: 3 tokens em comum já provam o parentesco.</summary>
        private const int MaximoDeTokensPontuados = 3;

        /// <summary>
        /// Quantos executáveis por pasta têm o cabeçalho PE lido.
        ///
        /// Medido no HD externo USB com cache frio: cada abertura de .exe custa ~250 ms
        /// (o antivírus varre o arquivo a cada open). Com 100 jogos de 3 exes, ler todos
        /// dava 75 s de scan — 99% do tempo total. Lendo só os 3 primeiros colocados, o
        /// sinal continua chegando em quem disputa a vitória e o custo cai junto.
        ///
        /// Risco assumido: um 4º colocado com PE de GUI (+10) poderia, na teoria,
        /// ultrapassar um dos três se todos eles fossem punidos. Como os três de cima
        /// sempre são lidos, o vencedor nunca escapa da checagem de requireAdministrator.
        /// </summary>
        public const int MaximoDeLeiturasDePePorPasta = 3;

        /// <summary>Maior ganho que o PE pode dar: GUI (+10) e metadados parecidos (+15).</summary>
        private const int MaiorGanhoDoPe = 10 + 15;

        /// <summary>Maior perda: console (-20) e requireAdministrator (-50).</summary>
        private const int MaiorPerdaDoPe = 20 + 50;

        /// <summary>
        /// Diferença de placar acima da qual o PE não tem como inverter a ordem entre dois
        /// candidatos. Se o segundo colocado está mais longe que isso, ler o cabeçalho não
        /// mudaria nada — e no HD externo cada leitura custa ~240 ms.
        /// </summary>
        private const int JanelaDeInfluenciaDoPe = MaiorGanhoDoPe + MaiorPerdaDoPe;

        private const RegexOptions OpcoesRegex =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        // ---- Regras por nome do arquivo (sem extensão) --------------------------------------

        // Todos os padrões abaixo rodam contra o nome COMPACTADO (tokens colados,
        // minúsculos, sem acento e sem separador). É o que faz "NeedForSpeed_Setup",
        // "needforspeed-setup" e "NEEDFORSPEEDSETUP" caírem na mesma regra: os três
        // viram "needforspeedsetup" e batem em "setup$".

        private sealed class RegraDeNome
        {
            public RegraDeNome(RegraPlacar regra, string padrao, int pontos, string explicacao)
            {
                Regra = regra;
                Padrao = new Regex(padrao, OpcoesRegex);
                Pontos = pontos;
                Explicacao = explicacao;
            }

            public RegraPlacar Regra { get; }
            public Regex Padrao { get; }
            public int Pontos { get; }
            public string Explicacao { get; }
        }

        /// <summary>
        /// VETO, não penalidade: quem cai aqui sai da disputa e nenhum bônus o resgata.
        ///
        /// Antes isto era -60, e a similaridade de tokens (até +75) conseguia superar.
        /// Num HD de repacks o instalador quase sempre carrega o nome do jogo
        /// ("NeedForSpeed_Setup.exe"), então ele ganhava do executável de verdade.
        /// </summary>
        private static readonly RegraDeNome[] RegrasDeVeto =
        {
            new RegraDeNome(RegraPlacar.NomeDeRedistribuivel,
                @"vcredist|dxsetup|dxwebsetup|oalinst|dotnetfx|directx|redist", 0, "redistribuível"),

            new RegraDeNome(RegraPlacar.NomeDeInstalador,
                @"^unins|uninstall|^setup|setup$|^install|install$|installer|inst$", 0,
                "instalador/desinstalador"),

            new RegraDeNome(RegraPlacar.NomeDeRelatorioDeErro,
                @"crash(handler|report|reporter|sender|dump|pad)|errorreport|bugreport", 0,
                "relatório de erro")
        };

        /// <summary>Penalidades que continuam sendo só pontos — dá para o bônus superar.</summary>
        private static readonly RegraDeNome[] RegrasDePenalidade =
        {
            // "^safemode" não está na tabela da spec, mas ela cita SafeMode.bat como algo
            // que precisa continuar descartado entre os candidatos de último recurso.
            new RegraDeNome(RegraPlacar.NomeDeFerramenta,
                @"benchmark|^config|settings|activate|keygen|patch|^safemode", -30, "ferramenta auxiliar")
        };

        /// <summary>
        /// Nomes de ponto de entrada indireto que PODEM ser o jeito certo de abrir o jogo:
        /// launcher do GOG, launcher de configuração do RPG Maker antigo, bootstrapper que
        /// prepara o DirectX antes de subir o jogo.
        /// </summary>
        private static readonly Regex NomeDeLancador =
            new Regex(@"launcher|^launch|client|bootstrap|^start|^play", OpcoesRegex);

        /// <summary>Updater entra na regra de redundância, mas nunca é promovido a ponto de entrada.</summary>
        private static readonly Regex NomeDeUpdater = new Regex(@"updater|^update", OpcoesRegex);

        // ---- Pastas ---------------------------------------------------------------------------

        /// <summary>
        /// Segmentos de pasta que quase nunca guardam o jogo. Também são os pontos onde
        /// o scanner para de descer.
        /// </summary>
        private static readonly string[] SegmentosIrrelevantes =
        {
            "uninstall", "support", "redist", "_commonredist", "directx",
            "dotnet", "tools", "bonus", "extras", "soundtrack", "docs"
        };

        /// <summary>Onde binário de jogo costuma morar de verdade.</summary>
        private static readonly string[][] PastasDeBinarios =
        {
            new[] { "bin" },
            new[] { "binaries", "win64" },
            new[] { "binaries", "win32" },
            new[] { "game" },
            new[] { "x64" }
        };

        /// <summary>
        /// Motivo do veto, ou null se o arquivo está liberado para disputar.
        ///
        /// Instalador, redistribuível e relatório de erro não são jogo em hipótese nenhuma,
        /// então o scanner também usa isso para decidir estrutura de pastas — um
        /// "dxwebsetup.exe" jogado na raiz de uma prateleira (@"Jogos\Antigos\") não pode
        /// fazer a prateleira inteira virar um jogo só.
        /// </summary>
        public static string? MotivoDeVeto(string? nomeDeArquivo)
        {
            if (string.IsNullOrEmpty(nomeDeArquivo)) return null;

            var compactado = Tokens.Compactar(Path.GetFileNameWithoutExtension(nomeDeArquivo));
            if (compactado.Length == 0) return null;

            foreach (var regra in RegrasDeVeto)
            {
                if (regra.Padrao.IsMatch(compactado)) return regra.Explicacao;
            }
            return null;
        }

        /// <summary>Atalho de leitura para <see cref="MotivoDeVeto"/>.</summary>
        public static bool NomeEhDescartavel(string? nomeDeArquivo) => MotivoDeVeto(nomeDeArquivo) != null;

        /// <summary>true se algum segmento do caminho for pasta de lixo (redist, docs, tools...).</summary>
        public static bool SegmentoEhIrrelevante(string? segmento)
        {
            if (string.IsNullOrEmpty(segmento)) return false;

            var normalizado = segmento!.Trim().ToLowerInvariant();
            foreach (var irrelevante in SegmentosIrrelevantes)
            {
                if (normalizado == irrelevante) return true;
            }

            // Pega variações reais: "_CommonRedist", "DirectX Redist", "Redist_x64".
            return normalizado.IndexOf("redist", StringComparison.Ordinal) >= 0;
        }

        // ---- Pontuação -------------------------------------------------------------------------

        /// <summary>
        /// Pontua todos os candidatos de uma pasta de jogo e devolve a lista ordenada
        /// do melhor para o pior.
        /// </summary>
        public static List<CandidatoExecutavel> Avaliar(
            IReadOnlyList<CandidatoExecutavel> candidatos,
            string nomeDaPastaDoJogo,
            ISistemaDeArquivos sistemaDeArquivos)
        {
            var tokensDaPasta = Tokens.Significativos(nomeDaPastaDoJogo);
            var pastaCompactada = Tokens.Compactar(nomeDaPastaDoJogo);

            // "Maior .exe do conjunto" — .bat/.lnk de último recurso não disputam por tamanho,
            // e o vetado também não: instalador de repack tem gigabytes e roubaria o +40
            // do executável de verdade.
            long maiorTamanhoDeExe = 0;
            foreach (var candidato in candidatos)
            {
                if (candidato.EhExe && !NomeEhDescartavel(candidato.Caminho) && candidato.Tamanho > maiorTamanhoDeExe)
                    maiorTamanhoDeExe = candidato.Tamanho;
            }

            // Passo 1: tudo que sai de graça — nome, pasta, tamanho e similaridade.
            foreach (var candidato in candidatos)
                Pontuar(candidato, tokensDaPasta, pastaCompactada, maiorTamanhoDeExe);

            // Depende de conhecer os vizinhos, então roda depois de todo mundo pontuado.
            AplicarRegraDeLancador(candidatos);

            var ordenados = new List<CandidatoExecutavel>(candidatos);
            ordenados.Sort(CompararPorPlacar);

            // Passo 2: só agora o disco é tocado, e só para quem ainda disputa a vitória.
            LerPeDosPrimeiros(ordenados, tokensDaPasta, sistemaDeArquivos);
            ordenados.Sort(CompararPorPlacar);

            return ordenados;
        }

        /// <summary>
        /// Lê o PE só de quem ainda pode mudar o resultado.
        ///
        /// Três filtros, em ordem de economia:
        ///  1. candidato excluído nunca é aberto — está fora da disputa de qualquer jeito,
        ///     e num acervo real a maioria dos .exe de uma pasta é instalador;
        ///  2. pasta com um só candidato não abre nada: não há o que decidir;
        ///  3. se o segundo colocado está fora da janela de influência, o PE não inverte
        ///     mais nada e ninguém é aberto.
        /// Sobrando disputa de verdade, lê no máximo os três primeiros.
        /// </summary>
        private static void LerPeDosPrimeiros(List<CandidatoExecutavel> ordenados,
                                              HashSet<string> tokensDaPasta,
                                              ISistemaDeArquivos sistemaDeArquivos)
        {
            var disputa = new List<CandidatoExecutavel>();
            foreach (var candidato in ordenados)
            {
                if (!candidato.Excluido && candidato.EhExe) disputa.Add(candidato);
            }

            if (disputa.Count < 2) return;
            if (disputa[0].Placar - disputa[1].Placar > JanelaDeInfluenciaDoPe) return;

            var placarDoLider = disputa[0].Placar;

            for (var i = 0; i < disputa.Count && i < MaximoDeLeiturasDePePorPasta; i++)
            {
                if (placarDoLider - disputa[i].Placar > JanelaDeInfluenciaDoPe) return;

                PontuarPe(disputa[i], sistemaDeArquivos, tokensDaPasta);
            }
        }

        /// <summary>
        /// Ordem: excluídos sempre por último, promovidos sempre primeiro, e no meio
        /// placar desc, tamanho desc, menos profundo, nome. Os desempates existem para o
        /// resultado ser estável — sem eles, a ordem de listagem do sistema de arquivos
        /// mudaria a escolha entre dois empatados.
        /// </summary>
        private static int CompararPorPlacar(CandidatoExecutavel a, CandidatoExecutavel b)
        {
            if (a.Excluido != b.Excluido) return a.Excluido ? 1 : -1;
            if (a.Promovido != b.Promovido) return a.Promovido ? -1 : 1;

            var porPlacar = b.Placar.CompareTo(a.Placar);
            if (porPlacar != 0) return porPlacar;

            var porTamanho = b.Tamanho.CompareTo(a.Tamanho);
            if (porTamanho != 0) return porTamanho;

            var porProfundidade = Profundidade(a.CaminhoRelativoAoJogo).CompareTo(Profundidade(b.CaminhoRelativoAoJogo));
            if (porProfundidade != 0) return porProfundidade;

            return string.Compare(a.CaminhoRelativoAoJogo, b.CaminhoRelativoAoJogo, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Sinais que não custam disco. O PE fica para depois, na segunda passada.</summary>
        private static void Pontuar(CandidatoExecutavel candidato, HashSet<string> tokensDaPasta,
                                    string pastaCompactada, long maiorTamanhoDeExe)
        {
            var nomeSemExtensao = Path.GetFileNameWithoutExtension(candidato.Caminho) ?? "";

            PontuarNome(candidato, nomeSemExtensao);
            PontuarPasta(candidato);
            PontuarTamanho(candidato, maiorTamanhoDeExe);
            PontuarSimilaridade(candidato, nomeSemExtensao, tokensDaPasta, pastaCompactada);
        }

        private static void PontuarNome(CandidatoExecutavel candidato, string nomeSemExtensao)
        {
            var motivoDeVeto = MotivoDeVeto(nomeSemExtensao);
            if (motivoDeVeto != null) candidato.Excluir(motivoDeVeto);

            var compactado = Tokens.Compactar(nomeSemExtensao);

            foreach (var regra in RegrasDePenalidade)
            {
                if (regra.Padrao.IsMatch(compactado))
                    candidato.Somar(regra.Regra, regra.Pontos, regra.Explicacao);
            }
        }

        /// <summary>
        /// A regra de launcher/updater é de contexto, não de nome.
        ///
        /// Penalizar cego dava resultado errado em coisa que eu tenho de verdade: jogo GOG
        /// com Mochila.exe, RPG Maker antigo com launcher de configuração, bootstrapper
        /// que prepara o DirectX. Nesses, o launcher É o ponto de entrada.
        ///
        /// Só é redundante quando existe executável de jogo na MESMA pasta
        /// (game.exe + game_launcher.exe). Se o executável do jogo está em subpasta e o
        /// launcher está sozinho na raiz, ele é promovido a escolhido.
        /// </summary>
        private static void AplicarRegraDeLancador(IReadOnlyList<CandidatoExecutavel> candidatos)
        {
            // Promover exige que a escolha seja óbvia: um único launcher na raiz. Com dois
            // ou mais (Mochila.exe + Start.exe), não dá para saber qual é o certo — vale
            // a pontuação normal e a revisão manual decide.
            var promocaoPermitida = ContarLancadoresNaRaiz(candidatos) == 1;

            foreach (var candidato in candidatos)
            {
                if (candidato.Excluido || !EhAuxiliarDeLancamento(candidato)) continue;

                if (TemJogoNaMesmaPasta(candidatos, candidato))
                {
                    candidato.Somar(RegraPlacar.NomeDeLauncherOuUpdater, -15,
                        "launcher redundante: há executável do jogo na mesma pasta");
                    continue;
                }

                // Não é redundante: nenhuma penalidade. E se o executável do jogo estiver
                // mesmo numa subpasta, este é o ponto de entrada.
                if (promocaoPermitida &&
                    EhLancadorPromovivel(candidato) &&
                    Profundidade(candidato.CaminhoRelativoAoJogo) == 0 &&
                    TemCandidatoEmSubpasta(candidatos))
                {
                    candidato.Somar(RegraPlacar.PontoDeEntradaEmSubpasta, +20,
                        "launcher na raiz com o executável do jogo em subpasta");
                    candidato.Promover("ponto de entrada do jogo");
                }
            }
        }

        /// <summary>Quantos launchers promovíveis estão na raiz da pasta do jogo.</summary>
        private static int ContarLancadoresNaRaiz(IReadOnlyList<CandidatoExecutavel> candidatos)
        {
            var quantidade = 0;

            foreach (var candidato in candidatos)
            {
                if (candidato.Excluido || !candidato.EhExe) continue;
                if (Profundidade(candidato.CaminhoRelativoAoJogo) != 0) continue;
                if (EhLancadorPromovivel(candidato)) quantidade++;
            }
            return quantidade;
        }

        private static bool EhLancadorPromovivel(CandidatoExecutavel candidato)
            => NomeDeLancador.IsMatch(Compactar(candidato));

        private static bool EhAuxiliarDeLancamento(CandidatoExecutavel candidato)
        {
            var compactado = Compactar(candidato);
            return NomeDeLancador.IsMatch(compactado) || NomeDeUpdater.IsMatch(compactado);
        }

        private static string Compactar(CandidatoExecutavel candidato)
            => Tokens.Compactar(Path.GetFileNameWithoutExtension(candidato.Caminho));

        /// <summary>Existe outro .exe na mesma pasta que não seja launcher nem esteja vetado?</summary>
        private static bool TemJogoNaMesmaPasta(IReadOnlyList<CandidatoExecutavel> candidatos,
                                                CandidatoExecutavel launcher)
        {
            var pastaDoLauncher = Path.GetDirectoryName(launcher.CaminhoRelativoAoJogo) ?? "";

            foreach (var outro in candidatos)
            {
                if (ReferenceEquals(outro, launcher) || outro.Excluido || !outro.EhExe) continue;
                if (EhAuxiliarDeLancamento(outro)) continue;

                var pastaDoOutro = Path.GetDirectoryName(outro.CaminhoRelativoAoJogo) ?? "";
                if (string.Equals(pastaDoOutro, pastaDoLauncher, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool TemCandidatoEmSubpasta(IReadOnlyList<CandidatoExecutavel> candidatos)
        {
            foreach (var candidato in candidatos)
            {
                if (candidato.Excluido || !candidato.EhExe) continue;
                if (Profundidade(candidato.CaminhoRelativoAoJogo) > 0) return true;
            }
            return false;
        }

        private static void PontuarPasta(CandidatoExecutavel candidato)
        {
            var segmentos = SegmentosDaPasta(candidato.CaminhoRelativoAoJogo);

            foreach (var segmento in segmentos)
            {
                if (SegmentoEhIrrelevante(segmento))
                {
                    candidato.Somar(RegraPlacar.PastaIrrelevante, -80, $"está dentro de \"{segmento}\"");
                    break;   // uma vez basta; -80 já tira o candidato do páreo
                }
            }

            if (segmentos.Count == 0)
            {
                candidato.Somar(RegraPlacar.NaRaizDaPasta, +15, "na raiz da pasta do jogo");
                return;
            }

            var nivelDeBinarios = CasarPastaDeBinarios(segmentos);
            if (nivelDeBinarios > 0)
            {
                candidato.Somar(RegraPlacar.PastaDeBinarios, +12, "em pasta de binários conhecida");

                var extras = segmentos.Count - nivelDeBinarios;
                if (extras > 0)
                {
                    candidato.Somar(RegraPlacar.ProfundidadeExtra, -5 * extras,
                        $"{extras.ToString(CultureInfo.InvariantCulture)} nível(is) além da pasta de binários");
                }
                return;
            }

            candidato.Somar(RegraPlacar.ProfundidadeExtra, -5 * segmentos.Count,
                $"{segmentos.Count.ToString(CultureInfo.InvariantCulture)} nível(is) abaixo da raiz do jogo");
        }

        private static void PontuarTamanho(CandidatoExecutavel candidato, long maiorTamanhoDeExe)
        {
            // .bat/.cmd/.lnk são minúsculos por natureza — cobrar tamanho deles não diz nada.
            if (!candidato.EhExe)
            {
                candidato.Somar(RegraPlacar.NaoEhExecutavel, -10, "não é .exe (candidato de último recurso)");
                return;
            }

            if (maiorTamanhoDeExe > 0 && candidato.Tamanho == maiorTamanhoDeExe)
                candidato.Somar(RegraPlacar.MaiorExecutavel, +40, "é o maior .exe da pasta");

            if (candidato.Tamanho >= UmMegabyte)
                candidato.Somar(RegraPlacar.TamanhoRazoavel, +10, "tem 1 MB ou mais");
            else if (candidato.Tamanho < CemKilobytes)
                candidato.Somar(RegraPlacar.TamanhoMinusculo, -15, "tem menos de 100 KB");
        }

        private static void PontuarSimilaridade(CandidatoExecutavel candidato, string nomeSemExtensao,
                                                HashSet<string> tokensDaPasta, string pastaCompactada)
        {
            var tokensDoExe = Tokens.Significativos(nomeSemExtensao);

            var emComum = new List<string>();
            foreach (var token in tokensDoExe)
            {
                if (tokensDaPasta.Contains(token)) emComum.Add(token);
            }

            if (emComum.Count > 0)
            {
                var pontuados = Math.Min(emComum.Count, MaximoDeTokensPontuados);
                emComum.Sort(StringComparer.Ordinal);
                candidato.Somar(RegraPlacar.TokensEmComum, 25 * pontuados,
                    $"token(s) em comum com o nome da pasta: {string.Join(", ", emComum.ToArray())}");
            }

            var exeCompactado = Tokens.Compactar(nomeSemExtensao);
            if (exeCompactado.Length == 0) return;

            if (string.Equals(exeCompactado, pastaCompactada, StringComparison.Ordinal))
            {
                candidato.Somar(RegraPlacar.NomeIgualAoDaPasta, +35, "nome igual ao da pasta");
                return;
            }

            // Bônus fraco de sigla: "nfsc" ⊂ "nfscarbon". Serve para desempatar, não para decidir.
            if (exeCompactado.Length >= 2 && Tokens.EhSubsequencia(exeCompactado, pastaCompactada))
                candidato.Somar(RegraPlacar.SiglaDoNomeDaPasta, +10, "nome é sigla do nome da pasta");
        }

        private static void PontuarPe(CandidatoExecutavel candidato, ISistemaDeArquivos sistemaDeArquivos,
                                      HashSet<string> tokensDaPasta)
        {
            if (!candidato.EhExe) return;

            var info = sistemaDeArquivos.LerInfoExecutavel(candidato.Caminho);
            if (info is null) return;      // sem PE legível, o resto do placar decide sozinho

            if (info.Subsistema == SubsistemaPe.Gui)
                candidato.Somar(RegraPlacar.SubsistemaGui, +10, "PE de interface gráfica");
            else if (info.Subsistema == SubsistemaPe.Console)
                candidato.Somar(RegraPlacar.SubsistemaConsole, -20, "PE de console");

            if (info.PedeAdministrador)
                candidato.Somar(RegraPlacar.PedeAdministrador, -50, "manifesto pede requireAdministrator");

            if (MetadadoParecido(info.FileDescription, tokensDaPasta) ||
                MetadadoParecido(info.ProductName, tokensDaPasta))
            {
                candidato.Somar(RegraPlacar.MetadadosParecidos, +15, "metadados do PE batem com o nome da pasta");
            }
        }

        private static bool MetadadoParecido(string? metadado, HashSet<string> tokensDaPasta)
        {
            if (string.IsNullOrWhiteSpace(metadado) || tokensDaPasta.Count == 0) return false;

            foreach (var token in Tokens.Significativos(metadado))
            {
                if (tokensDaPasta.Contains(token)) return true;
            }
            return false;
        }

        // ---- Apoio -------------------------------------------------------------------------------

        /// <summary>Segmentos de pasta entre a pasta do jogo e o arquivo (vazio = está na raiz).</summary>
        private static List<string> SegmentosDaPasta(string caminhoRelativoAoJogo)
        {
            var partes = caminhoRelativoAoJogo.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var segmentos = new List<string>(Math.Max(0, partes.Length - 1));

            for (var i = 0; i < partes.Length - 1; i++) segmentos.Add(partes[i]);
            return segmentos;
        }

        private static int Profundidade(string caminhoRelativoAoJogo)
            => SegmentosDaPasta(caminhoRelativoAoJogo).Count;

        /// <summary>
        /// Quantos segmentos iniciais casam com uma pasta de binários conhecida
        /// (0 = não casou). Ex.: @"Binaries\Win64\x\y.exe" casa 2 segmentos.
        /// </summary>
        private static int CasarPastaDeBinarios(List<string> segmentos)
        {
            var melhor = 0;

            foreach (var padrao in PastasDeBinarios)
            {
                if (padrao.Length > segmentos.Count) continue;

                var casou = true;
                for (var i = 0; i < padrao.Length; i++)
                {
                    if (!string.Equals(segmentos[i], padrao[i], StringComparison.OrdinalIgnoreCase))
                    {
                        casou = false;
                        break;
                    }
                }

                if (casou && padrao.Length > melhor) melhor = padrao.Length;
            }

            return melhor;
        }
    }
}
