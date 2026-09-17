// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.Scanner;
using Mochila.UI;
using System.Windows.Forms;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação da fase 17: tema configurável e relatório de integridade do acervo.
    ///
    /// (O som de navegação, terceiro item da fase na spec, ficou de fora desta entrega a
    /// pedido — não há nada dele para testar aqui.)
    ///
    /// As duas metades defendem promessas parecidas. No tema: <b>preferência ilegível não
    /// impede o launcher de abrir</b> — cor sem sentido cai no padrão e vira recado. No
    /// relatório: <b>contar não conserta</b> — varrer o acervo é leitura do começo ao fim, e
    /// é isso que torna seguro rodá-lo só para ver.
    /// </summary>
    public static class AutoTesteAparencia
    {
        private const string NomePastaSandbox = "_autoteste-aparencia-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarLeituraDeCor(v);
                TestarCorDeAcentoInvalida(v, raizReal);
                TestarPresetsNoConfig(v, raizReal);
                TestarLegibilidadeDasPaletas(v);
                TestarPisoDeContrasteNosDoisFundos(v);
                TestarRelatorioDeIntegridade(v, raizReal);
                TestarGeometriaDasConfiguracoes(v, raizReal);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                // O tema é estado global do processo: deixá-lo claro aqui pintaria a
                // próxima suíte (e a janela, num --autoteste seguido de uso) de outra cor.
                Tema.Aplicar(Tema.Paleta.Escuro, null);

                Caminhos.RestaurarPastaBase();
                LimparSandbox(v, raizReal);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- A cor de acento ------------------------------------------------------------------

        private static void TestarLeituraDeCor(Verificador v)
        {
            v.Escrever("Leitura da cor de acento");

            v.Verificar("#RRGGBB é lido",
                Tema.TentarLerCor("#78B4FF", out var azul) && azul.R == 0x78 && azul.G == 0xB4 && azul.B == 0xFF,
                azul.ToString());

            v.Verificar("sem o # também",
                Tema.TentarLerCor("FF8800", out var laranja) && laranja.R == 0xFF && laranja.B == 0x00);

            v.Verificar("com espaço em volta também", Tema.TentarLerCor("  #102030  ", out _));

            v.Verificar("nome que o .NET conhece é aceito", Tema.TentarLerCor("SteelBlue", out _));

            v.Verificar("texto sem sentido é recusado", !Tema.TentarLerCor("azul-bebê", out _));
            v.Verificar("hexadecimal pela metade é recusado", !Tema.TentarLerCor("#78B4", out _));
            v.Verificar("hexadecimal com letra inválida é recusado", !Tema.TentarLerCor("#GGGGGG", out _));
            v.Verificar("string vazia é recusada", !Tema.TentarLerCor("", out _));
            v.Verificar("null é recusado", !Tema.TentarLerCor(null, out _));

            v.VerificarTexto("e a cor volta para o arquivo em #RRGGBB",
                "#78B4FF", Tema.FormatarCor(Color.FromArgb(0x78, 0xB4, 0xFF)));
        }

        /// <summary>
        /// O teste que a spec pede: <b>cor de acento inválida não impede a abertura</b> —
        /// cai no padrão do tema e avisa.
        ///
        /// Os três casos que ela lista são diferentes entre si, e o meio é o que engana:
        /// campo ausente e string vazia significam "use a do tema" e NÃO são erro nenhum, e
        /// avisar neles encheria o rodapé de recado em toda abertura normal. Só texto
        /// presente e sem sentido é que merece o aviso.
        /// </summary>
        private static void TestarCorDeAcentoInvalida(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Cor de acento sem sentido: abre igual, no padrão, e avisa");

            PrepararSandbox(raizReal);

            Tema.Aplicar(Tema.Paleta.Escuro, null);
            var padraoDoEscuro = Tema.Acento;

            // 1) Valor sem sentido.
            Tema.Aplicar(Tema.Paleta.Escuro, "azul-que-nem-o-do-mar");
            v.Verificar("cor sem sentido cai no acento do tema", Tema.Acento == padraoDoEscuro,
                Tema.Acento.ToString());
            v.Verificar("e AVISA", Tema.Aviso is not null, Tema.Aviso);
            v.Verificar("o aviso diz o formato certo",
                Tema.Aviso is { } aviso && aviso.Contains("#RRGGBB"), Tema.Aviso);

            // 2) String vazia: não é erro, é "use a do tema".
            Tema.Aplicar(Tema.Paleta.Escuro, "");
            v.Verificar("string vazia cai no acento do tema", Tema.Acento == padraoDoEscuro);
            v.Verificar("e NÃO avisa (vazio não é erro)", Tema.Aviso is null, Tema.Aviso);

            // 3) Campo ausente: é o que Config devolve num arquivo sem a chave.
            var semCampo = Path.Combine(Caminhos.PastaEstado, "config-sem-acento.json");
            ArquivoTexto.EscreverAtomico(semCampo, "{ \"versao\": 1, \"tamanhoCard\": \"G\" }");

            var config = Config.Carregar(semCampo);
            v.Verificar("campo ausente vira string vazia no Config", config.CorDeAcento.Length == 0);

            Tema.Aplicar(config);
            v.Verificar("campo ausente cai no acento do tema", Tema.Acento == padraoDoEscuro);
            v.Verificar("e também não avisa", Tema.Aviso is null, Tema.Aviso);

            // 4) Cor válida: aí sim a preferência vale, e sem aviso nenhum.
            Tema.Aplicar(Tema.Paleta.Escuro, "#FF8800");
            v.Verificar("cor válida é obedecida",
                Tema.Acento == Color.FromArgb(0xFF, 0x88, 0x00), Tema.Acento.ToString());
            v.Verificar("e a seleção acompanha o acento", Tema.Selecao == Tema.Acento);
            v.Verificar("sem aviso nenhum", Tema.Aviso is null, Tema.Aviso);

            // 5) O arquivo inteiro corrompido, que é o pior caso: o launcher abre com os
            //    padrões, e é a fase 7 que já garantia isso — aqui só se confere que a
            //    fase 17 não estragou a garantia.
            var lixo = Path.Combine(Caminhos.PastaEstado, "config-lixo.json");
            ArquivoTexto.EscreverAtomico(lixo, "isto não é json { { {");

            var doLixo = Config.Carregar(lixo);
            v.Verificar("config corrompido devolve os padrões, sem lançar",
                doLixo.Tema == TemaDoLauncher.Escuro && doLixo.CorDeAcento.Length == 0);
        }

        private static void TestarPresetsNoConfig(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("O tema sobrevive ao disco");

            PrepararSandbox(raizReal);

            var padrao = new Config();
            v.Verificar("o padrão continua sendo o tema escuro", padrao.Tema == TemaDoLauncher.Escuro);
            v.Verificar("e sem cor de acento gravada", padrao.CorDeAcento.Length == 0);

            var escolhido = new Config { Tema = TemaDoLauncher.Claro, CorDeAcento = "#1C68C4" };
            escolhido.Salvar();

            var lido = Config.Carregar();
            v.Verificar("tema claro preservado", lido.Tema == TemaDoLauncher.Claro, lido.Tema.ToString());
            v.VerificarTexto("cor de acento preservada", "#1C68C4", lido.CorDeAcento);

            // Tema desconhecido no arquivo (editado à mão, ou versão futura) não pode
            // impedir a abertura: cai no escuro, como todo enum deste projeto.
            var estranho = Path.Combine(Caminhos.PastaEstado, "config-tema-estranho.json");
            ArquivoTexto.EscreverAtomico(estranho, "{ \"versao\": 1, \"tema\": \"neon-anos-80\" }");

            v.Verificar("tema desconhecido cai no escuro",
                Config.Carregar(estranho).Tema == TemaDoLauncher.Escuro);

            // Portabilidade: nada do que a fase 17 acrescentou pode gravar caminho.
            var conteudo = ArquivoTexto.Ler(Caminhos.ArquivoConfig);
            v.Verificar("config.json continua sem letra de drive",
                !System.Text.RegularExpressions.Regex.IsMatch(conteudo, @"[A-Za-z]:[\\/]"), conteudo);

            Tema.Aplicar(lido);
            v.Verificar("aplicar o config claro põe o preset claro no ar", !Tema.EhEscuro);
            v.Verificar("com a cor de acento escolhida",
                Tema.Acento == Color.FromArgb(0x1C, 0x68, 0xC4), Tema.Acento.ToString());
        }

        // ---- As duas paletas ------------------------------------------------------------------

        /// <summary>
        /// Um tema que não dá para ler não é um tema, é um bug com nome bonito. A régua é a
        /// da WCAG: 4,5 para corpo de texto e 3,0 para elemento gráfico — a mesma que a fase
        /// 14 já usava para o acento tirado da arte.
        /// </summary>
        private static void TestarLegibilidadeDasPaletas(Verificador v)
        {
            v.Escrever("");
            v.Escrever("As duas paletas são legíveis");

            foreach (var paleta in new[] { Tema.Paleta.Escuro, Tema.Paleta.Claro })
            {
                Tema.Aplicar(paleta, null);
                var nome = paleta.ToString().ToLowerInvariant();

                Conferir(v, $"[{nome}] texto sobre o fundo", Tema.Texto, Tema.Fundo, 4.5);
                Conferir(v, $"[{nome}] texto forte sobre o fundo", Tema.TextoForte, Tema.Fundo, 4.5);
                Conferir(v, $"[{nome}] texto sobre a superfície", Tema.Texto, Tema.Superficie, 4.5);
                Conferir(v, $"[{nome}] texto sobre o controle", Tema.Texto, Tema.Controle, 4.5);

                // Texto fraco e acento são rótulo apagado e elemento gráfico: 3,0 basta, e
                // exigir 4,5 deles apagaria a diferença entre "apagado" e "normal".
                Conferir(v, $"[{nome}] texto fraco sobre o fundo", Tema.TextoFraco, Tema.Fundo, 3.0);
                Conferir(v, $"[{nome}] acento sobre o fundo", Tema.Acento, Tema.Fundo, 3.0);
                Conferir(v, $"[{nome}] erro sobre o fundo", Tema.Erro, Tema.Fundo, 3.0);
                Conferir(v, $"[{nome}] linha de baixa confiança", Tema.TextoBaixaConfianca,
                         Tema.FundoBaixaConfianca, 3.0);
            }

            Tema.Aplicar(Tema.Paleta.Claro, null);
            v.Verificar("o tema claro tem fundo claro de verdade", Tema.Fundo.R > 200 && Tema.Fundo.B > 200,
                Tema.Fundo.ToString());
            v.Verificar("e a barra de título acompanha", !Tema.EhEscuro);

            Tema.Aplicar(Tema.Paleta.Escuro, null);
            v.Verificar("o tema escuro tem fundo escuro de verdade", Tema.Fundo.R < 60 && Tema.Fundo.B < 60,
                Tema.Fundo.ToString());
        }

        private static void Conferir(Verificador v, string descricao, Color frente, Color fundo, double piso)
        {
            var contraste = CorDominante.Contraste(frente, fundo);
            v.Verificar($"{descricao} (piso {piso:F1})", contraste >= piso, $"{contraste:F2}");
        }

        /// <summary>
        /// O piso de contraste da fase 14 tem que saber para que lado empurrar.
        ///
        /// Ele nasceu só sabendo clarear, porque só existia tema escuro. Sobre o fundo claro
        /// da fase 17 isso vira um bug silencioso: empurrar para o branco nunca alcança piso
        /// nenhum, todo jogo cai na cor de reserva, e o acento tirado da arte simplesmente
        /// some da tela clara sem ninguém entender por quê.
        /// </summary>
        private static void TestarPisoDeContrasteNosDoisFundos(Verificador v)
        {
            v.Escrever("");
            v.Escrever("O piso de contraste sabe para que lado ir");

            var fundoEscuro = Color.FromArgb(0x16, 0x18, 0x1D);
            var fundoClaro = Color.FromArgb(0xF2, 0xF4, 0xF8);
            var reserva = Color.FromArgb(120, 180, 255);

            var azulMarinho = Color.FromArgb(20, 28, 70);
            var ajustadoNoEscuro = CorDominante.GarantirContraste(azulMarinho, fundoEscuro, reserva);

            v.Verificar("sobre fundo escuro, cor escura é CLAREADA",
                CorDominante.Contraste(ajustadoNoEscuro, fundoEscuro) >= 3.0 &&
                ajustadoNoEscuro.GetBrightness() > azulMarinho.GetBrightness(),
                ajustadoNoEscuro.ToString());

            var amareloPalha = Color.FromArgb(245, 235, 170);
            var ajustadoNoClaro = CorDominante.GarantirContraste(amareloPalha, fundoClaro, reserva);

            v.Verificar("sobre fundo claro, cor clara é ESCURECIDA",
                CorDominante.Contraste(ajustadoNoClaro, fundoClaro) >= 3.0 &&
                ajustadoNoClaro.GetBrightness() < amareloPalha.GetBrightness(),
                ajustadoNoClaro.ToString());

            // Cor que já serve continua intacta nos dois — o piso é um piso, não um filtro.
            var laranjaVivo = Color.FromArgb(255, 140, 30);
            v.Verificar("cor que já tem contraste passa intacta no escuro",
                CorDominante.GarantirContraste(laranjaVivo, fundoEscuro, reserva) == laranjaVivo);

            var vinho = Color.FromArgb(120, 20, 40);
            v.Verificar("e no claro também",
                CorDominante.GarantirContraste(vinho, fundoClaro, reserva) == vinho);
        }

        // ---- O relatório de integridade ---------------------------------------------------

        /// <summary>
        /// Os dois testes que a spec pede: <b>o relatório conta corretamente com jogo
        /// ausente e com capa quebrada</b>, e <b>contar não conserta nada sozinho</b>.
        ///
        /// O acervo montado aqui tem os quatro estados de propósito: um jogo inteiro (que
        /// não pode aparecer em lugar nenhum), um sem executável, dois com arte prometida e
        /// não entregue, e uma pasta no HD que ninguém catalogou.
        /// </summary>
        private static void TestarRelatorioDeIntegridade(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Relatório de integridade do acervo");

            var sandbox = PrepararSandbox(raizReal);
            var config = new Config();

            var disco = new SistemaDeArquivosSimulado();
            var biblioteca = new Biblioteca();
            biblioteca.PastasEscaneadas.Add("Jogos");

            // Três jogos com executável de verdade em disco. O scanner simulado precisa
            // enxergá-los também, senão eles voltariam como "pasta nova".
            var inteiro = MontarJogo(sandbox, disco, biblioteca, "inteiro", "Jogo Inteiro");
            var semCapa = MontarJogo(sandbox, disco, biblioteca, "sem-capa", "Sem Capa");
            var semHero = MontarJogo(sandbox, disco, biblioteca, "sem-hero", "Sem Hero");

            // O jogo inteiro tem capa de verdade: ele é o controle do teste.
            inteiro.CapaArquivo = "inteiro.jpg";
            File.WriteAllBytes(Path.Combine(Caminhos.PastaCapas, "inteiro.jpg"), new byte[] { 1, 2, 3 });

            // Arte prometida e não entregue, dos dois jeitos que isso acontece: o arquivo
            // que sumiu, e o que ficou com zero byte porque o HD saiu no meio do download.
            semCapa.CapaArquivo = "fantasma.jpg";

            semHero.HeroArquivo = "vazio.jpg";
            File.WriteAllBytes(Path.Combine(Caminhos.PastaCapas, "vazio.jpg"), new byte[0]);

            // Jogo cujo executável não existe em canto nenhum.
            var sumido = new Jogo
            {
                Id = "sumido",
                Titulo = "Jogo Sumido",
                ExecutavelRelativo = @"Jogos\Sumido\jogo.exe"
            };
            biblioteca.Jogos.Add(sumido);

            // E a novidade no HD: pasta com cara de jogo que nenhum card representa.
            disco.AdicionarArquivos(Path.Combine(sandbox, "Jogos", "Novidade"),
                "novidade.exe|5.000.000");

            biblioteca.Salvar();
            var antesDoRelatorio = ArquivoTexto.Ler(Caminhos.ArquivoBiblioteca);
            var arquivosAntes = ContarArquivos(sandbox);

            var relatorio = RelatorioDeIntegridade.Montar(biblioteca, config, disco);

            v.Verificar("conta o jogo não encontrado", relatorio.JogosNaoEncontrados == 1,
                relatorio.JogosNaoEncontrados.ToString());

            v.Verificar("conta as artes quebradas (a que sumiu e a de zero byte)",
                relatorio.ArtesQuebradas == 2, relatorio.ArtesQuebradas.ToString());

            v.Verificar("conta a pasta nova", relatorio.PastasNovas == 1, relatorio.PastasNovas.ToString());

            v.Verificar("e o total bate com a soma", relatorio.Problemas.Count == 4,
                relatorio.Problemas.Count.ToString());

            v.Verificar("o jogo inteiro NÃO aparece em lugar nenhum",
                !Contem(relatorio, "inteiro"), Resumir(relatorio));

            // A pergunta aqui é só sobre as pastas novas: "Sem Capa" APARECE no relatório,
            // e tem que aparecer — como arte quebrada. O que não pode é o scanner
            // redescobrir a pasta dele como se fosse jogo que ninguém catalogou.
            v.Verificar("nenhuma pasta de jogo catalogado vira novidade",
                SoAsPastasNovas(relatorio) == "Novidade", SoAsPastasNovas(relatorio));

            v.Verificar("o resumo é a linha que a spec pede",
                relatorio.Resumo().Contains("1 jogo(s) não encontrado(s)") &&
                relatorio.Resumo().Contains("2 arte(s) quebrada(s)") &&
                relatorio.Resumo().Contains("1 pasta(s) nova(s)"),
                relatorio.Resumo());

            // Cada problema sabe em quem mexer — é o que a tela usa para a ação direta.
            var doSumido = Achar(relatorio, TipoDeProblema.JogoNaoEncontrado);
            v.Verificar("o jogo sumido vem com o jogo junto",
                doSumido is not null && ReferenceEquals(doSumido.Jogo, sumido));

            var daArte = Achar(relatorio, TipoDeProblema.ArteQuebrada);
            v.Verificar("a arte quebrada vem com o arquivo culpado",
                daArte is not null && daArte.Alvo.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase),
                daArte?.Alvo);

            var daPasta = Achar(relatorio, TipoDeProblema.PastaNova);
            v.Verificar("a pasta nova vem com caminho relativo, sem letra de drive",
                daPasta is not null && !daPasta.Alvo.Contains(":"), daPasta?.Alvo);

            // ---- E agora o que mais importa: CONTAR NÃO CONSERTA ----------------------------

            v.Escrever("");
            v.Escrever("  ...e contar não conserta nada");

            v.Verificar("a capa fantasma continua apontada na biblioteca",
                semCapa.CapaArquivo == "fantasma.jpg", semCapa.CapaArquivo);

            v.Verificar("o hero de zero byte continua apontado",
                semHero.HeroArquivo == "vazio.jpg", semHero.HeroArquivo);

            v.Verificar("o jogo sumido continua na biblioteca, com o caminho intacto",
                biblioteca.ObterPorId("sumido")?.ExecutavelRelativo == @"Jogos\Sumido\jogo.exe");

            v.Verificar("nenhum jogo entrou nem saiu", biblioteca.Jogos.Count == 4,
                biblioteca.Jogos.Count.ToString());

            v.Verificar("o biblioteca.json não foi reescrito",
                ArquivoTexto.Ler(Caminhos.ArquivoBiblioteca) == antesDoRelatorio);

            v.Verificar("nenhum arquivo foi criado nem apagado no HD",
                ContarArquivos(sandbox) == arquivosAntes,
                $"{ContarArquivos(sandbox)} (era {arquivosAntes})");

            // Rodar de novo tem que dar exatamente o mesmo resultado: é a prova de que a
            // primeira passada não mexeu em nada que a segunda fosse enxergar diferente.
            var denovo = RelatorioDeIntegridade.Montar(biblioteca, config, disco);
            v.Verificar("rodar de novo dá o mesmo relatório",
                denovo.JogosNaoEncontrados == 1 && denovo.ArtesQuebradas == 2 && denovo.PastasNovas == 1,
                denovo.Resumo());

            // Acervo em ordem: o relatório precisa saber dizer que está tudo bem, senão
            // ninguém confia nele quando ele acusa alguma coisa.
            var limpa = new Biblioteca();
            limpa.PastasEscaneadas.Add("Jogos");

            var so = MontarJogo(sandbox, disco, limpa, "unico", "Único");
            so.CapaArquivo = "inteiro.jpg";

            // O acervo limpo é o mesmo disco, então "Novidade" e os outros jogos ainda estão
            // lá: para provar o "tudo certo" basta uma biblioteca sem pasta escaneada.
            var semRaiz = new Biblioteca();
            semRaiz.Jogos.Add(so);

            var tudoCerto = RelatorioDeIntegridade.Montar(semRaiz, config, disco);
            v.Verificar("acervo em ordem não gera problema nenhum", tudoCerto.TudoCerto, Resumir(tudoCerto));

            v.Verificar("e o relatório admite que não procurou pasta nova",
                tudoCerto.PastasNaoVarridas && tudoCerto.Resumo().Contains("Não há pasta escaneada"),
                tudoCerto.Resumo());
        }

        // ---- A tela de configurações ------------------------------------------------------

        /// <summary>
        /// Os controles novos da fase 17 cabem na janela E NÃO FICAM ATRÁS DA LISTA DE PASTAS.
        ///
        /// A segunda metade é a que importa, e ela nasceu de um bug de verdade encontrado ao
        /// fotografar a tela: no WinForms, irmãos ancorados são posicionados <b>do último
        /// filho para o primeiro</b>, cada um tirando espaço do que sobrou. A lista de pastas
        /// é <c>Dock.Fill</c> e estava sendo acrescentada ANTES dos blocos de baixo, então ela
        /// engolia a área restante e os blocos "Grade" e "Limpar cache" eram desenhados com
        /// altura zero.
        ///
        /// A janela abria bonita, sem erro nenhum, e simplesmente não tinha aquelas opções —
        /// que é exatamente o tipo de coisa que nenhum teste de comportamento enxerga. Daí o
        /// teste ser de geometria, e a pergunta ser "sobrepõe?", não só "cabe?".
        /// </summary>
        private static void TestarGeometriaDasConfiguracoes(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Os controles novos aparecem na tela de configurações");

            PrepararSandbox(raizReal);

            var biblioteca = new Biblioteca();
            biblioteca.PastasEscaneadas.Add("Jogos");

            using (var janela = new FormConfiguracoes(new Config(), biblioteca))
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                var lista = Area(janela.ListaDePastas);

                Conferir(v, janela, "o seletor de tema", janela.CaixaDoTema, lista);
                Conferir(v, janela, "o campo da cor de acento", janela.CampoDoAcento, lista);
                Conferir(v, janela, "o botão da integridade", janela.BotaoDaIntegridade, lista);
                Conferir(v, janela, "a situação do cache", janela.SituacaoDoCache, lista);

                v.Verificar("o botão da integridade diz qual é a tecla dele",
                    janela.BotaoDaIntegridade?.Text.Contains("F8") == true, janela.BotaoDaIntegridade?.Text);

                v.Verificar("o combo começa no tema que está no config", janela.CaixaDoTema.SelectedIndex == 0);
                v.Verificar("e o campo da cor começa vazio", janela.CampoDoAcento.Text.Length == 0);
            }
        }

        private static void Conferir(Verificador v, Form janela, string nome, Control? controle, Rectangle lista)
        {
            if (controle is null)
            {
                v.Verificar($"{nome} existe", false);
                return;
            }

            var area = Area(controle);
            var cliente = janela.RectangleToScreen(janela.ClientRectangle);

            v.Verificar($"{nome} tem tamanho de verdade", area.Height > 4 && area.Width > 4, area.ToString());
            v.Verificar($"{nome} cabe na janela", cliente.Contains(area), $"{area} em {cliente}");
            v.Verificar($"{nome} não fica atrás da lista de pastas",
                !area.IntersectsWith(lista), $"{area} x {lista}");
        }

        private static Rectangle Area(Control controle)
            => controle.Parent?.RectangleToScreen(controle.Bounds) ?? Rectangle.Empty;

        // ---- Apoio ---------------------------------------------------------------------------

        /// <summary>
        /// Um jogo que existe dos dois lados: arquivo de verdade em disco (é o que
        /// <c>ExecutavelExiste</c> olha) e entrada no disco simulado (é o que o scanner
        /// olha). Sem os dois, o mesmo jogo apareceria como sumido ou como pasta nova.
        /// </summary>
        private static Jogo MontarJogo(string sandbox, SistemaDeArquivosSimulado disco,
                                       Biblioteca biblioteca, string id, string titulo)
        {
            var pasta = Path.Combine(sandbox, "Jogos", titulo);
            var exe = Path.Combine(pasta, $"{id}.exe");

            Directory.CreateDirectory(pasta);
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

            disco.AdicionarArquivos(pasta, $"{id}.exe|5.000.000");

            var jogo = new Jogo
            {
                Id = id,
                Titulo = titulo,
                ExecutavelRelativo = Caminhos.ParaRelativo(exe)
            };

            biblioteca.Jogos.Add(jogo);
            return jogo;
        }

        private static bool Contem(RelatorioDeIntegridade relatorio, string texto)
        {
            foreach (var problema in relatorio.Problemas)
            {
                if (problema.Descricao.IndexOf(texto, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Os títulos das pastas novas, em ordem, separados por vírgula.</summary>
        private static string SoAsPastasNovas(RelatorioDeIntegridade relatorio)
        {
            var titulos = new List<string>();

            foreach (var problema in relatorio.Problemas)
            {
                if (problema.Tipo != TipoDeProblema.PastaNova) continue;

                var descricao = problema.Descricao;
                var fim = descricao.IndexOf('"', 1);
                titulos.Add(fim > 1 ? descricao.Substring(1, fim - 1) : descricao);
            }

            return titulos.Count == 0 ? "(nenhuma)" : string.Join(", ", titulos.ToArray());
        }

        private static ProblemaDeIntegridade? Achar(RelatorioDeIntegridade relatorio, TipoDeProblema tipo)
        {
            foreach (var problema in relatorio.Problemas)
            {
                if (problema.Tipo == tipo) return problema;
            }
            return null;
        }

        private static string Resumir(RelatorioDeIntegridade relatorio)
        {
            var linhas = new List<string>();
            foreach (var problema in relatorio.Problemas) linhas.Add(problema.Descricao);

            return linhas.Count == 0 ? "(vazio)" : string.Join(" | ", linhas.ToArray());
        }

        private static int ContarArquivos(string pasta)
        {
            try
            {
                return Directory.GetFiles(pasta, "*", SearchOption.AllDirectories).Length;
            }
            catch (Exception)
            {
                return -1;
            }
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
