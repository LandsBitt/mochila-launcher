// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using Launcher.Scanner;
using Launcher.Util;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 2: tokenização, placar e os três casos reais
    /// do meu HD (NFS Most Wanted, NFS Underground 2, NFS Carbon).
    ///
    /// Roda contra listagens simuladas, então não precisa do HD plugado e não escreve
    /// nada em disco. Chamado por "Launcher.exe --autoteste".
    /// </summary>
    public static class AutoTesteScanner
    {
        /// <summary>Raiz virtual dos testes. Letra de propósito diferente da máquina real.</summary>
        private const string Raiz = @"Z:\Jogos";
        private const string Carros = Raiz + @"\Antigos\Carros";

        private const string PastaMostWanted = Carros + @"\Need For Speed Most Wanted Black Edition";
        private const string PastaUnderground2 = Carros + @"\Need for Speed - Underground 2";
        private const string PastaCarbon = Carros + @"\NFS Carbon";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);

            try
            {
                TestarTokenizacao(v);
                TestarTitulos(v);
                TestarCasosNfs(v);
                TestarVarreduraCompleta(v);
                TestarPastasDeLixo(v);
                TestarLimiteDeProfundidade(v);
                TestarUltimoRecurso(v);
                TestarSinaisDoPe(v);
                TestarEconomiaDeLeituraDePe(v);
                TestarPastasDeCategoria(v);
                TestarVetoAbsoluto(v);
                TestarRegraDeLancador(v);
                TestarListaDeExclusao(v);
                TestarRegistroDeDescartes(v);
                TestarCancelamento(v);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- Tokenização ----------------------------------------------------------------------

        private static void TestarTokenizacao(Verificador v)
        {
            v.Escrever("Tokenização");

            v.VerificarTexto("split na fronteira letra/dígito (SPEED2)",
                "speed|2", Juntar(Tokens.Dividir("SPEED2")));

            v.VerificarTexto("split em camelCase (DeadCells)",
                "dead|cells", Juntar(Tokens.Dividir("DeadCells")));

            v.VerificarTexto("split de sigla + palavra (NFSCarbon)",
                "nfs|carbon", Juntar(Tokens.Dividir("NFSCarbon")));

            v.VerificarTexto("split em espaço, _, - e .",
                "need|for|speed|underground|2", Juntar(Tokens.Dividir("Need for Speed - Underground 2")));

            v.VerificarTexto("sigla pura continua um token só",
                "nfsc", Juntar(Tokens.Dividir("NFSC")));

            v.VerificarTexto("acento sai do token",
                "coracao|de|aco", Juntar(Tokens.Dividir("Coração de Aço")));

            var significativos = Tokens.Significativos("Need For Speed Most Wanted Black Edition");
            v.Verificar("stopwords fora do conjunto significativo",
                !significativos.Contains("black") && !significativos.Contains("edition") && significativos.Contains("speed"));

            v.Verificar("subsequência: nfsc ⊂ nfscarbon", Tokens.EhSubsequencia("nfsc", "nfscarbon"));
            v.Verificar("subsequência: gtaiv ⊂ grandtheftautoiv", Tokens.EhSubsequencia("gtaiv", "grandtheftautoiv"));
            v.Verificar("subsequência recusa fora de ordem", !Tokens.EhSubsequencia("cnfs", "nfscarbon"));

            v.VerificarTexto("compactação junta os tokens", "nfscarbon", Tokens.Compactar("NFS Carbon"));
        }

        private static string Juntar(List<string> tokens) => string.Join("|", tokens.ToArray());

        // ---- Título proposto -------------------------------------------------------------------

        private static void TestarTitulos(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Título proposto a partir do nome da pasta");

            v.VerificarTexto("title case com palavra menor em minúscula",
                "Need for Speed Most Wanted Black Edition",
                TituloDePasta.Limpar("Need For Speed Most Wanted Black Edition"));

            v.VerificarTexto("hífen de subtítulo preservado",
                "Need for Speed - Underground 2",
                TituloDePasta.Limpar("Need for Speed - Underground 2"));

            v.VerificarTexto("sigla curta não vira Title Case",
                "NFS Carbon", TituloDePasta.Limpar("NFS Carbon"));

            v.VerificarTexto("tags de repack, versão e idioma somem",
                "Cuphead", TituloDePasta.Limpar("Cuphead_v1.2.3_[FitGirl]_REPACK_PT-BR"));

            v.VerificarTexto("tag (GOG) some, parêntese legítimo fica",
                "The Witcher (Enhanced Edition)", TituloDePasta.Limpar("The Witcher (Enhanced Edition) (GOG)"));

            v.VerificarTexto("ponto vira espaço",
                "Resident Evil 4", TituloDePasta.Limpar("resident.evil.4"));

            v.VerificarTexto("pasta gritada volta ao normal",
                "Grand Theft Auto III", TituloDePasta.Limpar("GRAND THEFT AUTO III"));

            v.VerificarTexto("nome que sumiria na limpeza volta cru",
                "v1.2", TituloDePasta.Limpar("v1.2"));
        }

        // ---- Os três casos obrigatórios ----------------------------------------------------------

        private static void TestarCasosNfs(Verificador v)
        {
            var fs = MontarHdDeExemplo();
            var scanner = new ScannerDeJogos(fs);

            // Caso 1 -------------------------------------------------------------------------------
            v.Escrever("");
            v.Escrever(@"Caso 1 — Need For Speed Most Wanted Black Edition\  (esperado: speed.exe)");

            var caso1 = scanner.AnalisarPastaDeJogo(PastaMostWanted);
            if (caso1 is null)
            {
                v.RegistrarErro("  FALHA nenhum candidato encontrado no caso 1");
            }
            else
            {
                v.VerificarTexto("escolheu o executável certo", "speed.exe", caso1.Escolhido.NomeDoArquivo);
                v.Verificar("placar alto o bastante para não ser baixa confiança",
                    !caso1.BaixaConfianca, caso1.Escolhido.Placar.ToString());
                v.Verificar("dominância por tamanho contou",
                    caso1.Escolhido.PontosDe(RegraPlacar.MaiorExecutavel) == 40);
                v.Verificar("similaridade de token contou (speed)",
                    caso1.Escolhido.PontosDe(RegraPlacar.TokensEmComum) >= 25);
                v.Verificar("safemode_inst.exe vetado pelo sufixo _inst",
                    Candidato(caso1, "safemode_inst.exe")?.Excluido == true);
                v.Verificar("shell_inst.exe vetado pelo sufixo _inst",
                    Candidato(caso1, "shell_inst.exe")?.Excluido == true);
                v.Verificar("nenhuma DLL virou candidata", !TemCandidatoComExtensao(caso1, ".dll"));
                v.VerificarTexto("título proposto",
                    "Need for Speed Most Wanted Black Edition", caso1.TituloProposto);
            }

            // Caso 2 -------------------------------------------------------------------------------
            v.Escrever("");
            v.Escrever(@"Caso 2 — Need for Speed - Underground 2\  (esperado: SPEED2.EXE)");

            var caso2 = scanner.AnalisarPastaDeJogo(PastaUnderground2);
            if (caso2 is null)
            {
                v.RegistrarErro("  FALHA nenhum candidato encontrado no caso 2");
            }
            else
            {
                v.VerificarTexto("escolheu o executável certo", "SPEED2.EXE", caso2.Escolhido.NomeDoArquivo);
                v.Verificar("não é baixa confiança", !caso2.BaixaConfianca, caso2.Escolhido.Placar.ToString());

                // A spec é explícita: se este teste passar sem os pontos de similaridade,
                // a tokenização está errada mesmo que o resultado final acerte.
                var similaridade = caso2.Escolhido.PontosDe(RegraPlacar.TokensEmComum);
                v.Verificar("SPEED2 ganhou pontos de similaridade (split letra/dígito funcionou)",
                    similaridade >= 50, similaridade.ToString());

                v.Verificar("unins000.exe vetado pelo prefixo unins",
                    Candidato(caso2, "unins000.exe")?.Excluido == true);
                v.Verificar("SafeMode.bat fora da lista (há .exe com placar positivo)",
                    Candidato(caso2, "SafeMode.bat") is null);
                v.Verificar("o .ico não virou candidato", !TemCandidatoComExtensao(caso2, ".ico"));
            }

            // Caso 3 -------------------------------------------------------------------------------
            v.Escrever("");
            v.Escrever(@"Caso 3 — NFS Carbon\  (esperado: NFSC.exe)");

            var caso3 = scanner.AnalisarPastaDeJogo(PastaCarbon);
            if (caso3 is null)
            {
                v.RegistrarErro("  FALHA nenhum candidato encontrado no caso 3");
            }
            else
            {
                v.VerificarTexto("escolheu o executável certo", "NFSC.exe", caso3.Escolhido.NomeDoArquivo);
                v.Verificar("não é baixa confiança", !caso3.BaixaConfianca, caso3.Escolhido.Placar.ToString());
                v.Verificar("bônus de sigla contou (nfsc ⊂ nfscarbon)",
                    caso3.Escolhido.PontosDe(RegraPlacar.SiglaDoNomeDaPasta) == 10);
                v.Verificar("EAInstall.dll não entrou na lista de candidatos",
                    Candidato(caso3, "EAInstall.dll") is null);
                v.Verificar("único candidato da pasta", caso3.Candidatos.Count == 1,
                    caso3.Candidatos.Count.ToString());
            }
        }

        /// <summary>
        /// Varre a raiz inteira de uma vez: as três pastas são irmãs, da mesma engine, com
        /// os mesmos arquivos-satélite (server.dll, dinput8.dll, filelist.txt, 00000000.256).
        /// O scanner tem que tratar cada uma isoladamente.
        /// </summary>
        private static void TestarVarreduraCompleta(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Varredura da raiz inteira");

            var scanner = new ScannerDeJogos(MontarHdDeExemplo());
            var jogos = scanner.Escanear(new[] { Raiz });

            v.Verificar("achou os 3 jogos", jogos.Count == 3, jogos.Count.ToString());

            v.VerificarTexto("Most Wanted resolvido isoladamente",
                "speed.exe", ExecutavelDe(jogos, "Need For Speed Most Wanted Black Edition"));
            v.VerificarTexto("Underground 2 resolvido isoladamente",
                "SPEED2.EXE", ExecutavelDe(jogos, "Need for Speed - Underground 2"));
            v.VerificarTexto("Carbon resolvido isoladamente",
                "NFSC.exe", ExecutavelDe(jogos, "NFS Carbon"));

            v.Verificar("pastas de categoria não viraram jogo",
                jogos.TrueForAll(j => j.NomeDaPasta != "Carros" && j.NomeDaPasta != "Antigos"));

            v.Verificar("nenhum jogo saiu com baixa confiança", jogos.TrueForAll(j => !j.BaixaConfianca));

            // Duas raízes aninhadas não podem produzir o mesmo jogo duas vezes.
            var comRaizesRepetidas = scanner.Escanear(new[] { Raiz, Raiz + @"\Antigos" });
            v.Verificar("raízes aninhadas não duplicam jogos",
                comRaizesRepetidas.Count == 3, comRaizesRepetidas.Count.ToString());
        }

        // ---- Regras isoladas ---------------------------------------------------------------------

        private static void TestarPastasDeLixo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Pastas de lixo (redist, uninstall, docs...)");

            const string pasta = @"Z:\Jogos\Meu Jogo";
            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pasta, "meujogo.exe|3.000.000")
                .AdicionarArquivos(pasta + @"\_CommonRedist", "vcredist_x86.exe|9.000.000")
                .AdicionarArquivos(pasta + @"\_CommonRedist\vcredist", "bem_escondido.exe|9.000.000")
                .AdicionarArquivos(pasta + @"\Uninstall", "unins000.exe|2.000.000")
                .AdicionarArquivos(pasta + @"\Uninstall\dados", "escondido.exe|9.000.000")
                .AdicionarArquivos(pasta + @"\Tools", "editor.exe|8.000.000");

            var jogo = new ScannerDeJogos(fs).AnalisarPastaDeJogo(pasta);
            if (jogo is null)
            {
                v.RegistrarErro("  FALHA pasta com executável não produziu jogo");
                return;
            }

            v.VerificarTexto("o executável da raiz ganha dos das pastas de lixo",
                "meujogo.exe", jogo.Escolhido.NomeDoArquivo);

            v.Verificar("executável em _CommonRedist levou -80",
                Candidato(jogo, "vcredist_x86.exe")?.PontosDe(RegraPlacar.PastaIrrelevante) == -80);
            v.Verificar("executável em Tools levou -80",
                Candidato(jogo, "editor.exe")?.PontosDe(RegraPlacar.PastaIrrelevante) == -80);
            v.Verificar("o scanner não desce para dentro de Uninstall\\",
                Candidato(jogo, "escondido.exe") is null);
            v.Verificar("o scanner não desce para dentro de _CommonRedist\\",
                Candidato(jogo, "bem_escondido.exe") is null);
            v.Verificar("mas o unins000.exe da própria Uninstall\\ aparece como candidato punido",
                Candidato(jogo, "unins000.exe")?.Placar < 0);
        }

        private static void TestarLimiteDeProfundidade(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Limite de profundidade da busca");

            const string pasta = @"Z:\Jogos\Jogo Fundo";
            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pasta, "jogofundo.exe|3.000.000")
                .AdicionarArquivos(pasta + @"\a\b\c\d", "quatroniveis.exe|4.000.000")
                .AdicionarArquivos(pasta + @"\a\b\c\d\e", "cinconiveis.exe|9.000.000");

            var jogo = new ScannerDeJogos(fs).AnalisarPastaDeJogo(pasta);
            if (jogo is null)
            {
                v.RegistrarErro("  FALHA pasta com executável não produziu jogo");
                return;
            }

            v.Verificar("executável a 4 níveis entra na lista", Candidato(jogo, "quatroniveis.exe") is not null);
            v.Verificar("executável a 5 níveis fica de fora", Candidato(jogo, "cinconiveis.exe") is null);
            v.VerificarTexto("a raiz continua vencendo mesmo contra um exe maior",
                "jogofundo.exe", jogo.Escolhido.NomeDoArquivo);
        }

        private static void TestarUltimoRecurso(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Candidatos de último recurso (.bat/.cmd/.lnk)");

            const string pastaBat = @"Z:\Jogos\Emulado Antigo";
            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaBat, "emulado antigo.bat|420", "leiame.txt|900");

            var jogo = new ScannerDeJogos(fs).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("pasta só com .bat ainda vira jogo", jogo.Count == 1, jogo.Count.ToString());
            if (jogo.Count == 1)
            {
                v.VerificarTexto("o .bat foi aceito como ponto de entrada",
                    "emulado antigo.bat", jogo[0].Escolhido.NomeDoArquivo);
                v.Verificar("candidato de último recurso está marcado", !jogo[0].Escolhido.EhExe);
            }

            // Com um .exe válido na pasta, o .bat não deve nem aparecer.
            const string pastaMista = @"Z:\Jogos\Jogo Misto";
            var fs2 = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaMista, "jogo misto.exe|5.000.000", "iniciar.bat|120");

            var misto = new ScannerDeJogos(fs2).AnalisarPastaDeJogo(pastaMista);
            v.Verificar("com .exe positivo, o .bat sai da lista",
                misto is not null && Candidato(misto, "iniciar.bat") is null);

            // SafeMode.bat continua descartado mesmo sendo o único ponto de entrada.
            const string pastaSafe = @"Z:\Jogos\Jogo Portatil";
            var fs3 = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaSafe, "SafeMode.bat|13");

            var safe = new ScannerDeJogos(fs3).AnalisarPastaDeJogo(pastaSafe);
            v.Verificar("SafeMode.bat fica com placar negativo",
                safe is not null && safe.Escolhido.Placar < 0, safe?.Escolhido.Placar.ToString());
        }

        private static void TestarSinaisDoPe(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Sinais lidos do PE");

            // Cada sinal numa pasta pequena: com o limite de 3 leituras por pasta, um teste
            // com 5 concorrentes não conseguiria afirmar nada sobre o 4º e o 5º.
            v.Verificar("subsistema GUI dá +10",
                PontosDePe(v, "grafico.exe", new InfoExecutavel { Subsistema = SubsistemaPe.Gui },
                    RegraPlacar.SubsistemaGui) == 10);

            v.Verificar("subsistema console dá -20",
                PontosDePe(v, "console.exe", new InfoExecutavel { Subsistema = SubsistemaPe.Console },
                    RegraPlacar.SubsistemaConsole) == -20);

            v.Verificar("requireAdministrator dá -50",
                PontosDePe(v, "elevado.exe",
                    new InfoExecutavel { Subsistema = SubsistemaPe.Gui, PedeAdministrador = true },
                    RegraPlacar.PedeAdministrador) == -50);

            v.Verificar("metadado parecido com a pasta dá +15",
                PontosDePe(v, "semnome.exe",
                    new InfoExecutavel { Subsistema = SubsistemaPe.Gui, FileDescription = "Carbon Racing Game" },
                    RegraPlacar.MetadadosParecidos) == 15);

            v.Verificar("exe sem PE legível não ganha nem perde por isso",
                PontosDePe(v, "sempe.exe", null, RegraPlacar.SubsistemaGui) == 0);
        }

        /// <summary>
        /// Monta uma pasta com o exe sob teste e um vizinho de placar igual, e devolve os
        /// pontos que a regra rendeu. O vizinho existe porque o scanner não abre o PE
        /// quando há um só candidato — sem disputa, a leitura seria I/O desperdiçado.
        /// </summary>
        private static int PontosDePe(Verificador v, string nomeDoExe, InfoExecutavel? info, RegraPlacar regra)
        {
            const string pasta = @"Z:\Jogos\NFS Carbon Teste";

            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivo(pasta + "\\" + nomeDoExe, 3_000_000, info)
                .AdicionarArquivo(pasta + @"\vizinho.exe", 3_000_000);

            var jogo = new ScannerDeJogos(fs).AnalisarPastaDeJogo(pasta);
            if (jogo is null)
            {
                v.RegistrarErro($"  FALHA pasta de teste do PE ({nomeDoExe}) não produziu jogo");
                return int.MinValue;
            }

            var candidato = Candidato(jogo, nomeDoExe);
            if (candidato is null)
            {
                v.RegistrarErro($"  FALHA {nomeDoExe} não entrou na lista de candidatos");
                return int.MinValue;
            }
            return candidato.PontosDe(regra);
        }

        /// <summary>
        /// O I/O de PE era 99% do tempo de scan no HD externo com cache frio. Duas regras
        /// seguram isso: nada de ler PE de candidato vetado, e no máximo 3 leituras por pasta.
        /// </summary>
        private static void TestarEconomiaDeLeituraDePe(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Economia de leitura de PE");

            // Cinco candidatos empatados: todos dentro da janela, então o teto de 3 é que manda.
            const string pasta = @"Z:\Jogos\Jogo Com Muitos Exes";
            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pasta,
                    "alfa.exe|9.000.000",
                    "beta.exe|9.000.000",
                    "gama.exe|9.000.000",
                    "delta.exe|9.000.000",
                    "epsilon.exe|9.000.000");

            var jogo = new ScannerDeJogos(fs).AnalisarPastaDeJogo(pasta);

            v.Verificar("no máximo 3 leituras de PE por pasta",
                fs.LeiturasDePe.Count == 3, fs.LeiturasDePe.Count.ToString());
            v.Verificar("o escolhido está entre os lidos",
                jogo is not null && fs.LeiturasDePe.Contains(jogo.Escolhido.Caminho));

            // Candidato sozinho: não há o que decidir, então nada é aberto.
            const string pastaComLixo = @"Z:\Jogos\Jogo Com Lixo";
            var fsLixo = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaComLixo,
                    "jogo com lixo.exe|9.000.000",
                    "setup.exe|900.000.000",
                    "unins000.exe|800.000");

            var comLixo = new ScannerDeJogos(fsLixo).AnalisarPastaDeJogo(pastaComLixo);

            v.Verificar("candidato único não custa nenhuma leitura de PE",
                fsLixo.LeiturasDePe.Count == 0, fsLixo.LeiturasDePe.Count.ToString());
            v.VerificarTexto("e ainda assim escolhe certo",
                "jogo com lixo.exe", comLixo?.Escolhido.NomeDoArquivo);

            // Segundo colocado longe demais: o PE não inverteria nada, então não é lido.
            const string pastaFolgada = @"Z:\Jogos\Jogo Folgado";
            var fsFolgado = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaFolgada, "jogo folgado.exe|9.000.000")
                .AdicionarArquivos(pastaFolgada + @"\Extras", "brinde.exe|8.000.000");

            new ScannerDeJogos(fsFolgado).AnalisarPastaDeJogo(pastaFolgada);
            v.Verificar("vantagem fora da janela de influência dispensa a leitura",
                fsFolgado.LeiturasDePe.Count == 0, fsFolgado.LeiturasDePe.Count.ToString());

            // Disputa apertada: aí sim vale abrir.
            const string pastaApertada = @"Z:\Jogos\Jogo Apertado";
            var fsApertado = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaApertada, "jogo apertado.exe|9.000.000", "outro jogo.exe|9.000.000");

            new ScannerDeJogos(fsApertado).AnalisarPastaDeJogo(pastaApertada);
            v.Verificar("disputa apertada abre os candidatos",
                fsApertado.LeiturasDePe.Count == 2, fsApertado.LeiturasDePe.Count.ToString());

            // O acervo real: as três pastas de NFS têm um vencedor folgado cada.
            var fsNfs = MontarHdDeExemplo();
            new ScannerDeJogos(fsNfs).Escanear(new[] { Raiz });

            v.Verificar("o acervo de 3 jogos não abre executável nenhum",
                fsNfs.LeiturasDePe.Count == 0, fsNfs.LeiturasDePe.Count.ToString());
        }

        private static void TestarPastasDeCategoria(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Pasta de categoria x pasta de jogo");

            // Invólucro: pasta sem arquivo nenhum e com um único jogo dentro.
            var fsInvolucro = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Estrategia\Age Of Empires", "empires.exe|4.000.000");

            var achados = new ScannerDeJogos(fsInvolucro).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("invólucro sem arquivos cede a vez ao jogo de dentro",
                achados.Count == 1 && achados[0].NomeDaPasta == "Age Of Empires",
                achados.Count == 1 ? achados[0].NomeDaPasta : $"{achados.Count} jogos");

            // Jogo de verdade cujo executável mora numa subpasta, com arquivos soltos na raiz.
            var fsSubpasta = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Meu Jogo Antigo", "leiame.txt|300", "dados.pak|900.000")
                .AdicionarArquivos(@"Z:\Jogos\Meu Jogo Antigo\System", "meu jogo antigo.exe|4.000.000");

            var comSubpasta = new ScannerDeJogos(fsSubpasta).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("jogo com exe em subpasta mantém o nome da pasta de cima",
                comSubpasta.Count == 1 && comSubpasta[0].NomeDaPasta == "Meu Jogo Antigo",
                comSubpasta.Count == 1 ? comSubpasta[0].NomeDaPasta : $"{comSubpasta.Count} jogos");

            // Caso real do meu HD: E:\Jogos\Antigos tinha um dxwebsetup.exe solto na raiz,
            // e isso fazia a prateleira inteira ser confundida com um único jogo.
            var fsPrateleiraComLixo = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Antigos", "dxwebsetup.exe|287.000")
                .AdicionarArquivos(@"Z:\Jogos\Antigos\Carros\Jogo Um", "jogo um.exe|4.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Antigos\Carros\Jogo Dois", "jogo dois.exe|4.000.000");

            var prateleira = new ScannerDeJogos(fsPrateleiraComLixo).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("instalador solto não transforma a prateleira num jogo",
                prateleira.Count == 2, $"{prateleira.Count} jogo(s): {NomesDePasta(prateleira)}");

            // O mesmo, com um jogo só embaixo: o instalador solto não pode dar à prateleira
            // o direito de virar o jogo.
            var fsPrateleiraSolitaria = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Antigos", "dxwebsetup.exe|287.000")
                .AdicionarArquivos(@"Z:\Jogos\Antigos\Jogo Sozinho", "jogo sozinho.exe|4.000.000");

            var solitaria = new ScannerDeJogos(fsPrateleiraSolitaria).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("prateleira com um jogo só e um instalador solto cede a vez",
                solitaria.Count == 1 && solitaria[0].NomeDaPasta == "Jogo Sozinho",
                NomesDePasta(solitaria));

            // Pasta de binários conhecida marca a pasta de cima como o jogo.
            var fsBinarios = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Jogo Unreal\Binaries\Win64", "JogoUnreal-Win64-Shipping.exe|40.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Jogo Unreal\Engine\Extras", "CrashReportClient.exe|9.000.000");

            var unreal = new ScannerDeJogos(fsBinarios).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("pasta com Binaries\\Win64 é o jogo",
                unreal.Count == 1 && unreal[0].NomeDaPasta == "Jogo Unreal",
                unreal.Count == 1 ? unreal[0].NomeDaPasta : $"{unreal.Count} jogos");

            if (unreal.Count == 1)
            {
                v.VerificarTexto("o shipping de Binaries\\Win64 vence o CrashReportClient",
                    "JogoUnreal-Win64-Shipping.exe", unreal[0].Escolhido.NomeDoArquivo);
                v.Verificar("bônus de pasta de binários aplicado",
                    unreal[0].Escolhido.PontosDe(RegraPlacar.PastaDeBinarios) == 12);
            }
        }

        /// <summary>
        /// Instalador é veto, não penalidade: num HD de repacks ele carrega o nome do jogo
        /// e, quando valia -60, a similaridade de tokens (+75) o resgatava.
        /// </summary>
        private static void TestarVetoAbsoluto(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Veto absoluto de instalador/redistribuível");

            const string pasta = @"Z:\Jogos\Need For Speed Most Wanted";
            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pasta, "NeedForSpeed_Setup.exe|500.000")
                .AdicionarArquivos(pasta + @"\Bin", "speed.exe|6.029.312");

            var jogo = new ScannerDeJogos(fs).AnalisarPastaDeJogo(pasta);
            if (jogo is null)
            {
                v.RegistrarErro("  FALHA a pasta não produziu jogo");
                return;
            }

            v.VerificarTexto("o instalador com nome do jogo não vence o executável real",
                "speed.exe", jogo.Escolhido.NomeDoArquivo);

            var instalador = Candidato(jogo, "NeedForSpeed_Setup.exe");
            v.Verificar("o instalador está marcado como excluído", instalador?.Excluido == true);
            v.Verificar("continua visível na revisão manual, para eu poder forçar",
                instalador is not null);
            v.Verificar("excluído fica no fim da lista, nunca em primeiro",
                jogo.Candidatos[jogo.Candidatos.Count - 1].Excluido);
            v.Verificar("nenhum bônus resgata o excluído — ele ganhou pontos e mesmo assim perdeu",
                instalador is not null && instalador.PontosDe(RegraPlacar.TokensEmComum) > 0);
            v.Verificar("instalador não rouba o bônus de maior executável",
                jogo.Escolhido.PontosDe(RegraPlacar.MaiorExecutavel) == 40);

            // As variações de escrita têm que cair todas no mesmo veto.
            foreach (var nome in new[]
                     {
                         "NeedForSpeed_Setup.exe", "needforspeed-setup.exe", "NEEDFORSPEEDSETUP.EXE",
                         "unins000.exe", "Uninstall.exe", "GameInstaller.exe", "safemode_inst.exe",
                         "vcredist_x64.exe", "DXSETUP.exe", "dotnetfx35.exe", "oalinst.exe",
                         "CrashHandler.exe", "crash_reporter.exe", "BugReport.exe"
                     })
            {
                v.Verificar($"vetado: {nome}", Pontuador.NomeEhDescartavel(nome));
            }

            // E o que não é lixo tem que continuar passando.
            foreach (var nome in new[] { "speed.exe", "NFSC.exe", "SPEED2.exe", "Instinct.exe", "Constantine.exe" })
                v.Verificar($"não vetado: {nome}", !Pontuador.NomeEhDescartavel(nome));

            // Pasta que só tem instalador não é jogo nenhum.
            const string soInstalador = @"Z:\Jogos\Pasta De Instaladores";
            var fsSoInstalador = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(soInstalador, "setup.exe|900.000.000", "vcredist_x86.exe|9.000.000");

            v.Verificar("pasta só com instalador não vira jogo",
                new ScannerDeJogos(fsSoInstalador).AnalisarPastaDeJogo(soInstalador) is null);
        }

        /// <summary>
        /// Launcher deixou de ser penalidade cega. Os dois lados precisam funcionar:
        /// redundante (game.exe + game_launcher.exe na mesma pasta) perde pontos;
        /// launcher sozinho na raiz com o jogo em subpasta é o ponto de entrada.
        /// </summary>
        private static void TestarRegraDeLancador(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Launcher: penalidade só quando redundante");

            // Lado 1 — redundante.
            const string pastaRedundante = @"Z:\Jogos\Jogo Redundante";
            var fsRedundante = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaRedundante, "game.exe|8.000.000", "game_launcher.exe|1.200.000");

            var redundante = new ScannerDeJogos(fsRedundante).AnalisarPastaDeJogo(pastaRedundante);
            if (redundante is null)
            {
                v.RegistrarErro("  FALHA pasta redundante não produziu jogo");
            }
            else
            {
                v.VerificarTexto("com o jogo na mesma pasta, o game.exe vence",
                    "game.exe", redundante.Escolhido.NomeDoArquivo);
                v.Verificar("o launcher redundante levou -15",
                    Candidato(redundante, "game_launcher.exe")?.PontosDe(RegraPlacar.NomeDeLauncherOuUpdater) == -15);
                v.Verificar("o executável do jogo não foi penalizado",
                    Candidato(redundante, "game.exe")?.PontosDe(RegraPlacar.NomeDeLauncherOuUpdater) == 0);
            }

            // Lado 2 — GOG: Launcher.exe na raiz, jogo em subpasta.
            const string pastaGog = @"Z:\Jogos\Jogo GOG";
            var fsGog = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaGog, "Launcher.exe|2.000.000")
                .AdicionarArquivos(pastaGog + @"\Bin", "jogo gog.exe|30.000.000");

            var gog = new ScannerDeJogos(fsGog).AnalisarPastaDeJogo(pastaGog);
            if (gog is null)
            {
                v.RegistrarErro("  FALHA pasta GOG não produziu jogo");
            }
            else
            {
                v.Verificar("launcher não redundante não é penalizado",
                    Candidato(gog, "Launcher.exe")?.PontosDe(RegraPlacar.NomeDeLauncherOuUpdater) == 0);
                v.VerificarTexto("com o jogo em subpasta, o launcher é o ponto de entrada",
                    "Launcher.exe", gog.Escolhido.NomeDoArquivo);
                v.Verificar("a escolha do launcher está explicada", gog.Escolhido.Promovido);
                v.Verificar("o executável de dentro continua na lista, para eu poder trocar",
                    Candidato(gog, "jogo gog.exe") is not null);
            }

            // Bootstrapper que prepara o DirectX antes de subir o jogo.
            const string pastaBootstrap = @"Z:\Jogos\Jogo Bootstrap";
            var fsBootstrap = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaBootstrap, "Bootstrapper.exe|900.000")
                .AdicionarArquivos(pastaBootstrap + @"\game", "jogo bootstrap.exe|20.000.000");

            var bootstrap = new ScannerDeJogos(fsBootstrap).AnalisarPastaDeJogo(pastaBootstrap);
            v.Verificar("bootstrapper na raiz também é ponto de entrada",
                bootstrap is not null && bootstrap.Escolhido.NomeDoArquivo == "Bootstrapper.exe",
                bootstrap?.Escolhido.NomeDoArquivo);

            // RPG Maker antigo: Game.exe + launcher de configuração na mesma pasta.
            const string pastaRpg = @"Z:\Jogos\RPG Antigo";
            var fsRpg = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaRpg, "Game.exe|1.400.000", "Launcher.exe|300.000");

            var rpg = new ScannerDeJogos(fsRpg).AnalisarPastaDeJogo(pastaRpg);
            v.Verificar("no RPG Maker o Game.exe ganha do launcher de configuração",
                rpg is not null && rpg.Escolhido.NomeDoArquivo == "Game.exe", rpg?.Escolhido.NomeDoArquivo);

            // Dois launchers na raiz: não dá para saber qual é o certo, então ninguém é
            // promovido — vale a pontuação normal e a revisão manual decide.
            const string pastaDoisLancadores = @"Z:\Jogos\Jogo Dois Lancadores";
            var fsDois = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaDoisLancadores, "Launcher.exe|2.000.000", "Start.exe|1.800.000")
                .AdicionarArquivos(pastaDoisLancadores + @"\Bin", "jogo dois lancadores.exe|30.000.000");

            var dois = new ScannerDeJogos(fsDois).AnalisarPastaDeJogo(pastaDoisLancadores);
            if (dois is null)
            {
                v.RegistrarErro("  FALHA pasta com dois launchers não produziu jogo");
            }
            else
            {
                v.Verificar("com dois launchers na raiz, ninguém é promovido",
                    dois.Candidatos.TrueForAll(c => !c.Promovido));
                v.Verificar("nenhum dos dois launchers é penalizado (não são redundantes)",
                    Candidato(dois, "Launcher.exe")?.PontosDe(RegraPlacar.NomeDeLauncherOuUpdater) == 0 &&
                    Candidato(dois, "Start.exe")?.PontosDe(RegraPlacar.NomeDeLauncherOuUpdater) == 0);
                v.VerificarTexto("sem promoção, o placar normal decide",
                    "jogo dois lancadores.exe", dois.Escolhido.NomeDoArquivo);
                v.Verificar("os dois launchers seguem na lista para eu escolher na revisão",
                    Candidato(dois, "Launcher.exe") is not null && Candidato(dois, "Start.exe") is not null);
            }

            // Updater nunca é promovido, mesmo sozinho na raiz.
            const string pastaUpdater = @"Z:\Jogos\Jogo Com Updater";
            var fsUpdater = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(pastaUpdater, "Updater.exe|800.000")
                .AdicionarArquivos(pastaUpdater + @"\Bin", "jogo com updater.exe|20.000.000");

            var updater = new ScannerDeJogos(fsUpdater).AnalisarPastaDeJogo(pastaUpdater);
            v.Verificar("updater não é promovido a ponto de entrada",
                updater is not null && updater.Escolhido.NomeDoArquivo == "jogo com updater.exe",
                updater?.Escolhido.NomeDoArquivo);
        }

        private static void TestarListaDeExclusao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Lista de exclusão do config.json");

            var padrao = FiltroDeExclusao.Padrao;
            v.Verificar("Riot Games ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\Riot Games"));
            v.Verificar("League of Legends ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\League of Legends"));
            v.Verificar("VALORANT ignorada por padrão (sem ligar para caixa)", padrao.PastaIgnorada(@"Z:\Jogos\Valorant"));
            v.Verificar("RiotClientElectron ignorada por padrão", padrao.PastaIgnorada(@"Z:\x\RiotClientElectron"));
            v.Verificar("RiotClientServices.exe ignorado por padrão", padrao.ExecutavelIgnorado(@"Z:\x\RiotClientServices.exe"));
            v.Verificar("LeagueClient.exe ignorado por padrão", padrao.ExecutavelIgnorado(@"Z:\x\LeagueClient.exe"));
            v.Verificar("pasta comum não é ignorada", !padrao.PastaIgnorada(@"Z:\Jogos\NFS Carbon"));
            v.Verificar("executável comum não é ignorado", !padrao.ExecutavelIgnorado(@"Z:\Jogos\NFS Carbon\NFSC.exe"));

            // As demais plataformas com launcher e atualizador próprios. Mesma lógica da
            // Riot: dependem de instalação e login, não rodam do HD em outro PC.
            v.Verificar("Epic Games ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\Epic Games"));
            v.Verificar("Steam ignorada por padrão", padrao.PastaIgnorada(@"Z:\Steam"));
            v.Verificar("steamapps ignorada por padrão", padrao.PastaIgnorada(@"Z:\Steam\steamapps"));
            v.Verificar("Battle.net ignorada por padrão (o ponto é literal, não curinga)",
                padrao.PastaIgnorada(@"Z:\Jogos\Battle.net"));
            v.Verificar("Ubisoft ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\Ubisoft"));
            v.Verificar("Ubisoft Game Launcher ignorada por padrão",
                padrao.PastaIgnorada(@"Z:\Jogos\Ubisoft Game Launcher"));
            v.Verificar("EA Games ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\EA Games"));
            v.Verificar("Origin ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\Origin"));
            v.Verificar("GOG Galaxy ignorada por padrão", padrao.PastaIgnorada(@"Z:\Jogos\GOG Galaxy"));

            // "Battle.net" tem ponto: se alguém tratasse a entrada como regex crua, o "."
            // casaria com qualquer caractere e "BattleXnet" seria ignorada junto.
            v.Verificar("o ponto de Battle.net não vira curinga", !padrao.PastaIgnorada(@"Z:\Jogos\BattleXnet"));

            // Jogo de verdade com nome parecido não pode ser levado junto.
            v.Verificar("\"Steamworld Dig\" não é a pasta do Steam", !padrao.PastaIgnorada(@"Z:\Jogos\Steamworld Dig"));
            v.Verificar("\"Origins\" não é a pasta do Origin",
                !padrao.PastaIgnorada(@"Z:\Jogos\Assassins Creed Origins"));

            var comCuringa = new FiltroDeExclusao(new[] { "Riot*" }, new[] { "*Client*" });
            v.Verificar("curinga em pasta funciona", comCuringa.PastaIgnorada(@"Z:\x\RiotGamesQualquerCoisa"));
            v.Verificar("curinga em executável funciona", comCuringa.ExecutavelIgnorado(@"Z:\x\MeuClientAqui.exe"));
            v.Verificar("curinga não vira coringa geral", !comCuringa.PastaIgnorada(@"Z:\x\Carros"));

            // A árvore completa da Riot não pode sobrar nada: nem jogo, nem candidato.
            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Riot Games\League of Legends\Game", "League of Legends.exe|20.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Riot Games\Riot Client\RiotClientElectron", "RiotClientServices.exe|9.000.000")
                .AdicionarArquivos(@"Z:\Jogos\NFS Carbon", "NFSC.exe|7.217.152");

            var scanner = new ScannerDeJogos(fs);
            var jogos = scanner.Escanear(new[] { @"Z:\Jogos" });

            v.Verificar("nada da Riot entra na biblioteca",
                jogos.Count == 1 && jogos[0].NomeDaPasta == "NFS Carbon", NomesDePasta(jogos));

            v.Verificar("a pasta da Riot foi registrada como descartada",
                FoiDescartada(scanner, "Riot Games"));

            // Com a lista vazia (eu editei o config.json), a Riot volta a aparecer.
            var semFiltro = new ScannerDeJogos(fs, FiltroDeExclusao.Nenhum).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("lista vazia no config faz a Riot voltar (a lista manda, não o código)",
                semFiltro.Count > 1, NomesDePasta(semFiltro));

            // O HD de verdade, com as plataformas todas misturadas ao acervo solto.
            var comPlataformas = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Steam\steamapps\common\Portal 2\bin", "portal2.exe|30.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Epic Games\Fortnite\FortniteGame\Binaries\Win64", "FortniteClient-Win64-Shipping.exe|80.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Battle.net\Diablo III", "Diablo III.exe|40.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Ubisoft\Ubisoft Game Launcher", "UbisoftGameLauncher.exe|5.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Origin\FIFA 21", "FIFA21.exe|60.000.000")
                .AdicionarArquivos(@"Z:\Jogos\GOG Galaxy\Games\Gwent", "Gwent.exe|20.000.000")
                .AdicionarArquivos(@"Z:\Jogos\NFS Carbon", "NFSC.exe|7.217.152")
                .AdicionarArquivos(@"Z:\Jogos\Dead Cells", "deadcells.exe|15.000.000");

            var scanDeHd = new ScannerDeJogos(comPlataformas);
            var achados = scanDeHd.Escanear(new[] { @"Z:\Jogos" });

            v.Verificar("das plataformas todas, só o acervo solto entra",
                achados.Count == 2, NomesDePasta(achados));

            foreach (var plataforma in new[] { "Steam", "Epic Games", "Battle.net", "Ubisoft", "Origin", "GOG Galaxy" })
            {
                v.Verificar($"{plataforma} descartada com motivo registrado",
                    FoiDescartada(scanDeHd, plataforma));
            }

            // O indie com launcher próprio continua entrando: ele é um exe na pasta dele,
            // roda do HD, e é para ele que a adoção de processo-filho da fase 5 existe.
            var indieComLauncher = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Meu Indie", "MeuIndieLauncher.exe|2.000.000", "MeuIndie.exe|40.000.000");

            var indies = new ScannerDeJogos(indieComLauncher).Escanear(new[] { @"Z:\Jogos" });
            v.Verificar("indie com launcher próprio continua sendo catalogado",
                indies.Count == 1 && indies[0].NomeDaPasta == "Meu Indie", NomesDePasta(indies));
        }

        private static void TestarRegistroDeDescartes(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Registro de pastas descartadas");

            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivos(@"Z:\Jogos\Jogo Bom", "jogo bom.exe|5.000.000")
                .AdicionarArquivos(@"Z:\Jogos\Só Documentos", "manual.pdf|900.000")
                .AdicionarArquivos(@"Z:\Jogos\Só Instalador", "setup.exe|900.000.000");

            var scanner = new ScannerDeJogos(fs);
            var jogos = scanner.Escanear(new[] { @"Z:\Jogos" });

            v.Verificar("só o jogo bom entra", jogos.Count == 1, NomesDePasta(jogos));
            v.Verificar("pasta sem executável foi registrada", FoiDescartada(scanner, "Só Documentos"));
            v.Verificar("pasta só com instalador foi registrada", FoiDescartada(scanner, "Só Instalador"));
            v.Verificar("o contador de pastas varridas anda", scanner.PastasVisitadas >= 3,
                scanner.PastasVisitadas.ToString());
        }

        private static void TestarCancelamento(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Cancelamento");

            var fs = MontarHdDeExemplo();
            using (var origem = new System.Threading.CancellationTokenSource())
            {
                origem.Cancel();

                var cancelou = false;
                try
                {
                    new ScannerDeJogos(fs).Escanear(new[] { Raiz }, null, origem.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelou = true;
                }

                v.Verificar("o scan respeita o token de cancelamento", cancelou);
            }
        }

        // ---- Montagem das listagens reais ---------------------------------------------------------

        /// <summary>
        /// As três pastas exatamente como estão no HD, incluindo os arquivos-satélite
        /// repetidos que vêm de as três serem a mesma engine.
        /// </summary>
        private static SistemaDeArquivosSimulado MontarHdDeExemplo()
        {
            var fs = new SistemaDeArquivosSimulado();

            // Caso 1
            fs.AdicionarArquivos(PastaMostWanted,
                "safemode_inst.exe|40.960",
                "shell_inst.exe|40.960",
                "speed.exe|6.029.312",
                "dinput8.dll|180.224",
                "server.dll|94.208",
                "FirewallInstallHelper.dll|61.440",
                "GameuxInstallHelper.dll|73.728",
                "filelist.txt|12.288",
                "foobar|0",
                "00000000.256|256");

            foreach (var sub in new[] { "Uninstall", "Support", "CARS", "MOVIES", "SOUND", "TRACKS" })
                fs.AdicionarPasta(Path.Combine(PastaMostWanted, sub));

            // Caso 2
            fs.AdicionarArquivos(PastaUnderground2,
                "SPEED2.EXE|4.800.512",
                "unins000.exe|715.099",
                "SafeMode.bat|13",
                "NFSU_icon.ico|13.502",
                "dimap.dll|45.056",
                "dinput8.dll|180.224",
                "server.dll|94.208",
                "NFSU2Fix.sdb|24.576",
                "filelist.txt|10.240");

            foreach (var sub in new[] { "CARS", "MOVIES", "pfdata", "SDATA.Backup", "speech" })
                fs.AdicionarPasta(Path.Combine(PastaUnderground2, sub));

            // Caso 3
            fs.AdicionarArquivos(PastaCarbon,
                "NFSC.exe|7.217.152",
                "EAInstall.dll|118.784",
                "msvcp71.dll|499.712",
                "msvcr71.dll|348.160",
                "dinput8.dll|180.224",
                "server.dll|94.208",
                "foobar|0",
                "00000000.256|256");

            foreach (var sub in new[] { "CARS", "ONLINE", "SAVE", "scripts" })
                fs.AdicionarPasta(Path.Combine(PastaCarbon, sub));

            return fs;
        }

        // ---- Apoio ----------------------------------------------------------------------------------

        private static CandidatoExecutavel? Candidato(JogoDetectado jogo, string nomeDoArquivo)
            => jogo.Candidatos.Find(c => string.Equals(c.NomeDoArquivo, nomeDoArquivo, StringComparison.OrdinalIgnoreCase));

        private static bool TemCandidatoComExtensao(JogoDetectado jogo, string extensao)
            => jogo.Candidatos.Exists(c => string.Equals(Path.GetExtension(c.Caminho), extensao, StringComparison.OrdinalIgnoreCase));

        private static bool FoiDescartada(ScannerDeJogos scanner, string nomeDaPasta)
        {
            foreach (var descarte in scanner.Descartes)
            {
                if (descarte.Pasta.EndsWith(nomeDaPasta, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string NomesDePasta(List<JogoDetectado> jogos)
            => jogos.Count == 0 ? "(nenhum)" : string.Join(", ", jogos.ConvertAll(j => j.NomeDaPasta).ToArray());

        private static string ExecutavelDe(List<JogoDetectado> jogos, string nomeDaPasta)
        {
            var jogo = jogos.Find(j => string.Equals(j.NomeDaPasta, nomeDaPasta, StringComparison.OrdinalIgnoreCase));
            return jogo is null ? "(pasta não encontrada no resultado)" : jogo.Escolhido.NomeDoArquivo;
        }
    }
}
#endif   // DEBUG
