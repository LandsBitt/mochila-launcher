// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Mochila.Dados;
using Mochila.Execucao;
using Mochila.Modelo;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação da fase 15: prioridade do processo e os scripts de antes e depois.
    ///
    /// Os quatro testes que a spec grifa são todos sobre a MESMA promessa, e é ela que este
    /// arquivo existe para defender: <b>nada em volta do lançamento pode impedir o jogo de
    /// abrir</b>. Script fora do drive é recusado na hora de configurar; script que sumiu,
    /// script que trava e prioridade que o Windows nega viram recado no rodapé, com o jogo
    /// subindo do mesmo jeito.
    /// </summary>
    public static class AutoTesteExecucao
    {
        private const string NomePastaSandbox = "_autoteste-execucao-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;
            var limiteOriginal = ScriptsDoJogo.LimiteDoAntes;

            try
            {
                TestarValidacaoDoScript(v, raizReal);
                TestarIdaEVoltaDasOpcoes(v, raizReal);
                TestarDescricaoDaFicha(v);
                TestarScriptAntesRoda(v, raizReal);
                TestarScriptDepoisRoda(v, raizReal);
                TestarScriptSumidoNaoImpedeOJogo(v, raizReal);
                TestarTempoEsgotadoDoAntes(v, raizReal);
                TestarPrioridade(v, raizReal);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                ScriptsDoJogo.LimiteDoAntes = limiteOriginal;
                Caminhos.RestaurarPastaBase();
                LimparSandbox(v, raizReal);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- O seletor: o que entra e o que é recusado -----------------------------------------

        /// <summary>
        /// A regra da spec: só .bat e .cmd, e só dentro do drive do launcher.
        ///
        /// O teste do outro drive é o que importa. Um script em C:\ funciona lindamente na
        /// minha máquina e some no notebook do amigo — e o caminho gravado teria uma letra
        /// de drive no <c>biblioteca.json</c>, que é a regra que este projeto não quebra.
        /// </summary>
        private static void TestarValidacaoDoScript(Verificador v, string raizReal)
        {
            v.Escrever("O que pode virar script de um jogo");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDeScripts = Path.Combine(sandbox, "Scripts");
            Directory.CreateDirectory(pastaDeScripts);

            var bat = Path.Combine(pastaDeScripts, "prepara.bat");
            var cmd = Path.Combine(pastaDeScripts, "prepara.cmd");
            var exe = Path.Combine(pastaDeScripts, "prepara.exe");
            var texto = Path.Combine(pastaDeScripts, "leia-me.txt");

            foreach (var arquivo in new[] { bat, cmd, exe, texto }) File.WriteAllText(arquivo, "@echo off");

            v.Verificar(".bat é aceito",
                ScriptsDoJogo.TentarAceitar(bat, out var relativoDoBat, out _), relativoDoBat);

            v.Verificar("e vira caminho relativo, sem letra de drive",
                relativoDoBat == Path.Combine("Scripts", "prepara.bat"), relativoDoBat);

            v.Verificar(".cmd é aceito", ScriptsDoJogo.TentarAceitar(cmd, out _, out _));

            v.Verificar("BAT em maiúscula é aceito (o Windows não liga)",
                ScriptsDoJogo.TemExtensaoAceita(@"Scripts\PREPARA.BAT"));

            // .exe fica de fora de propósito: para abrir outro executável existe o campo do
            // executável do jogo, e um "script" que é exe viraria um segundo lançador.
            v.Verificar(".exe é recusado", !ScriptsDoJogo.TentarAceitar(exe, out _, out var recusaDoExe));
            v.Verificar("e a recusa explica o porquê",
                recusaDoExe.Contains("não é um .bat"), recusaDoExe);

            v.Verificar(".txt é recusado", !ScriptsDoJogo.TentarAceitar(texto, out _, out _));
            v.Verificar("caminho vazio é recusado", !ScriptsDoJogo.TentarAceitar("", out _, out _));
            v.Verificar("null é recusado", !ScriptsDoJogo.TentarAceitar(null, out _, out _));

            // O caso que a spec grifa: fora do drive do launcher.
            var raizAtual = Path.GetPathRoot(Caminhos.PastaBase) ?? @"D:\";
            var outraLetra = raizAtual.StartsWith("Q", StringComparison.OrdinalIgnoreCase) ? "R" : "Q";
            var deOutroDrive = $@"{outraLetra}:\Scripts\prepara.bat";

            v.Verificar("script de outro drive é recusado",
                !ScriptsDoJogo.TentarAceitar(deOutroDrive, out _, out var recusaDoDrive));
            v.Verificar("e a recusa fala do HD do launcher",
                recusaDoDrive.Contains("fora do HD"), recusaDoDrive);

            v.Verificar("script de rede (UNC) é recusado",
                !ScriptsDoJogo.TentarAceitar(@"\\servidor\scripts\prepara.bat", out _, out _));

            // Segunda linha de defesa: mesmo que algo escape do seletor (arquivo editado à
            // mão, versão futura), a biblioteca não deixa gravar caminho absoluto.
            var biblioteca = new Biblioteca();
            var jogo = new Jogo
            {
                Id = "com-script-absoluto",
                Titulo = "Com script absoluto",
                ExecutavelRelativo = @"Jogos\x\x.exe"
            };
            jogo.OpcoesDeExecucao.ScriptAntes = deOutroDrive;
            biblioteca.Jogos.Add(jogo);

            v.Verificar("Validar acusa script com caminho absoluto", biblioteca.Validar().Count > 0);

            var destino = Path.Combine(Caminhos.PastaEstado, "nao-deve-existir.json");
            var recusouSalvar = false;
            try
            {
                biblioteca.Salvar(destino);
            }
            catch (InvalidOperationException)
            {
                recusouSalvar = true;
            }

            v.Verificar("e Salvar recusa a biblioteca inteira", recusouSalvar);
            v.Verificar("nada foi gravado", !File.Exists(destino));
        }

        // ---- O JSON --------------------------------------------------------------------------

        private static void TestarIdaEVoltaDasOpcoes(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("As opções sobrevivem ao disco");

            PrepararSandbox(raizReal);

            var biblioteca = new Biblioteca();
            var jogo = new Jogo
            {
                Id = "com-opcoes",
                Titulo = "Com opções",
                ExecutavelRelativo = @"Jogos\x\x.exe"
            };
            jogo.OpcoesDeExecucao.Prioridade = PrioridadeDoProcesso.Alta;
            jogo.OpcoesDeExecucao.ScriptAntes = @"Scripts\antes.bat";
            jogo.OpcoesDeExecucao.ScriptDepois = @"Scripts\depois.cmd";
            biblioteca.Jogos.Add(jogo);

            biblioteca.Salvar();

            var lido = Biblioteca.Carregar().ObterPorId("com-opcoes");
            v.Verificar("o jogo voltou do disco", lido is not null);
            if (lido is null) return;

            v.Verificar("prioridade preservada",
                lido.OpcoesDeExecucao.Prioridade == PrioridadeDoProcesso.Alta,
                lido.OpcoesDeExecucao.Prioridade.ToString());

            v.VerificarTexto("script de antes preservado", @"Scripts\antes.bat", lido.OpcoesDeExecucao.ScriptAntes);
            v.VerificarTexto("script de depois preservado", @"Scripts\depois.cmd", lido.OpcoesDeExecucao.ScriptDepois);

            // Lixo no campo da prioridade não pode impedir o jogo de existir: cai no padrão.
            var comLixo = OpcoesDeExecucao.PrioridadeDeTexto("turbo-maximo");
            v.Verificar("prioridade desconhecida no arquivo cai em normal",
                comLixo == PrioridadeDoProcesso.Normal, comLixo.ToString());

            v.Verificar("script em branco no arquivo é o mesmo que nenhum",
                OpcoesDeExecucao.DeJson(null).ScriptAntes is null);
        }

        private static void TestarDescricaoDaFicha(Verificador v)
        {
            v.Escrever("");
            v.Escrever("A linha \"Execução\" da tela de detalhes");

            var padrao = new OpcoesDeExecucao();
            v.Verificar("jogo no padrão não gera linha nenhuma",
                padrao.EhPadrao && padrao.Descrever().Length == 0, $"\"{padrao.Descrever()}\"");

            var so = new OpcoesDeExecucao { Prioridade = PrioridadeDoProcesso.Acima };
            v.Verificar("só a prioridade já tira do padrão",
                !so.EhPadrao && so.Descrever().Contains("acima"), so.Descrever());

            var tudo = new OpcoesDeExecucao
            {
                Prioridade = PrioridadeDoProcesso.Alta,
                ScriptAntes = @"Scripts\antes.bat",
                ScriptDepois = @"Scripts\depois.bat"
            };
            var descricao = tudo.Descrever();
            v.Verificar("com tudo configurado, a linha cita os três",
                descricao.Contains("alta") && descricao.Contains("antes.bat") && descricao.Contains("depois.bat"),
                descricao);
        }

        // ---- Os ganchos rodando de verdade -----------------------------------------------------

        /// <summary>
        /// O "antes" roda ANTES e roda na PASTA DO JOGO.
        ///
        /// O script mora numa pasta e escreve num caminho relativo; se o
        /// <c>WorkingDirectory</c> fosse o do script (ou o do launcher), o arquivo apareceria
        /// em outro lugar. É a mesma regra que o lançamento do jogo já obedece desde a fase
        /// 5, e ela vale para o gancho pelo mesmo motivo: um .bat de duas linhas para jogo
        /// antigo é todo escrito em caminho relativo.
        /// </summary>
        private static void TestarScriptAntesRoda(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("O script de antes");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "com-gancho");
            Directory.CreateDirectory(pastaDoJogo);

            var relativo = EscreverScript(sandbox, "antes.bat", "echo ok> antes-rodou.txt");

            var retorno = ScriptsDoJogo.RodarAntes(relativo, pastaDoJogo);

            v.Verificar("rodou até o fim", retorno.Resultado == ResultadoDoScript.Ok,
                retorno.Resultado.ToString());
            v.Verificar("e não teve nada a avisar", !retorno.TemAviso, retorno.Aviso);

            v.Verificar("o WorkingDirectory foi a pasta do jogo",
                File.Exists(Path.Combine(pastaDoJogo, "antes-rodou.txt")));

            v.Verificar("jogo sem script configurado não faz nada",
                ScriptsDoJogo.RodarAntes(null, pastaDoJogo).Resultado == ResultadoDoScript.SemScript);
        }

        private static void TestarScriptDepoisRoda(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("O script de depois");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "com-gancho");
            Directory.CreateDirectory(pastaDoJogo);

            var relativo = EscreverScript(sandbox, "depois.cmd", "echo ok> depois-rodou.txt");

            var retorno = ScriptsDoJogo.RodarDepois(relativo, pastaDoJogo);
            v.Verificar("foi disparado", retorno.Resultado == ResultadoDoScript.Ok, retorno.Resultado.ToString());

            // O "depois" não é esperado — a janela do launcher está voltando e não pode
            // ficar presa a um script de limpeza. Então aqui quem espera é o teste.
            v.Verificar("e o arquivo dele apareceu",
                Esperar(Path.Combine(pastaDoJogo, "depois-rodou.txt"), TimeSpan.FromSeconds(10)));
        }

        /// <summary>
        /// O teste que a spec pede com todas as letras: <b>script inexistente no lançamento
        /// avisa e não impede o jogo</b>.
        ///
        /// HD reorganizado é o caso normal deste launcher — é para ele que existe a
        /// impressão digital da fase 10. Um .bat que mudou de lugar não pode ser o motivo de
        /// eu não conseguir jogar.
        /// </summary>
        private static void TestarScriptSumidoNaoImpedeOJogo(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Script que sumiu: avisa, e o jogo abre igual");

            var sumido = ScriptsDoJogo.RodarAntes(@"Scripts\nunca-existiu.bat", raizReal);
            v.Verificar("o \"antes\" ausente é detectado",
                sumido.Resultado == ResultadoDoScript.NaoEncontrado, sumido.Resultado.ToString());
            v.Verificar("e o aviso diz que o jogo abriu mesmo assim",
                sumido.Aviso.Contains("mesmo assim"), sumido.Aviso);

            var sumidoDepois = ScriptsDoJogo.RodarDepois(@"Scripts\nunca-existiu.cmd", raizReal);
            v.Verificar("o \"depois\" ausente também é detectado",
                sumidoDepois.Resultado == ResultadoDoScript.NaoEncontrado);

            // Agora o lançamento inteiro, com processo de verdade.
            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "simulado");
            Directory.CreateDirectory(pastaDoJogo);

            if (CopiarExeDeMentira(v, pastaDoJogo) is not { } executavel) return;

            var jogo = new Jogo
            {
                Id = "com-script-sumido",
                Titulo = "Com script sumido",
                ExecutavelRelativo = Caminhos.ParaRelativo(executavel),
                Argumentos = "--dormir 1"
            };
            jogo.OpcoesDeExecucao.ScriptAntes = @"Scripts\nunca-existiu.bat";

            var avisos = new List<string>();

            using (var lancador = new LancadorDeJogos())
            using (var terminou = new ManualResetEventSlim(false))
            {
                lancador.AvisoDeScript += (_, aviso) => { lock (avisos) avisos.Add(aviso); };
                lancador.SessaoTerminada += (_, _) => terminou.Set();

                var abriu = true;
                try
                {
                    lancador.Lancar(jogo);
                }
                catch (Exception erro)
                {
                    abriu = false;
                    v.Escrever($"  (lançamento falhou: {erro.Message})");
                }

                v.Verificar("O JOGO ABRIU, com o script apontando para o nada", abriu);
                v.Verificar("e o launcher avisou", avisos.Count == 1, string.Join(" | ", avisos.ToArray()));

                v.Verificar("a sessão terminou normalmente", terminou.Wait(TimeSpan.FromSeconds(25)));
            }
        }

        /// <summary>
        /// O "antes" que trava: passa do teto, o jogo abre assim mesmo, e o script
        /// <b>continua rodando</b>.
        ///
        /// Não matá-lo é decisão, não esquecimento: o script pode estar no meio de uma cópia
        /// de arquivo do jogo, e interrompê-lo pela metade deixa o estado pior do que se
        /// nunca tivesse rodado. O teste prova as duas coisas — que eu parei de esperar e
        /// que ele terminou sozinho.
        /// </summary>
        private static void TestarTempoEsgotadoDoAntes(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("O script de antes que passa do tempo");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "demorado");
            Directory.CreateDirectory(pastaDoJogo);

            // "ping -n 4" espera ~3 s sem depender de timeout.exe, que recusa rodar com a
            // entrada redirecionada — que é exatamente o caso de um processo sem janela.
            var relativo = EscreverScript(sandbox, "demorado.bat",
                "ping -n 4 127.0.0.1 >nul", "echo fim> demorado-terminou.txt");

            var limiteOriginal = ScriptsDoJogo.LimiteDoAntes;
            RetornoDoScript retorno;
            var relogio = Stopwatch.StartNew();

            try
            {
                // Os 30 s da spec continuam sendo o padrão; aqui o teto vira meio segundo
                // para a suíte não parar por meio minuto provando isto.
                ScriptsDoJogo.LimiteDoAntes = TimeSpan.FromMilliseconds(500);
                retorno = ScriptsDoJogo.RodarAntes(relativo, pastaDoJogo);
            }
            finally
            {
                relogio.Stop();
                ScriptsDoJogo.LimiteDoAntes = limiteOriginal;
            }

            v.Verificar("o launcher desistiu de esperar",
                retorno.Resultado == ResultadoDoScript.TempoEsgotado, retorno.Resultado.ToString());

            v.Verificar("e desistiu no tempo certo, sem esperar o script inteiro",
                relogio.Elapsed < TimeSpan.FromSeconds(2), $"{relogio.Elapsed.TotalSeconds:F1}s");

            v.Verificar("o aviso diz que o jogo abriu assim mesmo",
                retorno.Aviso.Contains("assim mesmo"), retorno.Aviso);

            v.Verificar("o aviso lembra do \"pause\", que é a causa de sempre",
                retorno.Aviso.Contains("pause"), retorno.Aviso);

            // A prova de que ninguém foi morto: o script chega ao fim por conta própria.
            v.Verificar("e o script NÃO foi morto — terminou sozinho",
                Esperar(Path.Combine(pastaDoJogo, "demorado-terminou.txt"), TimeSpan.FromSeconds(20)));

            v.Verificar("os 30 s da spec continuam sendo o padrão",
                ScriptsDoJogo.LimiteDoAntes == TimeSpan.FromSeconds(30),
                ScriptsDoJogo.LimiteDoAntes.ToString());
        }

        // ---- Prioridade -------------------------------------------------------------------

        private static void TestarPrioridade(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Prioridade do processo");

            var sandbox = PrepararSandbox(raizReal);
            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "prioritario");
            Directory.CreateDirectory(pastaDoJogo);

            if (CopiarExeDeMentira(v, pastaDoJogo) is not { } executavel) return;

            var jogo = new Jogo
            {
                Id = "prioritario",
                Titulo = "Prioritário",
                ExecutavelRelativo = Caminhos.ParaRelativo(executavel),
                Argumentos = "--dormir 4"
            };
            jogo.OpcoesDeExecucao.Prioridade = PrioridadeDoProcesso.Alta;

            var avisos = new List<string>();

            var processo = Process.Start(new ProcessStartInfo
            {
                FileName = executavel,
                Arguments = "--dormir 4",
                WorkingDirectory = pastaDoJogo,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (processo is null)
            {
                v.RegistrarErro("  FALHA não consegui abrir o processo de mentira");
                return;
            }

            try
            {
                LancadorDeJogos.AplicarPrioridade(processo, jogo, avisos.Add);

                processo.Refresh();
                v.Verificar("\"alta\" vira High de verdade no processo",
                    processo.PriorityClass == ProcessPriorityClass.High,
                    processo.PriorityClass.ToString());

                // E para em High: RealTime tiraria ciclo do teclado e do mouse, o que num
                // PC fraco trava a máquina em vez de acelerar o jogo.
                v.Verificar("e não chega em RealTime",
                    processo.PriorityClass != ProcessPriorityClass.RealTime);

                jogo.OpcoesDeExecucao.Prioridade = PrioridadeDoProcesso.Acima;
                LancadorDeJogos.AplicarPrioridade(processo, jogo, avisos.Add);
                processo.Refresh();

                v.Verificar("\"acima\" vira AboveNormal",
                    processo.PriorityClass == ProcessPriorityClass.AboveNormal,
                    processo.PriorityClass.ToString());

                v.Verificar("nada disso precisou avisar", avisos.Count == 0,
                    string.Join(" | ", avisos.ToArray()));

                // Prioridade "normal" não encosta no processo: é o caso de todo o acervo, e
                // pagar uma chamada ao sistema por jogo aberto seria trabalho por nada.
                jogo.OpcoesDeExecucao.Prioridade = PrioridadeDoProcesso.Normal;
                LancadorDeJogos.AplicarPrioridade(processo, jogo, avisos.Add);
                processo.Refresh();

                v.Verificar("\"normal\" não mexe no que já estava lá",
                    processo.PriorityClass == ProcessPriorityClass.AboveNormal,
                    processo.PriorityClass.ToString());
            }
            finally
            {
                try { processo.Kill(); } catch (Exception) { }
                try { processo.WaitForExit(5000); } catch (Exception) { }
            }

            // O teste que a spec pede: falhar em elevar prioridade NÃO derruba o lançamento.
            // Um processo já morto é a forma determinística de provocar a falha — no mundo
            // real ela vem de política de grupo, antivírus ou conta sem privilégio.
            jogo.OpcoesDeExecucao.Prioridade = PrioridadeDoProcesso.Alta;
            avisos.Clear();

            var explodiu = false;
            try
            {
                LancadorDeJogos.AplicarPrioridade(processo, jogo, avisos.Add);
            }
            catch (Exception)
            {
                explodiu = true;
            }
            finally
            {
                processo.Dispose();
            }

            v.Verificar("PRIORIDADE QUE FALHA NÃO LANÇA EXCEÇÃO (o jogo abriria igual)", !explodiu);
            v.Verificar("e o launcher avisa, no rodapé", avisos.Count == 1,
                string.Join(" | ", avisos.ToArray()));

            if (avisos.Count == 1)
            {
                v.Verificar("o aviso diz que o jogo abriu normalmente",
                    avisos[0].Contains("abriu normalmente"), avisos[0]);
            }
        }

        // ---- Apoio ---------------------------------------------------------------------------

        /// <summary>Escreve um .bat na pasta Scripts\ da sandbox e devolve o relativo dele.</summary>
        private static string EscreverScript(string sandbox, string nome, params string[] linhas)
        {
            var pasta = Path.Combine(sandbox, "Scripts");
            Directory.CreateDirectory(pasta);

            var caminho = Path.Combine(pasta, nome);

            var conteudo = new List<string> { "@echo off" };
            conteudo.AddRange(linhas);

            // Codepage do console, não UTF-8: um .bat escrito com BOM começa com três bytes
            // que o cmd tenta executar como comando.
            File.WriteAllLines(caminho, conteudo.ToArray(), System.Text.Encoding.Default);

            return Caminhos.ParaRelativo(caminho);
        }

        /// <summary>
        /// O jogo de mentira é uma cópia do próprio launcher, que sabe dormir por N
        /// segundos com "--dormir". Mesmo truque da fase 5.
        /// </summary>
        private static string? CopiarExeDeMentira(Verificador v, string pastaDoJogo)
        {
            var origem = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(origem) || !File.Exists(origem))
            {
                v.Escrever("  (sem executável de origem para copiar — teste pulado)");
                return null;
            }

            var destino = Path.Combine(pastaDoJogo, "jogo.exe");
            File.Copy(origem!, destino, overwrite: true);
            return destino;
        }

        /// <summary>Espera um arquivo aparecer. Devolve false se ele não apareceu a tempo.</summary>
        private static bool Esperar(string arquivo, TimeSpan limite)
        {
            var fim = DateTime.UtcNow + limite;

            while (DateTime.UtcNow < fim)
            {
                if (File.Exists(arquivo)) return true;
                Thread.Sleep(50);
            }
            return File.Exists(arquivo);
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

            // O exe copiado (e o cmd.exe de um script que ainda está terminando) podem
            // segurar a pasta por um instante depois de o teste passar.
            for (var tentativa = 0; tentativa < 5; tentativa++)
            {
                try
                {
                    if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
                    return;
                }
                catch (Exception ex)
                {
                    if (tentativa == 4) v.Escrever($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
                    else Thread.Sleep(400);
                }
            }
        }
    }
}
#endif   // DEBUG
