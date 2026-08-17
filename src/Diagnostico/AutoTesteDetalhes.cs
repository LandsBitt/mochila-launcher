// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Mochila.Entrada;
using Mochila.Modelo;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 11: a tela de detalhes.
    ///
    /// Duas coisas mandam aqui, e as duas são regra da spec:
    ///
    /// 1. <b>A arte grande morre ao fechar.</b> É o que separa este painel de um Form por
    ///    jogo, e é a única razão de a fase 11 caber na regra de memória do launcher. O
    ///    número está no <c>--bench-detalhes</c>; aqui fica o invariante.
    /// 2. <b>Existe um caminho de navegação só.</b> Abrir, folhear e fechar acontecem por
    ///    <see cref="ComandoDeNavegacao"/>, no mesmo roteador do gamepad e do teclado — o
    ///    teste manda comando, não aperta tecla nem chama método de tela.
    /// </summary>
    public static class AutoTesteDetalhes
    {
        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);

            try
            {
                TestarDuploCliqueNaGrade(v);

                AcervoDeDemonstracao.Montar(20);
                try
                {
                    ComJanela(janela =>
                    {
                        TestarAbrirEFechar(v, janela);
                        TestarLiberacaoDaArte(v, janela);
                        TestarFolhear(v, janela);
                        TestarComandosEngolidos(v, janela);
                        TestarArgumentos(v, janela);
                        TestarJogoSemCapa(v, janela);
                        TestarRegraDoLauncherDormindo(v, janela);
                    });
                }
                finally
                {
                    AcervoDeDemonstracao.Limpar();
                }
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- O que o duplo clique virou --------------------------------------------------------

        /// <summary>
        /// A mudança de comportamento da fase 11, escrita como teste: duplo clique deixou
        /// de lançar. Lançar ficou com Enter e com o A do controle — é a ação cara (salva,
        /// esconde a janela, sobe um processo) e merece ser deliberada.
        /// </summary>
        private static void TestarDuploCliqueNaGrade(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Duplo clique num card");

            using (var miniaturas = new CacheDeMiniaturas())
            using (var grade = new GradeDeCapas(miniaturas))
            {
                var lancou = false;
                var pediuDetalhes = (Jogo?)null;

                grade.JogoAcionado += (_, _) => lancou = true;
                grade.DetalhesPedidos += (_, jogo) => pediuDetalhes = jogo;

                grade.DefinirJogos(new[]
                {
                    new Jogo { Id = "a", Titulo = "A", ExecutavelRelativo = @"Jogos\a\a.exe" },
                    new Jogo { Id = "b", Titulo = "B", ExecutavelRelativo = @"Jogos\b\b.exe" }
                });

                grade.PedirDetalhes();

                v.Verificar("duplo clique pede a tela de detalhes", pediuDetalhes?.Id == "a", pediuDetalhes?.Id);
                v.Verificar("e NÃO lança o jogo", !lancou);

                // Enter continua sendo o que lança, pelo comando de sempre.
                grade.TratarComando(ComandoDeNavegacao.Confirmar);
                v.Verificar("Enter (Confirmar) continua lançando", lancou);
            }
        }

        // ---- Abrir, fechar e o contexto do roteador --------------------------------------------

        private static void TestarAbrirEFechar(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("Abrir e fechar pelo roteador de comandos");

            v.Verificar("a janela começa no contexto da grade",
                janela.ContextoParaDiagnostico == ContextoDeNavegacao.Grade);

            v.Verificar("o comando Detalhes (X do controle, tecla I) é tratado",
                janela.DespacharParaDiagnostico(ComandoDeNavegacao.Detalhes));

            v.Verificar("a tela abriu", janela.DetalhesAbertosParaDiagnostico);
            v.Verificar("e o contexto virou Detalhes",
                janela.ContextoParaDiagnostico == ContextoDeNavegacao.Detalhes,
                janela.ContextoParaDiagnostico.ToString());

            v.Verificar("mostra o jogo que estava selecionado",
                janela.PainelDeDetalhesParaDiagnostico.JogoAtual?.Id == janela.GradeParaDiagnostico.JogoSelecionado?.Id);

            // Voltar é o Esc do teclado e o B do controle — mesmo comando, mesmo caminho.
            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Voltar);

            v.Verificar("Voltar fecha", !janela.DetalhesAbertosParaDiagnostico);
            v.Verificar("e o contexto volta para a grade",
                janela.ContextoParaDiagnostico == ContextoDeNavegacao.Grade);

            // A mesma tecla que abre também fecha: numa tela que se sobrepõe, é o que a
            // mão espera.
            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Detalhes);
            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Detalhes);

            v.Verificar("Detalhes de novo fecha a tela", !janela.DetalhesAbertosParaDiagnostico);
        }

        /// <summary>
        /// O invariante da fase: nada de imagem grande viva com a tela fechada. Sem isto, o
        /// painel seria só um Form com outro nome.
        /// </summary>
        private static void TestarLiberacaoDaArte(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("A arte grande morre ao fechar");

            var painel = janela.AbrirDetalhesParaDiagnostico();
            v.Verificar("aberta, a arte em resolução cheia está carregada",
                painel.ArteCarregadaParaDiagnostico);

            janela.FecharDetalhesParaDiagnostico();
            v.Verificar("fechada, não sobra imagem nenhuma", !painel.ArteCarregadaParaDiagnostico);

            // Dez idas e voltas: se o Dispose faltasse em algum caminho, seriam dez imagens
            // de 600x900 vivas ao mesmo tempo.
            for (var i = 0; i < 10; i++)
            {
                janela.AbrirDetalhesParaDiagnostico();
                janela.FecharDetalhesParaDiagnostico();
            }

            v.Verificar("dez aberturas seguidas não deixam arte para trás",
                !painel.ArteCarregadaParaDiagnostico);
        }

        // ---- Folhear -----------------------------------------------------------------------------

        private static void TestarFolhear(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("Folhear sem fechar (← →)");

            var grade = janela.GradeParaDiagnostico;
            grade.Selecionar(0);

            var painel = janela.AbrirDetalhesParaDiagnostico();
            var primeiro = painel.JogoAtual?.Id;

            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Direita);

            v.Verificar("→ passa para o jogo seguinte", painel.JogoAtual?.Id == grade.Jogos[1].Id,
                painel.JogoAtual?.Id);
            v.Verificar("e a tela continua aberta", janela.DetalhesAbertosParaDiagnostico);
            v.Verificar("com a arte do jogo novo carregada", painel.ArteCarregadaParaDiagnostico);
            v.Verificar("a seleção da grade acompanha", grade.IndiceSelecionado == 1);

            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Esquerda);
            v.Verificar("← volta para o anterior", painel.JogoAtual?.Id == primeiro, painel.JogoAtual?.Id);

            // Na ponta, não sai da lista nem fecha.
            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Esquerda);
            v.Verificar("no primeiro jogo, ← não faz nada",
                janela.DetalhesAbertosParaDiagnostico && painel.JogoAtual?.Id == primeiro);

            grade.Selecionar(grade.Jogos.Count - 1);
            janela.AbrirDetalhesParaDiagnostico();
            janela.DespacharParaDiagnostico(ComandoDeNavegacao.Direita);

            v.Verificar("no último jogo, → não faz nada",
                janela.DetalhesAbertosParaDiagnostico &&
                painel.JogoAtual?.Id == grade.Jogos[grade.Jogos.Count - 1].Id);

            janela.FecharDetalhesParaDiagnostico();
        }

        /// <summary>
        /// Comandos de rolagem não podem "vazar" para a grade escondida atrás: eu voltaria
        /// dos detalhes com a seleção noutro lugar sem ter visto nada mexer.
        /// </summary>
        private static void TestarComandosEngolidos(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("Comandos de rolagem não atravessam a tela");

            var grade = janela.GradeParaDiagnostico;
            grade.Selecionar(0);
            janela.AbrirDetalhesParaDiagnostico();

            foreach (var comando in new[]
                     {
                         ComandoDeNavegacao.Baixo, ComandoDeNavegacao.Cima,
                         ComandoDeNavegacao.PaginaSeguinte, ComandoDeNavegacao.Ultimo
                     })
            {
                v.Verificar($"{comando} é engolido (tratado, sem efeito)",
                    janela.DespacharParaDiagnostico(comando));
            }

            v.Verificar("a seleção da grade não se moveu", grade.IndiceSelecionado == 0,
                grade.IndiceSelecionado.ToString());

            janela.FecharDetalhesParaDiagnostico();
        }

        // ---- Argumentos --------------------------------------------------------------------------

        private static void TestarArgumentos(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("Argumentos editáveis");

            var grade = janela.GradeParaDiagnostico;
            grade.Selecionar(0);

            var jogo = grade.JogoSelecionado!;
            var painel = janela.AbrirDetalhesParaDiagnostico();

            painel.EditarArgumentosParaDiagnostico("-windowed -nointro");
            v.Verificar("o que eu digito vai para o jogo", jogo.Argumentos == "-windowed -nointro", jogo.Argumentos);

            v.Verificar("e chega ao disco na hora",
                Biblioteca.Carregar().ObterPorId(jogo.Id)?.Argumentos == "-windowed -nointro");

            // Espaço em volta é meu erro de digitação, não argumento.
            painel.EditarArgumentosParaDiagnostico("   -janela   ");
            v.Verificar("espaço em volta é aparado", jogo.Argumentos == "-janela", $"\"{jogo.Argumentos}\"");

            painel.EditarArgumentosParaDiagnostico("");
            v.Verificar("apagar tudo limpa os argumentos", jogo.Argumentos.Length == 0);

            janela.FecharDetalhesParaDiagnostico();
        }

        /// <summary>
        /// Jogo sem capa não pode abrir uma tela vazia: cai no mesmo card desenhado da
        /// grade, só que grande.
        /// </summary>
        private static void TestarJogoSemCapa(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("Jogo sem capa");

            var grade = janela.GradeParaDiagnostico;
            var indice = -1;

            for (var i = 0; i < grade.Jogos.Count; i++)
            {
                if (string.IsNullOrEmpty(grade.Jogos[i].CapaArquivo)) { indice = i; break; }
            }

            if (indice < 0)
            {
                v.Escrever("  (pulado: o acervo sintético desta rodada saiu todo com capa)");
                return;
            }

            grade.Selecionar(indice);
            var painel = janela.AbrirDetalhesParaDiagnostico();

            v.Verificar("sem capa, a tela abre com o card desenhado",
                janela.DetalhesAbertosParaDiagnostico && painel.ArteCarregadaParaDiagnostico);

            janela.FecharDetalhesParaDiagnostico();
        }

        /// <summary>
        /// "Enquanto um jogo roda, o launcher dorme" agora vale também para a maior imagem
        /// que ele carrega. Sair da frente do jogo tem que fechar a tela de detalhes.
        /// </summary>
        private static void TestarRegraDoLauncherDormindo(Verificador v, FormPrincipal janela)
        {
            v.Escrever("");
            v.Escrever("Com o jogo aberto, nada nosso fica na memória");

            janela.GradeParaDiagnostico.Selecionar(0);
            var painel = janela.AbrirDetalhesParaDiagnostico();

            janela.SairDaFrenteDoJogoParaDiagnostico();

            v.Verificar("sair da frente do jogo fecha os detalhes", !janela.DetalhesAbertosParaDiagnostico);
            v.Verificar("e solta a arte grande junto com as miniaturas",
                !painel.ArteCarregadaParaDiagnostico);
            v.Verificar("o contexto do roteador volta para a grade",
                janela.ContextoParaDiagnostico == ContextoDeNavegacao.Grade);
        }

        // ---- Apoio -------------------------------------------------------------------------------

        /// <summary>
        /// Roda o teste com uma janela de verdade, fora da área visível.
        ///
        /// Tem que ser mostrada: <c>Control.Visible</c> de um filho responde false enquanto
        /// a janela não aparece, e é justamente esse estado que os testes leem.
        /// </summary>
        private static void ComJanela(Action<FormPrincipal> teste)
        {
            using (var janela = new FormPrincipal())
            {
                janela.StartPosition = FormStartPosition.Manual;
                janela.ShowInTaskbar = false;
                janela.Size = new Size(1200, 800);
                janela.Location = new Point(-32000, -32000);
                janela.Show();

                try
                {
                    teste(janela);
                }
                finally
                {
                    janela.Close();
                }
            }
        }
    }
}
#endif   // DEBUG
