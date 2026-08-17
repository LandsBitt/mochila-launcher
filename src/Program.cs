using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Mochila.Dados;
using Mochila.Scanner;
#if DEBUG
using Mochila.Diagnostico;
#endif

namespace Mochila
{
    internal static class Program
    {
        private const int AnexarAoProcessoPai = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            AtivarRedeDeSeguranca();

#if DEBUG
            // Tudo daqui até o #endif é ferramenta de desenvolvimento: acervo sintético,
            // benches e a suíte de testes. Some inteiro na build de Release — o launcher
            // entregue tem os modos de verdade e mais nada.

            // "Mochila.exe --dormir <segundos>" não é recurso do launcher: é o jogo de
            // mentira do bench de lançamento. Uma cópia deste exe dentro da sandbox faz
            // o papel de jogo, para o bench medir com um processo de verdade. Fica no
            // topo do Main porque não pode carregar janela nenhuma.
            var indiceDormir = Array.FindIndex(args, a => string.Equals(a, "--dormir", StringComparison.OrdinalIgnoreCase));
            if (indiceDormir >= 0)
            {
                System.Threading.Thread.Sleep(NumeroDepoisDe(args, indiceDormir, padrao: 10) * 1000);
                return 0;
            }

            // "Mochila.exe --simular-launcher-proprio <segundos>" imita o jogo que tem
            // launcher próprio: dispara um processo-filho na mesma pasta e morre na hora.
            // É o cenário do Riot/EA/Ubisoft, e o único jeito de testar a adoção do filho
            // sem depender de ter um desses instalado.
            var indiceSimular = Array.FindIndex(args, a => string.Equals(a, "--simular-launcher-proprio", StringComparison.OrdinalIgnoreCase));
            if (indiceSimular >= 0)
            {
                var meuCaminho = System.Reflection.Assembly.GetEntryAssembly()!.Location;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = meuCaminho,
                    Arguments = $"--dormir {NumeroDepoisDe(args, indiceSimular, padrao: 8)}",
                    WorkingDirectory = Path.GetDirectoryName(meuCaminho)!,
                    UseShellExecute = false
                })?.Dispose();

