// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.Scanner;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 10: ações do card, adicionar jogo manualmente,
    /// impressão digital e religação.
    ///
    /// A parte que mais importa aqui é a impressão digital: é ela que impede o histórico
    /// de virar órfão quando eu reorganizo as pastas do HD — que é a coisa mais comum de
    /// se fazer num HD de jogos.
    /// </summary>
    public static class AutoTesteAcoes
    {
        private const string NomePastaSandbox = "_autoteste-acoes-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarHashFnv(v);

                PrepararSandbox(raizReal);

                TestarImpressaoDeArquivo(v);
                TestarImpressaoNoJson(v);
                TestarReconhecimentoDePastaRenomeada(v);
                TestarColisaoDeImpressao(v);
                TestarCopiaNaoRoubaHistorico(v);
                TestarReligacaoManual(v);
                TestarTiposDeArrasto(v);
                TestarAtalhoDoWindows(v);
                TestarSorteio(v);
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

        // ---- Impressão digital -----------------------------------------------------------------

        /// <summary>
        /// Vetores publicados do FNV-1a de 64 bits. Existem para o algoritmo não ser
        /// trocado sem querer: FNV-1 (multiplica e depois XOR) e a versão de 32 bits
        /// passam despercebidos numa revisão de código e invalidariam TODA impressão já
        /// gravada — sem erro, sem aviso, só o reconhecimento parando de funcionar.
        /// </summary>
        private static void TestarHashFnv(Verificador v)
        {
            v.Escrever("");
            v.Escrever("FNV-1a de 64 bits (vetores publicados)");

            v.Verificar("vazio = 0xcbf29ce484222325",
                ImpressaoDigital.Hash(Array.Empty<byte>(), 0) == 0xcbf29ce484222325);

            v.Verificar("\"a\" = 0xaf63dc4c8601ec8c",
                ImpressaoDigital.Hash(Encoding.ASCII.GetBytes("a"), 1) == 0xaf63dc4c8601ec8c);

            v.Verificar("\"foobar\" = 0x85944171f73967e8",
                ImpressaoDigital.Hash(Encoding.ASCII.GetBytes("foobar"), 6) == 0x85944171f73967e8);

            // Um byte diferente tem que dar hash diferente — é o mínimo que a impressão
            // precisa fazer para separar dois executáveis parecidos.
            v.Verificar("um byte de diferença muda o hash",
                ImpressaoDigital.Hash(new byte[] { 1, 2, 3 }, 3) !=
                ImpressaoDigital.Hash(new byte[] { 1, 2, 4 }, 3));

            v.Verificar("a quantidade lida é respeitada (não lê o buffer todo)",
                ImpressaoDigital.Hash(new byte[] { 1, 2, 3, 9, 9 }, 3) ==
                ImpressaoDigital.Hash(new byte[] { 1, 2, 3 }, 3));
        }

        private static void TestarImpressaoDeArquivo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Impressão digital de arquivo");

            var pasta = Path.Combine(Caminhos.PastaBase, "Jogos", "Corrida");
            Directory.CreateDirectory(pasta);

            var exe = Path.Combine(pasta, "speed.exe");
            File.WriteAllBytes(exe, ConteudoFalso(200_000, semente: 7));

            var impressao = ImpressaoDigital.De(exe);
            v.Verificar("calcula a impressão de um exe", impressao != null, impressao);

            v.Verificar("a impressão tem nome, tamanho e hash",
                impressao != null && impressao.StartsWith("speed.exe|200000|", StringComparison.Ordinal),
                impressao);

            v.Verificar("ler duas vezes dá o mesmo resultado",
                impressao == ImpressaoDigital.De(exe));

            // Um byte diferente DENTRO dos primeiros 64 KB tem que mudar a impressão.
            var quaseIgual = Path.Combine(pasta, "speed2.exe");
            var bytes = ConteudoFalso(200_000, semente: 7);
            bytes[1000] ^= 0xFF;
            File.WriteAllBytes(quaseIgual, bytes);

            v.Verificar("um byte trocado nos primeiros 64 KB muda a impressão",
                ImpressaoDigital.De(quaseIgual) != impressao);

            // Arquivo menor que o buffer não pode estourar nem virar impressão nula.
            var pequeno = Path.Combine(pasta, "mini.exe");
            File.WriteAllBytes(pequeno, new byte[] { 1, 2, 3 });

            var impressaoPequena = ImpressaoDigital.De(pequeno);
            v.Verificar("exe menor que 64 KB não estoura", impressaoPequena != null, impressaoPequena);
            v.Verificar("e leva o tamanho real na chave",
                impressaoPequena != null && impressaoPequena.Contains("|3|"), impressaoPequena);

            v.Verificar("arquivo inexistente devolve nulo (nunca lança)",
                ImpressaoDigital.De(Path.Combine(pasta, "nao-existe.exe")) is null);

            v.Verificar("caminho vazio devolve nulo", ImpressaoDigital.De("") is null);
        }

        /// <summary>
        /// A impressão só serve se sobreviver a fechar e reabrir o launcher: quem precisa
        /// dela é a entrada VELHA da biblioteca, e ela é lida do disco.
        /// </summary>
        private static void TestarImpressaoNoJson(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Impressão no biblioteca.json");

            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "nfs",
                Titulo = "NFS",
                ExecutavelRelativo = @"Jogos\Corrida\speed.exe",
                Impressao = "speed.exe|200000|abcdef0123456789"
            });

            var arquivo = Path.Combine(Caminhos.PastaEstado, "com-impressao.json");
            biblioteca.Salvar(arquivo);

            var lida = Biblioteca.Carregar(arquivo);
            v.Verificar("a impressão volta do disco igual",
                lida.ObterPorId("nfs")?.Impressao == "speed.exe|200000|abcdef0123456789",
                lida.ObterPorId("nfs")?.Impressao);

            // A impressão nasceu como campo aditivo na fase 10, com a biblioteca ainda em
            // v2. A fase 12 formalizou o schema em v3 e a gravação passou a sair na versão
            // do binário — o que o teste guarda é que a impressão atravessa o bump inteira,
            // que é o que importava desde o começo.
            v.Verificar($"a gravação sai na versão do binário ({Biblioteca.VersaoAtual})",
                lida.Versao == Biblioteca.VersaoAtual, lida.Versao.ToString());

            // Biblioteca antiga (sem o campo) não pode virar string vazia: nulo é "sem
            // impressão", e quem consome conta com isso.
            var semCampo = new Biblioteca();
            semCampo.Jogos.Add(new Jogo { Id = "x", Titulo = "X", ExecutavelRelativo = @"Jogos\x\x.exe" });

            var arquivoSemCampo = Path.Combine(Caminhos.PastaEstado, "sem-impressao.json");
            semCampo.Salvar(arquivoSemCampo);

            v.Verificar("jogo sem impressão grava null e relê null",
                Biblioteca.Carregar(arquivoSemCampo).ObterPorId("x")?.Impressao is null);
        }

        /// <summary>
        /// O caso que a fase inteira existe para resolver: eu renomeio a pasta do jogo e
        /// mando reescanear. Sem impressão, isso cria um card novo e abandona o antigo com
        /// o tempo jogado dentro.
        /// </summary>
        private static void TestarReconhecimentoDePastaRenomeada(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Pasta renomeada: o rescan reconhece sozinho");

            var pastaVelha = Path.Combine(Caminhos.PastaBase, "Jogos", "NFS Antigo");
            var pastaNova = Path.Combine(Caminhos.PastaBase, "Jogos", "Need for Speed");
            Directory.CreateDirectory(pastaVelha);

            var exeVelho = Path.Combine(pastaVelha, "speed.exe");
            File.WriteAllBytes(exeVelho, ConteudoFalso(120_000, semente: 11));

            var biblioteca = new Biblioteca();
            var jogo = new Jogo
            {
                Id = "nfs-antigo",
                Titulo = "NFS Antigo",
                ExecutavelRelativo = Caminhos.ParaRelativo(exeVelho),
                Impressao = ImpressaoDigital.De(exeVelho),
                SegundosJogados = 7200,
                CapaArquivo = "nfs-antigo.jpg",
                Favorito = true
            };
            biblioteca.Jogos.Add(jogo);

            // Renomeia a pasta, como eu faria no Explorer.
            Directory.Move(pastaVelha, pastaNova);
            var exeNovo = Path.Combine(pastaNova, "speed.exe");

            var resumo = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { Revisar(exeNovo, "Need For Speed") });

            v.Verificar("a biblioteca continua com UM jogo (não duplicou)",
                biblioteca.Jogos.Count == 1, biblioteca.Jogos.Count.ToString());

            v.Verificar("foi reconhecido pela impressão, não adicionado",
                resumo.ReligadosPelaImpressao.Count == 1 && resumo.Adicionados.Count == 0,
                $"religados={resumo.ReligadosPelaImpressao.Count} adicionados={resumo.Adicionados.Count}");

            v.Verificar("o id continua o mesmo", biblioteca.Jogos[0].Id == "nfs-antigo");
            v.Verificar("o tempo jogado ficou", biblioteca.Jogos[0].SegundosJogados == 7200);
            v.Verificar("a capa ficou", biblioteca.Jogos[0].CapaArquivo == "nfs-antigo.jpg");
            v.Verificar("o favorito ficou", biblioteca.Jogos[0].Favorito);

            v.Verificar("e o caminho agora aponta para a pasta nova",
                biblioteca.Jogos[0].ExecutavelRelativo == Caminhos.ParaRelativo(exeNovo),
                biblioteca.Jogos[0].ExecutavelRelativo);

            // Jogo catalogado antes da fase 10 ganha a impressão dele no primeiro rescan
            // em que o caminho ainda bate.
            var semImpressao = new Biblioteca();
            semImpressao.Jogos.Add(new Jogo
            {
                Id = "sem",
                Titulo = "Sem impressão",
                ExecutavelRelativo = Caminhos.ParaRelativo(exeNovo)
            });

            MescladorDeBiblioteca.Mesclar(semImpressao, new[] { Revisar(exeNovo, "Sem impressão") });

            v.Verificar("jogo antigo ganha a impressão dele no rescan seguinte",
                semImpressao.Jogos[0].Impressao != null, semImpressao.Jogos[0].Impressao);
        }

        /// <summary>
        /// Dois jogos com a mesma impressão (repack duplicado, pasta copiada): não dá para
        /// saber qual dos dois se mudou, então nenhum religa sozinho.
        /// </summary>
        private static void TestarColisaoDeImpressao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Colisão de impressão");

            var pasta = Path.Combine(Caminhos.PastaBase, "Jogos", "Colisao");
            Directory.CreateDirectory(pasta);

            var exe = Path.Combine(pasta, "jogo.exe");
            File.WriteAllBytes(exe, ConteudoFalso(50_000, semente: 3));
            var impressao = ImpressaoDigital.De(exe);

            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "copia-a",
                Titulo = "Cópia A",
                ExecutavelRelativo = @"Jogos\Sumiu A\jogo.exe",
                Impressao = impressao
            });
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "copia-b",
                Titulo = "Cópia B",
                ExecutavelRelativo = @"Jogos\Sumiu B\jogo.exe",
                Impressao = impressao
            });

            var resumo = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { Revisar(exe, "Achado") });

            v.Verificar("com dois candidatos idênticos, nenhum religa sozinho",
                resumo.ReligadosPelaImpressao.Count == 0);

            v.Verificar("e o jogo entra como novo, para eu decidir depois",
                resumo.Adicionados.Count == 1 && biblioteca.Jogos.Count == 3,
                biblioteca.Jogos.Count.ToString());
        }

        /// <summary>
        /// Copiei a pasta de um jogo para um segundo lugar e escaneei. O original continua
        /// lá — então nada se mudou, e religar arrastaria o histórico dele para a cópia.
        /// </summary>
        private static void TestarCopiaNaoRoubaHistorico(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Pasta copiada (o original ainda existe)");

            var original = Path.Combine(Caminhos.PastaBase, "Jogos", "Original");
            var copia = Path.Combine(Caminhos.PastaBase, "Jogos", "Backup do Original");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(copia);

            var conteudo = ConteudoFalso(80_000, semente: 23);
            var exeOriginal = Path.Combine(original, "jogo.exe");
            var exeCopia = Path.Combine(copia, "jogo.exe");
            File.WriteAllBytes(exeOriginal, conteudo);
            File.WriteAllBytes(exeCopia, conteudo);

            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "original",
                Titulo = "Original",
                ExecutavelRelativo = Caminhos.ParaRelativo(exeOriginal),
                Impressao = ImpressaoDigital.De(exeOriginal),
                SegundosJogados = 3600
            });

            var resumo = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { Revisar(exeCopia, "Backup do Original") });

            v.Verificar("a cópia NÃO rouba a entrada do original",
                resumo.ReligadosPelaImpressao.Count == 0 && resumo.Adicionados.Count == 1);

            v.Verificar("o original continua apontando para a pasta dele",
                biblioteca.ObterPorId("original")?.ExecutavelRelativo == Caminhos.ParaRelativo(exeOriginal));

            v.Verificar("e continua com o tempo jogado",
                biblioteca.ObterPorId("original")?.SegundosJogados == 3600);
        }

        /// <summary>
        /// Quando a impressão não resolve (o exe mudou, ou nunca houve impressão), sobra a
        /// religação manual. Ela nunca acontece sozinha — título igual não é prova.
        /// </summary>
        private static void TestarReligacaoManual(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Religação manual");

            var pasta = Path.Combine(Caminhos.PastaBase, "Jogos", "Carbon Novo");
            Directory.CreateDirectory(pasta);

            var exe = Path.Combine(pasta, "nfsc.exe");
            File.WriteAllBytes(exe, ConteudoFalso(90_000, semente: 31));

            var biblioteca = new Biblioteca();
            biblioteca.Jogos.Add(new Jogo
            {
                Id = "nfs-carbon",
                Titulo = "NFS Carbon",
                ExecutavelRelativo = @"Jogos\Some Daqui\nfsc.exe",   // caminho que não existe
                SegundosJogados = 5400,
                CapaArquivo = "nfs-carbon.jpg"
            });

            // Sem impressão gravada, o rescan não tem como reconhecer sozinho.
            var linha = Revisar(exe, "NFS Carbon");
            MescladorDeBiblioteca.SugerirReligacoes(biblioteca, new[] { linha });

            v.Verificar("o jogo sumido é oferecido como religação",
                linha.ReligacaoPossivel?.Id == "nfs-carbon", linha.ReligacaoPossivel?.Id);

            v.Verificar("mas a linha NÃO começa marcada para religar", !linha.VaiReligar);

            // Confirmar sem aceitar a oferta adiciona um jogo novo, como sempre fez.
            var semAceitar = new Biblioteca();
            semAceitar.Jogos.Add(new Jogo
            {
                Id = "nfs-carbon",
                Titulo = "NFS Carbon",
                ExecutavelRelativo = @"Jogos\Some Daqui\nfsc.exe"
            });
            var linhaIgnorada = Revisar(exe, "NFS Carbon");
            MescladorDeBiblioteca.SugerirReligacoes(semAceitar, new[] { linhaIgnorada });
            var resumoSem = MescladorDeBiblioteca.Mesclar(semAceitar, new[] { linhaIgnorada });

            v.Verificar("sem eu aceitar, nada religa e entra um jogo novo",
                resumoSem.ReligadosPorMim.Count == 0 && resumoSem.Adicionados.Count == 1);

            // Agora aceitando.
            linha.ReligarComId = linha.ReligacaoPossivel!.Id;
            var resumo = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { linha });

            v.Verificar("aceitando, religa em vez de adicionar",
                resumo.ReligadosPorMim.Count == 1 && resumo.Adicionados.Count == 0);

            v.Verificar("não duplicou a entrada", biblioteca.Jogos.Count == 1);
            v.Verificar("o id foi transferido", biblioteca.Jogos[0].Id == "nfs-carbon");
            v.Verificar("o tempo jogado veio junto", biblioteca.Jogos[0].SegundosJogados == 5400);
            v.Verificar("a capa veio junto", biblioteca.Jogos[0].CapaArquivo == "nfs-carbon.jpg");

            v.Verificar("e o caminho agora é o que o scanner achou",
                biblioteca.Jogos[0].ExecutavelRelativo == Caminhos.ParaRelativo(exe));

            v.Verificar("a religação já grava a impressão, para a próxima vez ser automática",
                biblioteca.Jogos[0].Impressao != null);
        }

        // ---- Arrastar e soltar -----------------------------------------------------------------

        private static void TestarTiposDeArrasto(Verificador v)
        {
            v.Escrever("");
            v.Escrever("O que cada tipo de arrasto vira");

            v.Verificar("jpg é imagem", AcoesDoJogo.EhImagem(@"C:\x\capa.jpg"));
            v.Verificar("PNG maiúsculo também", AcoesDoJogo.EhImagem(@"C:\x\CAPA.PNG"));
            v.Verificar("exe não é imagem", !AcoesDoJogo.EhImagem(@"C:\x\jogo.exe"));

            v.Verificar("exe é executável", AcoesDoJogo.EhExecutavel(@"C:\x\jogo.exe"));
            v.Verificar("lnk é executável", AcoesDoJogo.EhExecutavel(@"C:\x\jogo.lnk"));
            v.Verificar("bat e cmd são executáveis",
                AcoesDoJogo.EhExecutavel(@"C:\x\rodar.bat") && AcoesDoJogo.EhExecutavel(@"C:\x\rodar.cmd"));
            v.Verificar("dll não é executável (nunca foi candidato a jogo)",
                !AcoesDoJogo.EhExecutavel(@"C:\x\server.dll"));
            v.Verificar("jpg não é executável", !AcoesDoJogo.EhExecutavel(@"C:\x\capa.jpg"));
            v.Verificar("caminho vazio não é nada",
                !AcoesDoJogo.EhImagem("") && !AcoesDoJogo.EhExecutavel(null));
        }

        /// <summary>
        /// Um <c>.lnk</c> arrastado precisa virar o ALVO dele na biblioteca: gravar o
        /// atalho deixaria o acervo dependendo de um arquivo que vive fora do HD.
        /// </summary>
        private static void TestarAtalhoDoWindows(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Atalho do Windows (.lnk)");

            var pasta = Path.Combine(Caminhos.PastaBase, "Jogos", "ComAtalho");
            Directory.CreateDirectory(pasta);

            var exe = Path.Combine(pasta, "jogo.exe");
            File.WriteAllBytes(exe, ConteudoFalso(1000, semente: 5));

            try
            {
                var lnk = AtalhosDoWindows.CriarEm(pasta, "Meu Jogo", exe, "-janela", pasta);

                v.Verificar("cria o .lnk", File.Exists(lnk), lnk);

                var alvo = AtalhosDoWindows.AlvoDe(lnk);
                v.Verificar("e o alvo lido de volta é o executável",
                    string.Equals(alvo, exe, StringComparison.OrdinalIgnoreCase), alvo);
            }
            catch (Exception erro)
            {
                // COM do shell indisponível (sessão sem shell): não é falha do launcher.
                v.Escrever($"  (pulado: o shell não atendeu — {erro.Message})");
            }

            v.Verificar("atalho inexistente devolve nulo em vez de lançar",
                AtalhosDoWindows.AlvoDe(Path.Combine(pasta, "nao-existe.lnk")) is null);
        }

        // ---- Roleta ----------------------------------------------------------------------------

        private static void TestarSorteio(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Botão surpresa");

            var pasta = Path.Combine(Caminhos.PastaBase, "Jogos", "Sorteio");
            Directory.CreateDirectory(pasta);

            var existe = Path.Combine(pasta, "existe.exe");
            File.WriteAllBytes(existe, new byte[] { 1 });

            var presente = new Jogo
            {
                Id = "presente",
                Titulo = "Presente",
                ExecutavelRelativo = Caminhos.ParaRelativo(existe)
            };
            var ausente = new Jogo
            {
                Id = "ausente",
                Titulo = "Ausente",
                ExecutavelRelativo = @"Jogos\Sorteio\sumiu.exe"
            };

            var sorteio = new Random(42);

            // Cem tentativas: se o ausente pudesse sair, sairia.
            var soPresente = true;
            for (var i = 0; i < 100; i++)
            {
                if (SorteioDaGrade.Escolher(new[] { presente, ausente }, sorteio)?.Id != "presente")
                    soPresente = false;
            }

            v.Verificar("nunca sorteia um jogo marcado como não encontrado", soPresente);

            v.Verificar("lista só com ausentes não sorteia nada",
                SorteioDaGrade.Escolher(new[] { ausente }, sorteio) is null);

            v.Verificar("lista vazia não sorteia nada",
                SorteioDaGrade.Escolher(Array.Empty<Jogo>(), sorteio) is null);

            // O sorteio é sobre a lista VISÍVEL: quem filtra é quem chama, e é isso que
            // faz "surpresa" respeitar a busca que eu acabei de digitar.
            var outro = new Jogo
            {
                Id = "fora-do-filtro",
                Titulo = "Fora do filtro",
                ExecutavelRelativo = Caminhos.ParaRelativo(existe)
            };
            var soUm = SorteioDaGrade.Escolher(new[] { outro }, sorteio);
            v.Verificar("sorteia dentro da lista que recebeu, e só dela", soUm?.Id == "fora-do-filtro");
        }

        // ---- Apoio -----------------------------------------------------------------------------

        /// <summary>Monta a linha de revisão de um executável que existe em disco.</summary>
        private static JogoRevisado Revisar(string caminhoDoExe, string titulo)
        {
            var pasta = Path.GetDirectoryName(caminhoDoExe)!;
            var candidato = new CandidatoExecutavel(caminhoDoExe, Path.GetFileName(caminhoDoExe),
                                                   new FileInfo(caminhoDoExe).Length);
            candidato.Somar(RegraPlacar.MaiorExecutavel, 100, "teste");

            var detectado = new JogoDetectado(pasta, titulo, new List<CandidatoExecutavel> { candidato });
            return new JogoRevisado(detectado);
        }

        /// <summary>
        /// Conteúdo determinístico: o mesmo semente dá o mesmo arquivo, em qualquer
        /// máquina. Aleatório de verdade deixaria os testes de impressão instáveis.
        /// </summary>
        private static byte[] ConteudoFalso(int tamanho, int semente)
        {
            var bytes = new byte[tamanho];
            var estado = (uint)semente;

            for (var i = 0; i < tamanho; i++)
            {
                estado = estado * 1664525 + 1013904223;
                bytes[i] = (byte)(estado >> 24);
            }

            return bytes;
        }

        private static void PrepararSandbox(string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);
            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();
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
