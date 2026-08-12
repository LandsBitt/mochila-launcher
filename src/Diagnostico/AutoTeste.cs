using System;
using System.IO;
using System.Text.RegularExpressions;
using Launcher.Dados;
using Launcher.Modelo;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 1: caminhos relativos e ida-e-volta do JSON.
    ///
    /// Roda com "Launcher.exe --autoteste". Trabalha dentro de uma pasta descartável
    /// criada ao lado do executável (nunca escreve fora da pasta do launcher) e apaga
    /// tudo no fim.
    /// </summary>
    public static class AutoTeste
    {
        private const string NomePastaSandbox = "_autoteste-tmp";

        /// <summary>Detecta qualquer letra de drive num texto ("D:\", "c:/"). O JSON não pode ter isso.</summary>
        private static readonly Regex LetraDeDrive = new Regex(@"[A-Za-z]:[\\/]", RegexOptions.Compiled);

        private static int _passou;
        private static int _falhou;
        private static Action<string> _saida = _ => { };

        /// <summary>Executa tudo. Devolve true se todos os testes passaram.</summary>
        public static bool Executar(Action<string> saida)
        {
            _saida = saida;
            _passou = 0;
            _falhou = 0;

            var raizReal = Caminhos.PastaBase;
            var sandboxA = Path.Combine(raizReal, NomePastaSandbox, "hd-como-E");
            var sandboxB = Path.Combine(raizReal, NomePastaSandbox, "hd-como-F");

            try
            {
                LimparSandbox(raizReal);
                Directory.CreateDirectory(sandboxA);
                Caminhos.DefinirPastaBase(sandboxA);

                TestarEstruturaDePastas();
                TestarValidacaoDeRelativos();
                TestarMigracaoDeCaminhoAbsoluto();
                TestarIdaEVoltaDeCaminho();
                TestarRecusaDeCaminhoForaDaRaiz();
                TestarEscritaDaBiblioteca();
                TestarLeituraDaBiblioteca();
                TestarRecusaDeSalvarCaminhoAbsoluto();
                TestarConfig();
                TestarGeracaoDeId();
                TestarTrocaDeLetraDeDrive(sandboxA, sandboxB);
            }
            catch (Exception ex)
            {
                _falhou++;
                _saida($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
                LimparSandbox(raizReal);
            }

            _saida("");
            _saida($"Resultado: {_passou} passou, {_falhou} falhou.");
            return _falhou == 0;
        }

        // ---- Testes ------------------------------------------------------------------------

        private static void TestarEstruturaDePastas()
        {
            Caminhos.GarantirEstrutura();
            Verificar(@"cria _launcher\", Directory.Exists(Caminhos.PastaEstado));
            Verificar(@"cria _launcher\capas\", Directory.Exists(Caminhos.PastaCapas));
            Verificar(@"cria _launcher\cache\", Directory.Exists(Caminhos.PastaCache));
        }

        private static void TestarValidacaoDeRelativos()
        {
            Verificar("aceita relativo simples", Caminhos.EhRelativoValido(@"Jogos\NFS\speed.exe"));
            Verificar("recusa letra de drive", !Caminhos.EhRelativoValido(@"D:\Jogos\speed.exe"));
            Verificar("recusa drive sem barra", !Caminhos.EhRelativoValido(@"D:Jogos\speed.exe"));
            Verificar("recusa caminho com raiz", !Caminhos.EhRelativoValido(@"\Jogos\speed.exe"));
            Verificar("recusa UNC", !Caminhos.EhRelativoValido(@"\\servidor\jogos\speed.exe"));
            Verificar("recusa vazio", !Caminhos.EhRelativoValido(""));

            // O launcher vive numa subpasta (D:\Launcher\) e os jogos são irmãos dela
            // (D:\Jogos\): "..\Jogos" é o caminho normal do acervo, não um ataque.
            Verificar(@"aceita subir para a pasta irmã (..\Jogos)",
                Caminhos.EhRelativoValido(@"..\Jogos\NFS\speed.exe"));
            Verificar("aceita .. no meio, desde que continue no HD",
                Caminhos.EhRelativoValido(@"Jogos\..\Jogos\speed.exe"));

            // O limite continua sendo o drive: fora dele, o caminho vira coisa da máquina.
            var subidasDemais = string.Concat(System.Linq.Enumerable.Repeat(@"..\", 30)) + @"Windows\notepad.exe";
            Verificar("recusa subir além da raiz do drive", !Caminhos.EhRelativoValido(subidasDemais));
            Verificar("recusa apontar para a própria raiz do drive",
                !Caminhos.EhRelativoValido(string.Concat(System.Linq.Enumerable.Repeat(@"..\", 30))));
        }

        /// <summary>
        /// Biblioteca gravada com caminho absoluto (versão antiga, ou eu editando o JSON
        /// na mão) volta a ser relativa sozinha na próxima gravação.
        /// </summary>
        private static void TestarMigracaoDeCaminhoAbsoluto()
        {
            var pastaDeJogos = Path.Combine(Caminhos.PastaBase, "Jogos", "NFS");
            var exeAbsoluto = Path.Combine(pastaDeJogos, "speed.exe");

            Verificar("caminho absoluto do próprio HD é migrado",
                Caminhos.ParaRelativoMigrando(exeAbsoluto) == @"Jogos\NFS\speed.exe",
                Caminhos.ParaRelativoMigrando(exeAbsoluto));

            Verificar("relativo já válido passa intacto",
                Caminhos.ParaRelativoMigrando(@"Jogos\NFS\speed.exe") == @"Jogos\NFS\speed.exe");

            Verificar("relativo com .. também passa intacto",
                Caminhos.ParaRelativoMigrando(@"..\Jogos\NFS\speed.exe") == @"..\Jogos\NFS\speed.exe");

            // Pasta irmã do launcher: o caso do layout recomendado.
            var irma = Path.GetFullPath(Path.Combine(Caminhos.PastaBase, "..", "Jogos"));
            var migrada = Caminhos.ParaRelativoMigrando(irma);
            Verificar("pasta irmã vira ..\\Jogos", migrada == @"..\Jogos", migrada);

            var json = new Biblioteca();
            json.PastasEscaneadas.Add("Jogos");
            json.Jogos.Add(new Jogo
            {
                Id = "nfs",
                Titulo = "NFS",
                ExecutavelRelativo = @"..\Jogos\NFS\speed.exe"
            });

            var arquivo = Path.Combine(Caminhos.PastaEstado, "migracao.json");
            var gravou = true;
            try
            {
                json.Salvar(arquivo);
            }
            catch (Exception)
            {
                gravou = false;
            }

            Verificar("biblioteca com ..\\ é gravável", gravou);

            if (gravou)
            {
                var conteudo = ArquivoTexto.Ler(arquivo);
                Verificar("e o arquivo continua sem letra de drive", !LetraDeDrive.IsMatch(conteudo));
            }
        }

        private static void TestarIdaEVoltaDeCaminho()
        {
            const string esperado = @"Jogos\Antigos\Carros\Need For Speed Most Wanted Black Edition\speed.exe";
            var absoluto = Path.Combine(Caminhos.PastaBase, esperado);

            var relativo = Caminhos.ParaRelativo(absoluto);

            Verificar("relativo sem letra de drive", !LetraDeDrive.IsMatch(relativo));
            Verificar("relativo com separador do Windows", relativo.IndexOf('/') < 0);
            Verificar("relativo sem escape de Uri (%20)", !relativo.Contains("%20"));
            Verificar("relativo esperado", relativo == esperado, relativo);

            var voltou = Caminhos.ParaAbsoluto(relativo);
            Verificar("volta para o absoluto original",
                string.Equals(voltou, Path.GetFullPath(absoluto), StringComparison.OrdinalIgnoreCase),
                voltou);
        }

        /// <summary>
        /// O limite da portabilidade é o DRIVE, não a pasta do launcher. Pasta irmã é o
        /// layout recomendado (D:\Launcher\ + D:\Jogos\); outro drive é que não pode.
        /// </summary>
        private static void TestarRecusaDeCaminhoForaDaRaiz()
        {
            var pai = Path.GetDirectoryName(Caminhos.PastaBase)!;
            var irma = Path.Combine(pai, "outra-pasta", "jogo.exe");

            Verificar("pasta irmã do launcher é aceita (é o layout recomendado)",
                Caminhos.TentarParaRelativo(irma, out var relativoDaIrma), relativoDaIrma);

            Verificar("e vira um relativo com ..",
                relativoDaIrma.StartsWith(@"..\", StringComparison.Ordinal), relativoDaIrma);

            Verificar("que resolve de volta para o mesmo lugar",
                string.Equals(Caminhos.ParaAbsoluto(relativoDaIrma), Path.GetFullPath(irma),
                              StringComparison.OrdinalIgnoreCase));

            // Outro drive: aí sim está fora do HD e quebraria no próximo PC.
            var raizAtual = Path.GetPathRoot(Caminhos.PastaBase) ?? @"D:\";
            var outraLetra = raizAtual.StartsWith("Q", StringComparison.OrdinalIgnoreCase) ? "R" : "Q";
            var outroDrive = $@"{outraLetra}:\Jogos\jogo.exe";

            Verificar("recusa converter caminho de outro drive",
                !Caminhos.TentarParaRelativo(outroDrive, out _));

            Verificar("ParaRelativo lança quando está em outro drive",
                Lanca<CaminhoForaDaRaizException>(() => Caminhos.ParaRelativo(outroDrive)));

            Verificar("recusa caminho de rede (UNC)",
                !Caminhos.TentarParaRelativo(@"\\servidor\jogos\jogo.exe", out _));
        }

        private static void TestarEscritaDaBiblioteca()
        {
            MontarBibliotecaDeExemplo().Salvar();

            Verificar("biblioteca.json foi criado", File.Exists(Caminhos.ArquivoBiblioteca));

            var conteudo = ArquivoTexto.Ler(Caminhos.ArquivoBiblioteca);

            Verificar("JSON gravado sem letra de drive", !LetraDeDrive.IsMatch(conteudo));
            // ".." é permitido no formato (o acervo costuma ser irmão do launcher), mas
            // esta biblioteca de exemplo é toda de subpasta: nenhum tem o que subir.
            Verificar("exemplo com jogos abaixo do launcher não gera subida", !conteudo.Contains(@"..\\"));
            // Preso à constante, não ao literal: schema novo não pode quebrar o teste
            // por um número datado.
            Verificar($"JSON tem a chave versao (={Biblioteca.VersaoAtual})",
                conteudo.Contains($"\"versao\": {Biblioteca.VersaoAtual}"));
            Verificar("JSON tem pastasEscaneadas", conteudo.Contains("\"pastasEscaneadas\""));
            Verificar("JSON indentado", conteudo.Contains("\r\n  \""));
            Verificar("separador escapado corretamente", conteudo.Contains(@"Jogos\\Antigos\\Carros"));
            Verificar("steamGridDbId nulo sai como null", conteudo.Contains("\"steamGridDbId\": null"));
        }

        private static void TestarLeituraDaBiblioteca()
        {
            var original = MontarBibliotecaDeExemplo();
            var lida = Biblioteca.Carregar();

            Verificar("leu a versão", lida.Versao == original.Versao);
            Verificar("leu as pastas escaneadas", lida.PastasEscaneadas.Count == original.PastasEscaneadas.Count);
            Verificar("leu a quantidade de jogos", lida.Jogos.Count == original.Jogos.Count, $"{lida.Jogos.Count}");

            var esperado = original.Jogos[0];
            var obtido = lida.ObterPorId(esperado.Id);
            Verificar("achou o jogo pelo id", obtido is not null);
            if (obtido is null) return;

            Verificar("título preservado", obtido.Titulo == esperado.Titulo, obtido.Titulo);
            Verificar("executável preservado", obtido.ExecutavelRelativo == esperado.ExecutavelRelativo, obtido.ExecutavelRelativo);
            Verificar("argumentos preservados", obtido.Argumentos == esperado.Argumentos);
            Verificar("capa preservada", obtido.CapaArquivo == esperado.CapaArquivo);
            Verificar("steamGridDbId nulo continua nulo", obtido.SteamGridDbId is null);
            Verificar("tempo jogado preservado", obtido.SegundosJogados == esperado.SegundosJogados);
            Verificar("favorito preservado", obtido.Favorito == esperado.Favorito);
            Verificar("marca de executável fixado preservada",
                obtido.ExecutavelFixadoPeloUsuario == esperado.ExecutavelFixadoPeloUsuario);

            var comData = original.Jogos[1];
            var lidoComData = lida.ObterPorId(comData.Id);
            Verificar("data foi lida", lidoComData?.UltimaVezJogado is not null);
            if (lidoComData is null) return;

            Verificar("data preservada em UTC",
                lidoComData.UltimaVezJogado == comData.UltimaVezJogado,
                lidoComData.UltimaVezJogado?.ToString("o"));
            Verificar("acentos e aspas preservados", lidoComData.Titulo == comData.Titulo, lidoComData.Titulo);
            Verificar("steamGridDbId preenchido preservado", lidoComData.SteamGridDbId == comData.SteamGridDbId);
            Verificar("argumentos com hífen preservados", lidoComData.Argumentos == comData.Argumentos);
        }

        private static void TestarRecusaDeSalvarCaminhoAbsoluto()
        {
            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "invalido",
                Titulo = "Com caminho absoluto",
                ExecutavelRelativo = @"D:\Jogos\qualquer\jogo.exe"
            });

            Verificar("Validar acusa caminho absoluto", biblioteca.Validar().Count > 0);

            var destino = Path.Combine(Caminhos.PastaEstado, "nao-deve-existir.json");
            Verificar("Salvar recusa caminho absoluto",
                Lanca<InvalidOperationException>(() => biblioteca.Salvar(destino)));
            Verificar("arquivo inválido não foi criado", !File.Exists(destino));
        }

        private static void TestarConfig()
        {
            var padrao = Config.Carregar();
            Verificar("config ausente usa padrões",
                !padrao.TemChaveSteamGridDb() && padrao.TamanhoCard == TamanhoCard.M);
            Verificar("config ausente já traz a Riot na lista de pastas ignoradas",
                padrao.PastasIgnoradas.Contains("Riot Games"));
            Verificar("config ausente já traz os executáveis da Riot ignorados",
                padrao.ExecutaveisIgnorados.Contains("RiotClientServices"));

            var paraSalvar = new Config
            {
                SteamGridDbApiKey = "chave-de-teste-123",
                TamanhoCard = TamanhoCard.G,
                Ordenacao = OrdenacaoBiblioteca.JogadosRecentemente,
                SomenteFavoritos = true
            };
            paraSalvar.PastasIgnoradas.Add("Steam");
            paraSalvar.Salvar();

            var lida = Config.Carregar();
            Verificar("api key preservada", lida.SteamGridDbApiKey == "chave-de-teste-123");
            Verificar("tamanho do card preservado", lida.TamanhoCard == TamanhoCard.G);
            Verificar("ordenação preservada", lida.Ordenacao == OrdenacaoBiblioteca.JogadosRecentemente);
            Verificar("filtro de favoritos preservado", lida.SomenteFavoritos);
            Verificar("pasta acrescentada à lista de ignoradas preservada",
                lida.PastasIgnoradas.Contains("Steam") && lida.PastasIgnoradas.Contains("Riot Games"),
                string.Join(", ", lida.PastasIgnoradas.ToArray()));

            // Apagar a lista no arquivo tem que valer: "não ignore nada" não pode voltar
            // silenciosamente para os padrões de fábrica.
            var semNada = new Config();
            semNada.PastasIgnoradas.Clear();
            semNada.ExecutaveisIgnorados.Clear();
            semNada.Salvar();

            var relida = Config.Carregar();
            Verificar("lista esvaziada no arquivo continua vazia",
                relida.PastasIgnoradas.Count == 0 && relida.ExecutaveisIgnorados.Count == 0,
                string.Join(", ", relida.PastasIgnoradas.ToArray()));

            // A pasta Launcher\ tem que poder ser movida para qualquer lugar do HD sem
            // reconfiguração: nada de caminho absoluto no config, em nenhum campo.
            paraSalvar.Salvar();
            var conteudo = ArquivoTexto.Ler(Caminhos.ArquivoConfig);

            Verificar("config.json não guarda letra de drive", !LetraDeDrive.IsMatch(conteudo), conteudo);
            Verificar("config.json não guarda caminho de rede", !conteudo.Contains(@"\\\\"));
            Verificar("config.json não guarda a pasta do launcher",
                !conteudo.Contains(Caminhos.PastaBase.Replace(@"\", @"\\")));
        }

        private static void TestarGeracaoDeId()
        {
            var biblioteca = new Biblioteca();

            var id1 = biblioteca.GerarId("Need for Speed: Most Wanted (Black Edition)");
            Verificar("id vira slug", id1 == "need-for-speed-most-wanted-black-edition", id1);

            biblioteca.Jogos.Add(new Jogo { Id = id1, Titulo = "x", ExecutavelRelativo = @"Jogos\x\x.exe" });

            var id2 = biblioteca.GerarId("Need for Speed: Most Wanted (Black Edition)");
            Verificar("id duplicado ganha sufixo", id2 == $"{id1}-2", id2);

            Verificar("acento vira ascii no id", biblioteca.GerarId("Coração Valente") == "coracao-valente");
            Verificar("título só de símbolos cai no padrão", biblioteca.GerarId("!!!") == "jogo");
        }

        /// <summary>
        /// O teste que importa: copia a pasta portátil inteira para outro lugar
        /// (simulando o HD montado com outra letra) e confere que os caminhos
        /// continuam resolvendo, agora apontando para a nova raiz.
        /// </summary>
        private static void TestarTrocaDeLetraDeDrive(string sandboxA, string sandboxB)
        {
            CopiarPasta(Path.Combine(sandboxA, Caminhos.NomePastaEstado),
                        Path.Combine(sandboxB, Caminhos.NomePastaEstado));

            Caminhos.DefinirPastaBase(sandboxB);
            try
            {
                var lida = Biblioteca.Carregar();
                Verificar("biblioteca abre na nova raiz", lida.Jogos.Count > 0);
                if (lida.Jogos.Count == 0) return;

                var resolvido = lida.Jogos[0].CaminhoExecutavel();
                Verificar("caminho resolve dentro da nova raiz",
                    resolvido is not null && resolvido.StartsWith(sandboxB, StringComparison.OrdinalIgnoreCase),
                    resolvido);
                Verificar("caminho não aponta mais para a raiz antiga",
                    resolvido is not null &&
                    !resolvido.StartsWith(sandboxA + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    resolvido);
            }
            finally
            {
                Caminhos.DefinirPastaBase(sandboxA);
            }
        }

        // ---- Apoio -------------------------------------------------------------------------

        private static Biblioteca MontarBibliotecaDeExemplo()
        {
            var biblioteca = new Biblioteca();
            biblioteca.PastasEscaneadas.Add("Jogos");
            biblioteca.PastasEscaneadas.Add(@"Jogos\Antigos");

            biblioteca.Jogos.Add(new Jogo
            {
                Id = "nfsmw-black",
                Titulo = "Need for Speed: Most Wanted (Black Edition)",
                ExecutavelRelativo = @"Jogos\Antigos\Carros\Need For Speed Most Wanted Black Edition\speed.exe",
                Argumentos = "",
                CapaArquivo = "nfsmw-black.jpg",
                SteamGridDbId = null,
                SegundosJogados = 0,
                UltimaVezJogado = null,
                Favorito = false,
                ExecutavelFixadoPeloUsuario = false
            });

            // Segundo jogo cobre acento, aspas, data e id do SteamGridDB preenchidos.
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "coracao-de-aco",
                Titulo = "Coração de \"Aço\" — Edição Especial",
                ExecutavelRelativo = @"Jogos\Indies\Coracao de Aco\game.exe",
                Argumentos = "-windowed",
                CapaArquivo = "coracao-de-aco.jpg",
                SteamGridDbId = 5432,
                SegundosJogados = 8220,
                UltimaVezJogado = new DateTime(2026, 8, 10, 21, 30, 0, DateTimeKind.Utc),
                Favorito = true,
                ExecutavelFixadoPeloUsuario = true
            });

            return biblioteca;
        }

        private static void CopiarPasta(string origem, string destino)
        {
            Directory.CreateDirectory(destino);

            foreach (var arquivo in Directory.GetFiles(origem))
                File.Copy(arquivo, Path.Combine(destino, Path.GetFileName(arquivo)), overwrite: true);

            foreach (var subpasta in Directory.GetDirectories(origem))
                CopiarPasta(subpasta, Path.Combine(destino, Path.GetFileName(subpasta)));
        }

        private static void LimparSandbox(string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox);
            try
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception ex)
            {
                _saida($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
            }
        }

        /// <summary>Verdadeiro se a ação lançar exatamente a exceção esperada.</summary>
        private static bool Lanca<TExcecao>(Action acao) where TExcecao : Exception
        {
            try
            {
                acao();
                return false;
            }
            catch (TExcecao)
            {
                return true;
            }
        }

        private static void Verificar(string descricao, bool condicao, string? detalhe = null)
        {
            if (condicao)
            {
                _passou++;
                _saida($"  ok    {descricao}");
            }
            else
            {
                _falhou++;
                _saida($"  FALHA {descricao}{(string.IsNullOrEmpty(detalhe) ? "" : $"  -> obtido: {detalhe}")}");
            }
        }
    }
}