                return 0;
            }

            // Modo verificação: "Mochila.exe --autoteste" roda os testes das fases e sai.
            if (args.Any(a => string.Equals(a, "--autoteste", StringComparison.OrdinalIgnoreCase)))
                return ExecutarAutoTeste();

            // "Mochila.exe --gerar-icone <arquivo.ico>" grava o ícone do launcher em
            // disco. É assim que o launcher.ico do recurso do exe nasce: do mesmo desenho
            // que a janela usa, para não existirem duas versões do gamepad envelhecendo
            // em separado. Só precisa rodar de novo quando o desenho mudar.
            var indiceIcone = Array.FindIndex(args, a => string.Equals(a, "--gerar-icone", StringComparison.OrdinalIgnoreCase));
            if (indiceIcone >= 0)
                return GerarIcone(indiceIcone + 1 < args.Length ? args[indiceIcone + 1] : "launcher.ico");

            // "Mochila.exe --escanear <pasta>" roda o scanner de verdade contra uma pasta
            // do HD e imprime o placar. Serve para eu conferir a fase 2 no meu acervo real,
            // antes de existir a janela de revisão (fase 3). Não grava nada.
            if (args.Any(a => string.Equals(a, "--bench-miniaturas", StringComparison.OrdinalIgnoreCase)))
                return ExecutarBenchDeMiniaturas();

            var indiceEscanear = Array.FindIndex(args, a => string.Equals(a, "--escanear", StringComparison.OrdinalIgnoreCase));
            if (indiceEscanear >= 0)
                return ExecutarScanDeVerificacao(args.Skip(indiceEscanear + 1).ToArray());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // "Mochila.exe --revisao-demo" abre a janela de revisão com o acervo sintético
            // dos testes (linha amarela, combo de 15, títulos sujos de repack). Não grava nada.
            if (args.Any(a => string.Equals(a, "--revisao-demo", StringComparison.OrdinalIgnoreCase)))
                return ExecutarRevisaoDeDemonstracao(args);

            // "Mochila.exe --bench-memoria [n]" responde com número a regra da spec
            // ("200 jogos parados abaixo de ~80 MB"). Usa acervo sintético e apaga tudo no fim.
            // Com "--ciclos N" ele desce e sobe a biblioteca N vezes e imprime a série:
            // é assim que vazamento aparece (série que só sobe), não numa medida só.
            var indiceBenchMemoria = Array.FindIndex(args, a => string.Equals(a, "--bench-memoria", StringComparison.OrdinalIgnoreCase));
            if (indiceBenchMemoria >= 0)
            {
                var indiceCiclos = Array.FindIndex(args, a => string.Equals(a, "--ciclos", StringComparison.OrdinalIgnoreCase));
                return ExecutarBenchDeMemoria(
                    NumeroDepoisDe(args, indiceBenchMemoria, padrao: 200),
                    indiceCiclos >= 0 ? NumeroDepoisDe(args, indiceCiclos, padrao: 10) : 1);
            }

            // "Mochila.exe --bench-detalhes [n]" abre e fecha a tela de detalhes n vezes
            // (50 por padrão) e mostra a série de memória. É a prova de que o painel da
            // fase 11 libera a arte grande — a regra que um Form por jogo quebraria.
            var indiceBenchDetalhes = Array.FindIndex(args, a => string.Equals(a, "--bench-detalhes", StringComparison.OrdinalIgnoreCase));
            if (indiceBenchDetalhes >= 0)
            {
                return ComSaidaDeTexto("Bench da tela de detalhes",
                    (escrever, _) => BenchDeDetalhes.Executar(NumeroDepoisDe(args, indiceBenchDetalhes, padrao: 50), escrever));
            }

            // "Mochila.exe --bench-lancamento" mede o launcher escondido com um jogo
            // (processo de verdade) aberto: RAM, handles e CPU dormindo.
            if (args.Any(a => string.Equals(a, "--bench-lancamento", StringComparison.OrdinalIgnoreCase)))
            {
                var indiceCiclosDeLancamento = Array.FindIndex(args, a => string.Equals(a, "--ciclos", StringComparison.OrdinalIgnoreCase));
                var quantos = indiceCiclosDeLancamento >= 0 ? NumeroDepoisDe(args, indiceCiclosDeLancamento, padrao: 4) : 1;

                return ComSaidaDeTexto("Bench de lançamento",
                    (escrever, _) => BenchDeLancamento.Executar(quantos, escrever));
            }

            // "Mochila.exe --grade-demo [n]" abre a janela com um acervo sintético de n
            // jogos, para eu conferir a grade sem ter catalogado nada ainda. A sandbox é
            // apagada ao fechar: a biblioteca de verdade não é tocada.
            var indiceGradeDemo = Array.FindIndex(args, a => string.Equals(a, "--grade-demo", StringComparison.OrdinalIgnoreCase));
            var demonstracao = indiceGradeDemo >= 0;

            if (demonstracao)
            {
                Diagnostico.AcervoDeDemonstracao.Montar(NumeroDepoisDe(args, indiceGradeDemo, padrao: 60));

                // "--com-historico" inventa sessões para a demonstração: é o que permite
                // olhar a tela de estatísticas e a seção "Continuar jogando" da fase 13.
                if (args.Any(a => string.Equals(a, "--com-historico", StringComparison.OrdinalIgnoreCase)))
                    Diagnostico.AcervoDeDemonstracao.MontarHistorico();
            }

            try
            {
                // "Mochila.exe --captura <arquivo.png>" desenha a janela num arquivo, para eu
                // conferir o visual sem precisar estar na frente do PC. Não rouba o foco.
                var indiceCaptura = Array.FindIndex(args, a => string.Equals(a, "--captura", StringComparison.OrdinalIgnoreCase));
                if (indiceCaptura >= 0 && indiceCaptura + 1 < args.Length)
                {
                    // "--detalhes" fotografa a tela de detalhes (fase 11) e "--marcados"
                    // fotografa a grade com uma busca por tag e tudo marcado (fase 12).
                    // As duas só existem depois de alguém apertar algo — daí o preparador.
                    var comDetalhes = args.Any(a => string.Equals(a, "--detalhes", StringComparison.OrdinalIgnoreCase));
                    var comMarcacao = args.Any(a => string.Equals(a, "--marcados", StringComparison.OrdinalIgnoreCase));

                    // "--estatisticas" fotografa a tela da fase 13. Ela é uma janela própria,
                    // então é fotografada direto, como a janela de revisão.
                    if (args.Any(a => string.Equals(a, "--estatisticas", StringComparison.OrdinalIgnoreCase)))
                    {
                        var estatisticas = new UI.FormEstatisticas(Modelo.HistoricoDeSessoes.Carregar(),
                                                                   Modelo.Biblioteca.Carregar());

                        Diagnostico.CapturaDeTela.Capturar(estatisticas, args[indiceCaptura + 1],
                            new Size(980, 760));
                        return 0;
                    }

                    Func<Form, Control?>? preparar = null;

                    if (comDetalhes) preparar = janela => ((FormPrincipal)janela).AbrirDetalhesParaDiagnostico();
                    else if (comMarcacao)
                    {
                        preparar = janela =>
                        {
                            var principal = (FormPrincipal)janela;
                            principal.BuscarParaDiagnostico("#corrida");
                            principal.TratarSelecaoMultiplaParaDiagnostico(Keys.A, Keys.Control);
                            return null;
                        };
                    }

                    // "--tamanho L A" fotografa noutro tamanho de janela. Existe porque bug de
                    // layout mora na largura-limite, não na largura confortável: a barra
                    // superior tem controle em posição fixa à esquerda e botão colado à
                    // direita, e é só apertando a janela que dá para ver os dois se
                    // encontrarem.
                    var tamanho = new Size(1200, 800);
                    var indiceTamanho = Array.FindIndex(args, a => string.Equals(a, "--tamanho", StringComparison.OrdinalIgnoreCase));
                    if (indiceTamanho >= 0 && indiceTamanho + 2 < args.Length)
                    {
                        tamanho = new Size(NumeroDepoisDe(args, indiceTamanho, padrao: 1200),
                                           NumeroDepoisDe(args, indiceTamanho + 1, padrao: 800));
                    }

                    Diagnostico.CapturaDeTela.Capturar(new FormPrincipal(), args[indiceCaptura + 1],
                        tamanho, preparar);
                    return 0;
                }

                return Abrir();
            }
            finally
            {
                if (demonstracao) Diagnostico.AcervoDeDemonstracao.Limpar();
            }
