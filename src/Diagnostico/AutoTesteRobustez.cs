// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.IO;
using System.Threading;
using Mochila.Capas;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação da fase 7: o que acontece quando o mundo dá errado.
    ///
    /// Os três cenários que um HD externo produz de verdade — ele é desconectado no meio
    /// do uso, o JSON quebra numa queda de energia, e eu apago uma capa pelo Explorer com
    /// o launcher aberto. Nenhum deles pode virar stack trace na tela.
    /// </summary>
    public static class AutoTesteRobustez
    {
        private const string NomePastaSandbox = "_autoteste-robustez-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarBibliotecaCorrompida(v, raizReal);
                TestarHdDesconectado(v, raizReal);
                TestarCapaApagadaPorFora(v, raizReal);
                TestarEntradasEstranhas(v, raizReal);
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

        // ---- biblioteca.json quebrado -----------------------------------------------------------

        private static void TestarBibliotecaCorrompida(Verificador v, string raizReal)
        {
            v.Escrever("Biblioteca corrompida ou vazia");

            var sandbox = PrepararSandbox(raizReal);
            var arquivo = Path.Combine(sandbox, "quebrado.json");

            // Arquivo vazio: é o resultado clássico de queda de energia durante a gravação.
            File.WriteAllText(arquivo, "");
            v.Verificar("arquivo vazio vira erro tratado, não exceção crua",
                LancaTratado(() => Biblioteca.Carregar(arquivo)));

            File.WriteAllText(arquivo, "{\"versao\": 2, \"jogos\": [ {\"id\": \"a\",");
            v.Verificar("JSON cortado no meio vira erro tratado",
                LancaTratado(() => Biblioteca.Carregar(arquivo)));

            File.WriteAllText(arquivo, "isto não é json");
            v.Verificar("lixo puro vira erro tratado", LancaTratado(() => Biblioteca.Carregar(arquivo)));

            File.WriteAllText(arquivo, "[1, 2, 3]");
            v.Verificar("JSON válido mas do tipo errado vira erro tratado",
                LancaTratado(() => Biblioteca.Carregar(arquivo)));

            // Só espaço em branco.
            File.WriteAllText(arquivo, "   \r\n  ");
            v.Verificar("só espaços vira erro tratado", LancaTratado(() => Biblioteca.Carregar(arquivo)));

            // Estruturas parciais NÃO podem virar erro: campo faltando é normal em arquivo
            // antigo, e recusar tudo por causa disso seria pior que aceitar o que dá.
            File.WriteAllText(arquivo, "{}");
            var vazia = Biblioteca.Carregar(arquivo);
            v.Verificar("objeto JSON vazio abre como biblioteca vazia", vazia.Jogos.Count == 0);

            File.WriteAllText(arquivo, "{\"versao\":2,\"jogos\":[{\"titulo\":\"Sem id nem exe\"}]}");
            var incompleta = Biblioteca.Carregar(arquivo);
            v.Verificar("jogo sem campos obrigatórios é lido sem quebrar", incompleta.Jogos.Count == 1);
            v.Verificar("e a validação o recusa na hora de salvar", incompleta.Validar().Count > 0,
                string.Join(" | ", incompleta.Validar().ToArray()));

            // Config ilegível não pode impedir o launcher de abrir.
            var configRuim = Path.Combine(sandbox, "config-ruim.json");
            File.WriteAllText(configRuim, "{{{{");
            var config = Config.Carregar(configRuim);
            v.Verificar("config corrompido cai nos padrões, sem exceção", config.TamanhoCard == TamanhoCard.M);
        }

        // ---- HD desconectado ---------------------------------------------------------------------

        /// <summary>
        /// Simula o HD sumindo apontando a raiz portátil para um caminho que não existe —
        /// do ponto de vista do código, é exatamente o que acontece quando alguém puxa o
        /// cabo USB: todo caminho continua "válido" e todo acesso falha.
        /// </summary>
        private static void TestarHdDesconectado(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("HD desconectado no meio do uso");

            var sandbox = PrepararSandbox(raizReal);

            var biblioteca = new Biblioteca();
            biblioteca.PastasEscaneadas.Add("Jogos");
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "nfsc",
                Titulo = "NFS Carbon",
                ExecutavelRelativo = @"Jogos\NFS Carbon\NFSC.exe",
                CapaArquivo = "nfsc.jpg"
            });

            // O drive some de verdade: uma LETRA que não existe.
            //
            // Simular com uma subpasta inexistente de um drive real não serve — o Windows
            // simplesmente cria a pasta na hora de gravar, e o teste passaria sem provar
            // nada. Foi assim que a primeira versão deste teste mentiu.
            var letra = LetraDeDriveInexistente();
            if (letra is null)
            {
                v.Escrever("  (todas as letras de drive estão em uso — cenário não exercitado)");
                return;
            }

            var sumido = $@"{letra}:\Mochila";
            Caminhos.DefinirPastaBase(sumido);

            v.Verificar("ler biblioteca de pasta inexistente devolve vazia, sem lançar",
                Biblioteca.Carregar().Jogos.Count == 0);

            v.Verificar("ler config de pasta inexistente devolve padrões, sem lançar",
                Config.Carregar().TamanhoCard == TamanhoCard.M);

            var jogo = biblioteca.Jogos[0];

            v.Verificar("executável de HD ausente é reportado como inexistente", !jogo.ExecutavelExiste());
            v.Verificar("caminho da capa não quebra", jogo.CaminhoCapa() != null);
            v.Verificar("caminho da miniatura não quebra", jogo.CaminhoThumbnail().Length > 0);

            // O lançamento tem que recusar com erro tratado, não com exceção de sistema.
            var recusouLancar = false;
            try
            {
                Execucao.LancadorDeJogos.MontarInicio(jogo);
            }
            catch (Execucao.ExecutavelIndisponivelException)
            {
                recusouLancar = true;
            }
            catch (Exception)
            {
                recusouLancar = false;
            }
            v.Verificar("lançar jogo de HD desconectado vira erro tratado", recusouLancar);

            // A miniatura cai no card gerado em vez de estourar.
            using (var cache = new CacheDeMiniaturas())
            {
                cache.Obter(jogo);
                var apareceu = EsperarAte(() => cache.Obter(jogo) != null, 5000);
                v.Verificar("a grade desenha um card mesmo com o HD fora", apareceu);
            }

            // Gravar tem que falhar de forma tratável (a janela mostra recado e segue).
            var erroAoSalvar = "";
            try
            {
                biblioteca.Salvar();
            }
            catch (Exception erro)
            {
                erroAoSalvar = erro.Message;
            }

            v.Verificar("salvar em HD ausente falha com mensagem legível, não com stack",
                erroAoSalvar.Length > 0 && !erroAoSalvar.Contains("   at "), erroAoSalvar);

            // A capa local e o scanner também não podem estourar.
            v.Verificar("procurar arte em pasta inexistente devolve null",
                CapaLocal.Procurar(Path.Combine(sumido, "Jogos", "nada"), "Nada") is null);

            var scanner = new Scanner.ScannerDeJogos(new Scanner.SistemaDeArquivosReal());
            v.Verificar("escanear pasta inexistente devolve lista vazia, sem lançar",
                scanner.Escanear(new[] { Path.Combine(sumido, "Jogos") }).Count == 0);

            Caminhos.DefinirPastaBase(sandbox);
        }

        // ---- Capa apagada por fora -----------------------------------------------------------------

        /// <summary>
        /// Eu apago a capa pelo Explorer com o launcher aberto. O risco real não é
        /// quebrar: é a grade continuar mostrando a miniatura antiga guardada em cache,
        /// como se a capa ainda existisse.
        /// </summary>
        private static void TestarCapaApagadaPorFora(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Capa apagada por fora com o launcher aberto");

            PrepararSandbox(raizReal);

            var jogo = new Jogo { Id = "apagada", Titulo = "Jogo Com Capa", ExecutavelRelativo = @"Jogos\x\x.exe" };
            jogo.CapaArquivo = "apagada.jpg";

            using (var capa = GeradorDeCapa.Gerar("Capa de verdade", 600, 900))
                GeradorDeCapa.SalvarJpeg(capa, Path.Combine(Caminhos.PastaCapas, "apagada.jpg"));

            using (var cache = new CacheDeMiniaturas())
            {
                cache.Obter(jogo);
                v.Verificar("a miniatura é gerada da capa", EsperarAte(() => cache.Obter(jogo) != null, 5000));
                v.Verificar("e o thumb foi para o cache em disco", File.Exists(jogo.CaminhoThumbnail()));

                // Some a capa, mas o thumb antigo continua no disco.
                File.Delete(jogo.CaminhoCapa()!);
                v.Verificar("o thumb velho ainda está lá (é a armadilha)", File.Exists(jogo.CaminhoThumbnail()));

                cache.Invalidar(jogo.Id);
                cache.Obter(jogo);
                v.Verificar("recarrega sem quebrar", EsperarAte(() => cache.Obter(jogo) != null, 5000));

                // O que importa: sem capa, o desenho é o card gerado — não o thumb velho.
                using (var geradoAgora = GeradorDeCapa.Gerar(jogo.Titulo,
                           GeradorDeCapa.LarguraDaMiniatura, GeradorDeCapa.AlturaDaMiniatura))
                {
                    var atual = cache.Obter(jogo);
                    v.Verificar("a grade passa a desenhar o card gerado, não a capa apagada",
                        atual != null && MesmaCorDeFundo(atual, geradoAgora));
                }
            }

            // Capa corrompida (zero byte) também não pode derrubar nada.
            File.WriteAllBytes(Path.Combine(Caminhos.PastaCapas, "apagada.jpg"), Array.Empty<byte>());

            using (var cache = new CacheDeMiniaturas())
            {
                cache.Obter(jogo);
                v.Verificar("capa de 0 byte cai no card gerado", EsperarAte(() => cache.Obter(jogo) != null, 5000));
            }

            v.Verificar("abrir imagem inválida devolve null em vez de lançar",
                CapaLocal.Carregar(Path.Combine(Caminhos.PastaCapas, "apagada.jpg")) is null);
        }

        // ---- Entradas estranhas ---------------------------------------------------------------------

        private static void TestarEntradasEstranhas(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Entradas estranhas");

            PrepararSandbox(raizReal);

            var semNada = new Jogo { Id = "", Titulo = "", ExecutavelRelativo = "" };
            v.Verificar("jogo sem nada não quebra ExecutavelExiste", !semNada.ExecutavelExiste());
            v.Verificar("nem CaminhoCapa", semNada.CaminhoCapa() is null);

            v.Verificar("título vazio ainda gera card", GeradorDeCapa.Gerar("", 120, 180).Width == 120);

            var comCaracteresRuins = new Biblioteca();
            var id = comCaracteresRuins.GerarId("///???***");
            v.Verificar("título só de símbolos ainda gera id utilizável", id.Length > 0, id);

            v.Verificar("caminho com caractere inválido é recusado sem lançar",
                !Caminhos.EhRelativoValido("Jogos\\pas|ta\\jogo.exe"));

            v.Verificar("relativo nulo não quebra", Caminhos.ParaAbsolutoOuNulo(null) is null);
            v.Verificar("migração de caminho nulo devolve vazio", Caminhos.ParaRelativoMigrando(null).Length == 0);
        }

        // ---- Apoio ------------------------------------------------------------------------------------

        /// <summary>
        /// true quando a chamada falha do jeito certo: exceção NOSSA, com mensagem
        /// explicando o problema. Exceção crua do .NET (NullReference, IndexOutOfRange)
        /// reprova — é ela que vira tela feia para o usuário.
        /// </summary>
        private static bool LancaTratado(Action acao)
        {
            try
            {
                acao();
                return false;
            }
            catch (DadosCorrompidosException erro)
            {
                return erro.Message.Length > 0 && !erro.Message.Contains("   at ");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Uma letra de drive que o Windows não conhece, ou null se todas existirem.</summary>
        private static char? LetraDeDriveInexistente()
        {
            var usadas = new System.Collections.Generic.HashSet<char>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                var nome = drive.Name;
                if (nome.Length > 0) usadas.Add(char.ToUpperInvariant(nome[0]));
            }

            for (var letra = 'Z'; letra >= 'E'; letra--)
            {
                if (!usadas.Contains(letra)) return letra;
            }
            return null;
        }

        private static bool MesmaCorDeFundo(System.Drawing.Bitmap a, System.Drawing.Bitmap b)
            => a.GetPixel(2, 2).ToArgb() == b.GetPixel(2, 2).ToArgb();

        private static bool EsperarAte(Func<bool> condicao, int limiteMs)
        {
            var fim = Environment.TickCount + limiteMs;

            while (Environment.TickCount < fim)
            {
                if (condicao()) return true;
                Thread.Sleep(15);
            }
            return condicao();
        }

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
            try
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception ex)
            {
                v.Escrever($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
            }
        }
    }
}
#endif   // DEBUG