#else
            // Release: só o launcher.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // "Mochila.exe --escanear <pasta>" continua existindo: é código de produção
            // (o mesmo scanner do F6) e é o que me deixa investigar um scan estranho no
            // HD sem precisar de uma build especial.
            var indiceEscanearRelease = Array.FindIndex(args, a => string.Equals(a, "--escanear", StringComparison.OrdinalIgnoreCase));
            if (indiceEscanearRelease >= 0)
                return ExecutarScanDeVerificacao(args.Skip(indiceEscanearRelease + 1).ToArray());

            return Abrir();
#endif
        }

        /// <summary>Abre a janela do launcher. É o caminho normal, o de todo dia.</summary>
        private static int Abrir()
        {
            Application.Run(new FormPrincipal());
            return 0;
        }

        /// <summary>
        /// Última linha de defesa: exceção que ninguém tratou vira uma caixa explicando o
        /// que fazer, nunca um stack trace na cara de quem só queria abrir um jogo.
        /// </summary>
        private static void AtivarRedeDeSeguranca()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (_, e) => Socorro(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Socorro(e.ExceptionObject as Exception);
        }

        private static void Socorro(Exception? erro)
        {
            var detalhe = erro?.Message ?? "erro desconhecido";

            try
            {
                MessageBox.Show(
                    "O launcher esbarrou num problema e parou essa operação." + Environment.NewLine +
                    Environment.NewLine +
                    detalhe + Environment.NewLine + Environment.NewLine +
                    "A biblioteca continua salva. Se o HD foi desconectado, reconecte e abra de novo.",
                    "Mochila Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception)
            {
                // Se nem MessageBox abre, não há mais nada a fazer aqui.
            }
        }

        /// <summary>Lê o número logo depois de uma opção ("--grade-demo 200"), ou usa o padrão.</summary>
        private static int NumeroDepoisDe(string[] args, int indiceDaOpcao, int padrao)
        {
            if (indiceDaOpcao + 1 < args.Length &&
                int.TryParse(args[indiceDaOpcao + 1], System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out var valor) &&
                valor > 0)
            {
                return valor;
            }
            return padrao;
        }

#if DEBUG
        private static int ExecutarAutoTeste()
        {
            return ComSaidaDeTexto("Auto-teste", (escrever, _) =>
            {
                escrever("Fase 1 — caminhos relativos e JSON");
                escrever("");
                var fase1 = AutoTeste.Executar(escrever);

                escrever("");
                escrever("Fase 2 — scanner de executáveis");
                escrever("");
                var fase2 = AutoTesteScanner.Executar(escrever);

                escrever("");
                escrever("Fase 3 — revisão do scan e gravação na biblioteca");
                escrever("");
                var fase3 = AutoTesteRevisao.Executar(escrever);

                escrever("");
                escrever("Fase 4 — grade de capas");
                escrever("");
                var fase4 = AutoTesteGrade.Executar(escrever);

                escrever("");
                escrever("Fase 5 — lançar o jogo e contar o tempo");
                escrever("");
                var fase5 = AutoTesteLancamento.Executar(escrever);

                escrever("");
                escrever("Fase 6 — capas (online, manual e fallback)");
                escrever("");
                var fase6 = AutoTesteCapas.Executar(escrever);

                escrever("");
                escrever("Fase 7 — robustez (o que acontece quando dá errado)");
                escrever("");
                var fase7 = AutoTesteRobustez.Executar(escrever);

                escrever("");
                escrever("Fase 8 — gamepad por XInput");
                escrever("");
                var fase8 = AutoTesteGamepad.Executar(escrever);

                escrever("");
                escrever("Fase 10 — ações do card, adicionar manual e religação");
                escrever("");
                var fase10 = AutoTesteAcoes.Executar(escrever);

                escrever("");
                escrever("Fase 11 — tela de detalhes");
                escrever("");
                var fase11 = AutoTesteDetalhes.Executar(escrever);

                escrever("");
                escrever("Fase 12 — biblioteca v3, busca com operadores e ações em lote");
                escrever("");
                var fase12 = AutoTesteBiblioteca3.Executar(escrever);

                escrever("");
                escrever("Fase 13 — log de sessões, estatísticas e \"continuar jogando\"");
                escrever("");
                var fase13 = AutoTesteSessoes.Executar(escrever);

                escrever("");
                var tudoOk = fase1 && fase2 && fase3 && fase4 && fase5 && fase6 && fase7 && fase8 &&
                             fase10 && fase11 && fase12 && fase13;
                escrever(tudoOk ? "TUDO PASSOU." : "HOUVE FALHAS.");
                return tudoOk;
            });
        }

        /// <summary>
        /// Grava o .ico do launcher (todos os tamanhos, de 16 a 256) no caminho pedido.
        /// </summary>
        private static int GerarIcone(string caminho)
        {
            return ComSaidaDeTexto("Gerar ícone", (escrever, _) =>
            {
                var destino = Path.IsPathRooted(caminho)
                    ? caminho
                    : Path.GetFullPath(Path.Combine(Caminhos.PastaBase, caminho));

                try
                {
                    var bytes = UI.IconeDaMochila.MontarIco(UI.IconeDaMochila.TamanhosDoArquivo);
                    File.WriteAllBytes(destino, bytes);

                    escrever($"{destino}");
                    escrever($"{bytes.Length / 1024.0:F1} KB, tamanhos: " +
                             string.Join(", ", Array.ConvertAll(UI.IconeDaMochila.TamanhosDoArquivo, t => t.ToString())));
                    return true;
                }
                catch (Exception erro)
                {
                    escrever($"Não consegui gravar o ícone: {erro.Message}");
                    return false;
                }
            });
        }

#endif   // DEBUG

        /// <summary>
        /// Escaneia uma pasta real e imprime o resultado. O caminho pode ser relativo à
        /// pasta do launcher (o normal, já que tudo mora no mesmo HD) ou absoluto.
        /// </summary>
        private static int ExecutarScanDeVerificacao(string[] pastas)
        {
            return ComSaidaDeTexto("Scan de verificação", (escrever, _) =>
            {
                if (pastas.Length == 0)
                {
                    escrever(@"Uso: Mochila.exe --escanear <pasta> [outra pasta...]   (ex.: --escanear ..\Jogos)");
                    return false;
                }

                var raizes = new List<string>();
                foreach (var pasta in pastas)
                {
                    var absoluta = Path.IsPathRooted(pasta)
                        ? Path.GetFullPath(pasta)
                        : Path.GetFullPath(Path.Combine(Caminhos.PastaBase, pasta));

                    if (!Directory.Exists(absoluta))
                    {
                        escrever($"Pasta não encontrada: {absoluta}");
                        continue;
                    }
                    raizes.Add(absoluta);
                }

                if (raizes.Count == 0) return false;

                var config = Modelo.Config.Carregar();
                escrever($"Pastas ignoradas    : {Listar(config.PastasIgnoradas)}");
                escrever($"Executáveis ignorados: {Listar(config.ExecutaveisIgnorados)}");

                var relogio = System.Diagnostics.Stopwatch.StartNew();
                var disco = new SistemaDeArquivosReal();
                var scanner = new ScannerDeJogos(disco, FiltroDeExclusao.De(config));
                var jogos = scanner.Escanear(raizes);
                relogio.Stop();

                foreach (var jogo in jogos)
                {
                    escrever("");
                    escrever($"{jogo.TituloProposto}{(jogo.BaixaConfianca ? "   [BAIXA CONFIANÇA]" : "")}");
                    escrever($"  pasta: {jogo.PastaDoJogo}");

                    for (var i = 0; i < jogo.Candidatos.Count; i++)
                    {
                        var marca = i == 0 ? "->" : "  ";
                        escrever($"  {marca} {jogo.Candidatos[i].Detalhar()}");
                    }
                }

                if (scanner.Descartes.Count > 0)
                {
                    escrever("");
                    escrever($"--- {scanner.Descartes.Count} pasta(s) descartada(s) ---");
                    foreach (var descarte in scanner.Descartes)
                        escrever($"  {descarte.Pasta}{Environment.NewLine}      motivo: {descarte.Motivo}");
                }

                escrever("");
                escrever($"{scanner.PastasVisitadas} pasta(s) varrida(s), " +
                         $"{jogos.Count} jogo(s) identificado(s), " +
                         $"{scanner.Descartes.Count} descartada(s), em {relogio.ElapsedMilliseconds} ms.");

                var total = relogio.Elapsed.TotalMilliseconds;
                var outros = total - disco.MillisegundosDeListagem - disco.MillisegundosDePe;
                escrever($"  listagem de pastas : {disco.MillisegundosDeListagem,8:F1} ms  " +
                         $"({disco.Listagens} listagens)");
                escrever($"  leitura de PE      : {disco.MillisegundosDePe,8:F1} ms  " +
                         $"({disco.ExecutaveisLidos} exe(s), {disco.LeiturasDePeServidasPeloCache} do cache)");
                escrever($"     dos quais metadados: {disco.MillisegundosDeMetadados,8:F1} ms");
                escrever($"  placar e o resto   : {outros,8:F1} ms");
                return true;
            });
        }

#if DEBUG
        /// <summary>
        /// Abre a janela de revisão sobre o acervo sintético e conta, no fim, o que eu
        /// teria mandado gravar. Nenhum arquivo é tocado.
        /// </summary>
        private static int ExecutarRevisaoDeDemonstracao(string[] args)
        {
            var scanner = new ScannerDeJogos(AutoTesteRevisao.MontarAcervoSintetico());
            var jogos = scanner.Escanear(new[] { AutoTesteRevisao.RaizSintetica });

            // Com --captura, desenha a janela num PNG em vez de abrir: é como eu confiro
            // o layout de verdade, que é onde os bugs de UI moram.
            var indiceCaptura = Array.FindIndex(args, a => string.Equals(a, "--captura", StringComparison.OrdinalIgnoreCase));
            if (indiceCaptura >= 0 && indiceCaptura + 1 < args.Length)
            {
                var janelaParaFoto = new UI.FormRevisaoDoScan(jogos, scanner.Descartes);
                Diagnostico.CapturaDeTela.Capturar(janelaParaFoto, args[indiceCaptura + 1],
                    janelaParaFoto.Size);
                return 0;
            }

            using (var janela = new UI.FormRevisaoDoScan(jogos, scanner.Descartes))
            {
                var resultado = janela.ShowDialog();

                var mensagem = resultado == DialogResult.OK
                    ? $"Você confirmaria {janela.Confirmados().Count} de {janela.Linhas.Count} jogo(s)."
                    : "Revisão cancelada.";

                MessageBox.Show($"{mensagem}{Environment.NewLine}{Environment.NewLine}" +
                                "Isto é uma demonstração com dados sintéticos: nada foi gravado.",
                    "Revisão (demonstração)", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        /// <summary>
        /// Mede a carga de miniaturas da biblioteca real: quanto tempo por imagem e quanto
        /// disso é gravação do thumb. Responde "a grade está lenta por quê?" com número.
        /// </summary>
        private static int ExecutarBenchDeMiniaturas()
        {
            return ComSaidaDeTexto("Bench de miniaturas", (escrever, _) =>
            {
                var biblioteca = Modelo.Biblioteca.Carregar();
                if (biblioteca.Jogos.Count == 0)
                {
                    escrever("Biblioteca vazia.");
                    return false;
                }

                var quantos = Math.Min(20, biblioteca.Jogos.Count);
                var relogio = System.Diagnostics.Stopwatch.StartNew();

                using (var cache = new UI.CacheDeMiniaturas())
                {
                    for (var i = 0; i < quantos; i++) cache.Obter(biblioteca.Jogos[i]);

                    var limite = Environment.TickCount + 60_000;
                    while (cache.Carregadas < quantos && Environment.TickCount < limite)
                        System.Threading.Thread.Sleep(20);

                    relogio.Stop();

                    escrever($"{cache.Carregadas} de {quantos} miniatura(s) em {relogio.ElapsedMilliseconds} ms");
                    escrever($"  dentro da thread de carga : {cache.MillisegundosDeCarga,8:F1} ms " +
                             $"({cache.MillisegundosDeCarga / Math.Max(1, cache.Carregadas),6:F1} ms cada)");
                    escrever($"  gravando o thumb no disco : {cache.MillisegundosDeGravacao,8:F1} ms");
                    escrever($"  miniaturas geradas do zero: {cache.MiniaturasGeradas}");
                    escrever($"  imagens vivas na memória  : {cache.ImagensEmMemoria}");
                }
                return true;
            });
        }

        /// <summary>
        /// Roda o bench de memória da grade. Precisa de janela de verdade (WinForms e GDI+
        /// carregados), então vem depois do EnableVisualStyles.
        /// </summary>
        private static int ExecutarBenchDeMemoria(int quantidade, int ciclos)
            => ComSaidaDeTexto("Bench de memória da grade",
                (escrever, _) => BenchDeMemoria.Executar(quantidade, ciclos, escrever));

#endif   // DEBUG

        private static string Listar(List<string> itens)
            => itens.Count == 0 ? "(nenhum)" : string.Join(", ", itens.ToArray());

        /// <summary>
        /// Um WinExe não tem console próprio. Se a saída já estiver redirecionada
        /// (pipe/arquivo), escrevemos nela direto; senão anexamos ao console de quem
        /// chamou. Sem nenhum dos dois, cai para MessageBox.
        /// </summary>
        private static int ComSaidaDeTexto(string titulo, Func<Action<string>, string, bool> tarefa)
        {
            var temConsole = Console.IsOutputRedirected || AttachConsole(AnexarAoProcessoPai);

            if (temConsole)
            {
                Console.WriteLine();
                Console.WriteLine(titulo);
                Console.WriteLine();

                var ok = tarefa(Console.WriteLine, titulo);
                Console.WriteLine();
                return ok ? 0 : 1;
            }

            var relatorio = new StringBuilder();
            var passou = tarefa(linha => relatorio.AppendLine(linha), titulo);

            MessageBox.Show(relatorio.ToString(),
                passou ? $"{titulo}: ok" : $"{titulo}: houve falhas",
                MessageBoxButtons.OK,
                passou ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return passou ? 0 : 1;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int idDoProcesso);
    }
}
