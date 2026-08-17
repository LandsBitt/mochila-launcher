// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.Scanner;
using Mochila.UI;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 3: janela de revisão e gravação na biblioteca.
    ///
    /// O acervo real tem só três jogos, todos da mesma engine e todos resolvidos com
    /// folga — não exercita a janela de jeito nenhum. Então os testes daqui usam um
    /// acervo sintético de propósito difícil: pasta com 15 candidatos, jogo de placar
    /// baixo (a linha amarela), nomes sujos de repack e pasta sem candidato nenhum.
    /// </summary>
    public static class AutoTesteRevisao
    {
        private const string NomePastaSandbox = "_autoteste-revisao-tmp";

        /// <summary>Raiz virtual do acervo sintético (o scanner não toca em disco aqui).</summary>
        private const string Raiz = @"Z:\Jogos";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarLinhasDaRevisao(v);
                TestarComboDeCandidatos(v);
                TestarEdicaoDeTitulo(v);
                TestarBotoesDeMarcacao(v);
                TestarGeometriaDaJanela(v);
                TestarMesclagem(v, raizReal);
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

        // ---- A lista da janela ------------------------------------------------------------------

        private static void TestarLinhasDaRevisao(Verificador v)
        {
            v.Escrever("Linhas da janela de revisão");

            using (var janela = AbrirRevisao(out var descartes))
            {
                v.Verificar("uma linha por jogo detectado",
                    janela.Linhas.Count == 5, DescreverLinhas(janela));

                v.Verificar("tudo começa marcado",
                    janela.Confirmados().Count == janela.Linhas.Count);

                // Título limpo de tags de repack.
                var repack = Linha(janela, "Cuphead");
                v.Verificar("jogo de nome sujo entrou na lista", repack >= 0, DescreverLinhas(janela));
                if (repack >= 0)
                {
                    v.VerificarTexto("[FitGirl], v1.2.3, REPACK e PT-BR sumiram do título",
                        "Cuphead", janela.Linhas[repack].Titulo);
                    v.VerificarTexto("e é esse título que aparece na lista",
                        "Cuphead", janela.TextoDaLinha(repack)[0]);
                }

                var gog = Linha(janela, "The Witcher (Enhanced Edition)");
                v.Verificar("tag (GOG) sai e o parêntese legítimo fica", gog >= 0, DescreverLinhas(janela));

                // Linha amarela.
                var duvidoso = Linha(janela, "Jogo Duvidoso");
                v.Verificar("o jogo de placar baixo entrou na lista", duvidoso >= 0, DescreverLinhas(janela));
                if (duvidoso >= 0)
                {
                    var linha = janela.Linhas[duvidoso];
                    v.Verificar("placar abaixo de 30",
                        linha.Escolhido.Placar < Pontuador.PlacarDeConfianca,
                        linha.Escolhido.Placar.ToString(CultureInfo.InvariantCulture));
                    v.Verificar("marcado como baixa confiança", linha.BaixaConfianca);
                    v.Verificar("linha pintada de amarelo",
                        janela.CorDaLinha(duvidoso) == Tema.FundoBaixaConfianca,
                        janela.CorDaLinha(duvidoso).ToString());
                }

                // Linha normal não pode ficar amarela.
                var normal = Linha(janela, "Cuphead");
                if (normal >= 0)
                {
                    v.Verificar("jogo de placar bom não fica amarelo",
                        janela.CorDaLinha(normal) == Tema.Fundo, janela.CorDaLinha(normal).ToString());
                }

                // Pasta sem candidato nenhum.
                v.Verificar("pasta sem executável não virou linha",
                    Linha(janela, "Só Leia-me") < 0, DescreverLinhas(janela));
                v.Verificar("e foi registrada como descartada",
                    Contem(descartes, "Só Leia-me"), $"{descartes.Count} descarte(s)");
                v.Verificar("pasta só com instalador também foi descartada",
                    Contem(descartes, "Repack Não Instalado"));
                v.Verificar("a janela recebe a lista de descartes",
                    janela.Descartes.Count == descartes.Count);

                // Desmarcar tira da gravação.
                janela.DefinirInclusao(0, false);
                v.Verificar("desmarcar tira o jogo do que vai ser gravado",
                    janela.Confirmados().Count == janela.Linhas.Count - 1);
            }
        }

        private static void TestarComboDeCandidatos(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Combo de candidatos");

            using (var janela = AbrirRevisao(out _))
            {
                var indice = Linha(janela, "Jogo Cheio De Exes");
                if (indice < 0)
                {
                    v.RegistrarErro("  FALHA a pasta de 15 candidatos não virou linha");
                    return;
                }

                var opcoes = janela.OpcoesDoCombo(indice);
                v.Verificar("os 15 candidatos aparecem no combo", opcoes.Count == 15, opcoes.Count.ToString());

                var candidatos = janela.Linhas[indice].Deteccao.Candidatos;
                var emOrdem = true;
                for (var i = 1; i < candidatos.Count; i++)
                {
                    // Excluído vai para o fim; entre os demais, placar decrescente.
                    if (candidatos[i - 1].Excluido == candidatos[i].Excluido &&
                        candidatos[i - 1].Placar < candidatos[i].Placar)
                    {
                        emOrdem = false;
                    }
                    if (candidatos[i - 1].Excluido && !candidatos[i].Excluido) emOrdem = false;
                }
                v.Verificar("combo ordenado por placar, com os excluídos no fim", emOrdem);

                v.Verificar("o combo mostra o placar de cada candidato",
                    opcoes[0].Contains("pts"), opcoes[0]);
                v.Verificar("candidato vetado aparece marcado como excluído",
                    opcoes[opcoes.Count - 1].Contains("EXCLUÍDO"), opcoes[opcoes.Count - 1]);

                // Trocar o executável marca a escolha como minha.
                var antes = janela.Linhas[indice].Escolhido;
                v.Verificar("antes de eu mexer, a escolha é do scanner", !janela.Linhas[indice].EscolhaAlterada);

                janela.TrocarCandidato(indice, 3);
                var depois = janela.Linhas[indice].Escolhido;

                v.Verificar("trocar no combo troca o executável escolhido", !ReferenceEquals(antes, depois));
                v.Verificar("a linha registra que a escolha não é mais a do scanner",
                    janela.Linhas[indice].EscolhaAlterada);
                v.Verificar("mas trocar no combo NÃO trava a linha contra rescan",
                    !janela.Linhas[indice].Fixar);
                v.VerificarTexto("e a coluna do cadeado continua mostrando destravado",
                    "—", janela.TextoDaLinha(indice)[3]);

                janela.DefinirFixado(indice, true);
                v.Verificar("travar é um clique à parte", janela.Linhas[indice].Fixar);
                v.VerificarTexto("com estado visível na grade", "🔒 fixado", janela.TextoDaLinha(indice)[3]);

                janela.DefinirFixado(indice, false);
                v.Verificar("e é reversível", !janela.Linhas[indice].Fixar);
                v.VerificarTexto("a lista passa a mostrar o novo executável",
                    depois.CaminhoRelativoAoJogo, janela.TextoDaLinha(indice)[1]);
                v.VerificarTexto("e o novo placar",
                    depois.Placar.ToString(CultureInfo.InvariantCulture), janela.TextoDaLinha(indice)[2]);

                // Forçar um vetado é permitido — é para isso que ele continua na lista.
                var indiceDoVetado = candidatos.Count - 1;
                janela.TrocarCandidato(indice, indiceDoVetado);
                v.Verificar("dá para forçar um candidato excluído, se eu quiser",
                    janela.Linhas[indice].Escolhido.Excluido);
                v.Verificar("e a linha fica amarela, porque o placar despencou",
                    janela.CorDaLinha(indice) == Tema.FundoBaixaConfianca);
            }
        }

        private static void TestarEdicaoDeTitulo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Edição do título");

            using (var janela = AbrirRevisao(out _))
            {
                janela.RenomearTitulo(0, "  Nome Que Eu Escolhi  ");
                v.VerificarTexto("título editado é aparado e guardado",
                    "Nome Que Eu Escolhi", janela.Linhas[0].Titulo);
                v.VerificarTexto("e aparece na lista", "Nome Que Eu Escolhi", janela.TextoDaLinha(0)[0]);

                var anterior = janela.Linhas[0].Titulo;
                janela.RenomearTitulo(0, "   ");
                v.VerificarTexto("título em branco é recusado", anterior, janela.Linhas[0].Titulo);
            }
        }

        private static void TestarBotoesDeMarcacao(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Botões de marcação");

            using (var janela = AbrirRevisao(out _))
            {
                janela.MarcarTodos(false);
                v.Verificar("desmarcar todos zera a lista de gravação", janela.Confirmados().Count == 0);

                janela.MarcarTodos(true);
                v.Verificar("marcar todos volta tudo", janela.Confirmados().Count == janela.Linhas.Count);

                janela.MarcarSomenteConfiaveis();
                var confirmados = janela.Confirmados();

                var algumAmareloMarcado = false;
                foreach (var linha in confirmados)
                {
                    if (linha.BaixaConfianca) algumAmareloMarcado = true;
                }

                v.Verificar("\"só os confiáveis\" desmarca as linhas amarelas", !algumAmareloMarcado);
                v.Verificar("e mantém as demais", confirmados.Count > 0, confirmados.Count.ToString());
            }
        }

        // ---- Gravação na biblioteca -----------------------------------------------------------

        /// <summary>
        /// Aqui o disco entra: a mesclagem precisa de caminhos de verdade para virar
        /// relativo. Tudo acontece numa sandbox ao lado do executável, apagada no fim.
        /// </summary>
        private static void TestarMesclagem(Verificador v, string raizReal)
        {
            v.Escrever("");
            v.Escrever("Gravação na biblioteca");

            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);
            Caminhos.DefinirPastaBase(sandbox);

            var pastaDoJogo = Path.Combine(sandbox, "Jogos", "Jogo Gravado");
            var exePrincipal = Path.Combine(pastaDoJogo, "jogo gravado.exe");
            var exeAlternativo = Path.Combine(pastaDoJogo, "Bin", "outro.exe");

            var fs = new SistemaDeArquivosSimulado()
                .AdicionarArquivo(exePrincipal, 9_000_000)
                .AdicionarArquivo(exeAlternativo, 8_000_000);

            var detectado = new ScannerDeJogos(fs).AnalisarPastaDeJogo(pastaDoJogo);
            if (detectado is null)
            {
                v.RegistrarErro("  FALHA o jogo de teste não foi detectado");
                return;
            }

            // 1) Jogo novo entra.
            var biblioteca = new Biblioteca();
            var revisado = new JogoRevisado(detectado);
            var resumo = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { revisado });

            v.Verificar("jogo novo é adicionado", resumo.Adicionados.Count == 1 && biblioteca.Jogos.Count == 1);
            if (biblioteca.Jogos.Count == 0) return;

            var gravado = biblioteca.Jogos[0];
            v.VerificarTexto("caminho gravado é relativo, sem letra de drive",
                @"Jogos\Jogo Gravado\jogo gravado.exe", gravado.ExecutavelRelativo);
            v.Verificar("id gerado a partir do título", gravado.Id == "jogo-gravado", gravado.Id);
            v.Verificar("escolha do scanner não marca o executável como fixado",
                !gravado.ExecutavelFixadoPeloUsuario);
            v.Verificar("a biblioteca aceita salvar (passa na validação de portabilidade)",
                biblioteca.Validar().Count == 0, string.Join(" | ", biblioteca.Validar().ToArray()));

            // 2) Rescan do mesmo jogo não duplica nem mexe no que é meu.
            gravado.SegundosJogados = 8220;
            gravado.Favorito = true;
            gravado.Titulo = "Meu Título Escolhido";

            var resumo2 = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { new JogoRevisado(detectado) });

            v.Verificar("rescan não duplica o jogo", biblioteca.Jogos.Count == 1);
            v.Verificar("rescan sem mudança é reportado como tal", resumo2.SemMudanca.Count == 1);
            v.Verificar("tempo jogado preservado", gravado.SegundosJogados == 8220);
            v.Verificar("favorito preservado", gravado.Favorito);
            v.VerificarTexto("meu título não é sobrescrito pelo scanner",
                "Meu Título Escolhido", gravado.Titulo);

            // 3) Troco o executável no combo SEM travar: muda, mas não vira imune a rescan.
            var comTroca = new JogoRevisado(detectado);
            comTroca.Escolhido = detectado.Candidatos[1];
            var resumo3 = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { comTroca });

            v.Verificar("troca no combo atualiza o executável", resumo3.ExecutavelAtualizado.Count == 1);
            v.VerificarTexto("aponta para o executável que eu escolhi",
                @"Jogos\Jogo Gravado\Bin\outro.exe", gravado.ExecutavelRelativo);
            v.Verificar("trocar no combo NÃO trava contra rescan",
                !gravado.ExecutavelFixadoPeloUsuario);

            // 4) Sem trava, o rescan pode corrigir de volta. É o que eu quero enquanto a
            //    heurística ainda vai melhorar.
            var resumo4 = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { new JogoRevisado(detectado) });

            v.Verificar("rescan corrige o executável de um jogo destravado",
                resumo4.ExecutavelAtualizado.Count == 1);
            v.VerificarTexto("voltou para o que o scanner propõe",
                @"Jogos\Jogo Gravado\jogo gravado.exe", gravado.ExecutavelRelativo);

            // 5) Agora com a trava explícita: troco o executável E marco o cadeado.
            var comTrava = new JogoRevisado(detectado) { Fixar = true };
            comTrava.Escolhido = detectado.Candidatos[1];
            MescladorDeBiblioteca.Mesclar(biblioteca, new[] { comTrava });

            v.Verificar("marcar o cadeado grava executavelFixadoPeloUsuario",
                gravado.ExecutavelFixadoPeloUsuario);
            v.VerificarTexto("com o executável que eu escolhi",
                @"Jogos\Jogo Gravado\Bin\outro.exe", gravado.ExecutavelRelativo);

            // 6) Rescan de um jogo travado: a linha nasce com o cadeado marcado e apontando
            //    para o executável fixado, então confirmar não desfaz nada.
            using (var rescan = new FormRevisaoDoScan(new[] { detectado }, null, biblioteca))
            {
                v.Verificar("a linha do rescan já vem com o cadeado marcado", rescan.Linhas[0].Fixar);
                v.VerificarTexto("e apontando para o executável fixado, não para a proposta do scanner",
                    @"Bin\outro.exe", rescan.Linhas[0].Escolhido.CaminhoRelativoAoJogo);
                v.VerificarTexto("a coluna mostra o estado travado", "🔒 fixado", rescan.TextoDaLinha(0)[3]);

                var resumo6 = MescladorDeBiblioteca.Mesclar(biblioteca, rescan.Confirmados());

                v.Verificar("rescan respeita executavelFixadoPeloUsuario",
                    resumo6.MantidosPelaMarcaDeFixado.Count == 1);
                v.VerificarTexto("o executável fixado continua igual",
                    @"Jogos\Jogo Gravado\Bin\outro.exe", gravado.ExecutavelRelativo);
                v.Verificar("e continua travado", gravado.ExecutavelFixadoPeloUsuario);
            }

            // 7) Destravar é reversível: desmarco o cadeado e o rescan volta a corrigir.
            using (var destravando = new FormRevisaoDoScan(new[] { detectado }, null, biblioteca))
            {
                destravando.DefinirFixado(0, false);
                v.VerificarTexto("a coluna volta a mostrar destravado", "—", destravando.TextoDaLinha(0)[3]);

                var resumo7 = MescladorDeBiblioteca.Mesclar(biblioteca, destravando.Confirmados());

                v.Verificar("destravar é registrado no resumo", resumo7.Destravados.Count == 1);
                v.Verificar("a marca some do JSON", !gravado.ExecutavelFixadoPeloUsuario);
            }

            var resumo8 = MescladorDeBiblioteca.Mesclar(biblioteca, new[] { new JogoRevisado(detectado) });
            v.Verificar("destravado, o rescan seguinte pode corrigir de novo",
                resumo8.ExecutavelAtualizado.Count == 1);
            v.VerificarTexto("e corrigiu", @"Jogos\Jogo Gravado\jogo gravado.exe", gravado.ExecutavelRelativo);

            // 5) Linha desmarcada não entra.
            var desmarcado = new JogoRevisado(detectado) { Incluir = false };
            var bibliotecaVazia = new Biblioteca();
            MescladorDeBiblioteca.Mesclar(bibliotecaVazia, new[] { desmarcado });
            v.Verificar("linha desmarcada não é gravada", bibliotecaVazia.Jogos.Count == 0);

            // 6) Jogo fora da pasta do launcher é recusado, não gravado com caminho absoluto.
            var fsDeFora = new SistemaDeArquivosSimulado()
                .AdicionarArquivo(@"Z:\FioraDaRaiz\Jogo De Fora\jogo de fora.exe", 5_000_000);
            var deFora = new ScannerDeJogos(fsDeFora).AnalisarPastaDeJogo(@"Z:\FioraDaRaiz\Jogo De Fora");

            if (deFora is not null)
            {
                var bibliotecaDeFora = new Biblioteca();
                var resumoDeFora = MescladorDeBiblioteca.Mesclar(bibliotecaDeFora, new[] { new JogoRevisado(deFora) });

                v.Verificar("jogo de fora da raiz é recusado", resumoDeFora.ForaDaRaiz.Count == 1);
                v.Verificar("e não entra na biblioteca", bibliotecaDeFora.Jogos.Count == 0);
            }

            // 7) Títulos iguais geram ids diferentes.
            var bibliotecaComRepetidos = new Biblioteca();
            MescladorDeBiblioteca.Mesclar(bibliotecaComRepetidos, new[] { new JogoRevisado(detectado) });

            var outroCaminho = Path.Combine(sandbox, "Jogos", "Outra Pasta");
            var fs2 = new SistemaDeArquivosSimulado()
                .AdicionarArquivo(Path.Combine(outroCaminho, "outro jogo.exe"), 9_000_000);
            var segundo = new ScannerDeJogos(fs2).AnalisarPastaDeJogo(outroCaminho);

            if (segundo is not null)
            {
                var comMesmoTitulo = new JogoRevisado(segundo) { Titulo = "Jogo Gravado" };
                MescladorDeBiblioteca.Mesclar(bibliotecaComRepetidos, new[] { comMesmoTitulo });

                v.Verificar("dois jogos de mesmo título recebem ids distintos",
                    bibliotecaComRepetidos.Jogos.Count == 2 &&
                    bibliotecaComRepetidos.Jogos[0].Id != bibliotecaComRepetidos.Jogos[1].Id,
                    string.Join(", ", bibliotecaComRepetidos.Jogos.ConvertAll(j => j.Id).ToArray()));
            }

            // 8) Pastas escaneadas entram relativas.
            MescladorDeBiblioteca.RegistrarPastasEscaneadas(biblioteca, new[] { Path.Combine(sandbox, "Jogos") });
            MescladorDeBiblioteca.RegistrarPastasEscaneadas(biblioteca, new[] { Path.Combine(sandbox, "Jogos") });

            v.Verificar("pasta escaneada registrada uma vez só",
                biblioteca.PastasEscaneadas.Count == 1, string.Join(", ", biblioteca.PastasEscaneadas.ToArray()));
            v.VerificarTexto("e de forma relativa", "Jogos",
                biblioteca.PastasEscaneadas.Count > 0 ? biblioteca.PastasEscaneadas[0] : "(vazio)");

            // 9) O arquivo em disco não pode ter letra de drive nenhuma.
            Caminhos.GarantirEstrutura();
            biblioteca.Salvar();

            var conteudo = ArquivoTexto.Ler(Caminhos.ArquivoBiblioteca);
            v.Verificar("o biblioteca.json gravado não tem letra de drive",
                !System.Text.RegularExpressions.Regex.IsMatch(conteudo, @"[A-Za-z]:[\\/]"));
        }

        // ---- Geometria da janela ------------------------------------------------------------------

        /// <summary>
        /// Confere que dá para CONCLUIR o scan pela interface: os botões de confirmar e
        /// cancelar existem, estão visíveis e ficam dentro da área cliente.
        ///
        /// Este teste nasceu de um bug que passou por 373 outros: os dois botões eram
        /// posicionados com "ClientSize.Width - 172" e adicionados a um painel que ainda
        /// tinha o tamanho padrão de 200 px. O Anchor gravou a distância errada e os dois
        /// ficaram desenhados fora da janela — a janela abria, marcava, editava, e não
        /// tinha como gravar. Nenhum teste olhava geometria, então nenhum viu.
        /// </summary>
        private static void TestarGeometriaDaJanela(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Geometria da janela (os botões existem e cabem na tela)");

            using (var janela = AbrirRevisao(out _))
            {
                ExibirForaDaTela(janela);

                var adicionar = janela.BotaoAdicionar;
                var cancelar = janela.BotaoCancelar;

                v.Verificar("existe um botão de confirmar", adicionar != null);
                v.Verificar("existe um botão de cancelar", cancelar != null);
                if (adicionar is null || cancelar is null) return;

                v.Verificar("o de confirmar está visível", EhVisivelDeVerdade(adicionar));
                v.Verificar("o de cancelar está visível", EhVisivelDeVerdade(cancelar));

                v.Verificar("Enter confirma (AcceptButton)", ReferenceEquals(janela.AcceptButton, adicionar));
                v.Verificar("Esc cancela (CancelButton)", ReferenceEquals(janela.CancelButton, cancelar));

                ConferirDentroDaJanela(v, janela, adicionar, "botão de confirmar");
                ConferirDentroDaJanela(v, janela, cancelar, "botão de cancelar");
                ConferirDentroDaJanela(v, janela, janela.Rodape, "barra de status");

                // Sem isto, "dentro da área cliente" poderia estar aprovando qualquer coisa.
                // Um botão posicionado como o código antigo fazia (coordenada absoluta num
                // painel que ainda tinha 200 px) tem que ser reprovado.
                using (var forjado = new Button { Bounds = new Rectangle(janela.ClientSize.Width + 40, 8, 160, 30) })
                {
                    janela.Controls.Add(forjado);

                    v.Verificar("a checagem reprova um botão fora da janela (senão não prova nada)",
                        !janela.ClientRectangle.Contains(RetanguloNaJanela(forjado, janela)),
                        RetanguloNaJanela(forjado, janela).ToString());

                    janela.Controls.Remove(forjado);
                }

                // O texto conta quantos vão ser gravados, e some a possibilidade de gravar zero.
                v.Verificar("o texto do botão traz a contagem",
                    adicionar.Text.Contains(janela.Linhas.Count.ToString()), adicionar.Text);
                v.Verificar("com jogos marcados, dá para confirmar", adicionar.Enabled);

                janela.MarcarTodos(false);
                v.Verificar("sem nenhum marcado, o botão desabilita", !adicionar.Enabled);
                v.Verificar("e o texto vai a zero", adicionar.Text.Contains("0"), adicionar.Text);

                janela.MarcarTodos(true);
                v.Verificar("marcando de novo, ele volta", adicionar.Enabled);

                // O painel de detalhes não pode voltar a comer a janela.
                v.Verificar("o painel de detalhes é discreto (não toma meia janela)",
                    janela.PainelDeDetalhes.Height <= janela.ClientSize.Height / 4,
                    $"{janela.PainelDeDetalhes.Height} px de {janela.ClientSize.Height}");

                // E no tamanho mínimo, que é onde layout ruim aparece primeiro.
                janela.Size = janela.MinimumSize;
                ForcarLayout(janela);

                v.Verificar("no tamanho mínimo o de confirmar continua visível", EhVisivelDeVerdade(adicionar));
                ConferirDentroDaJanela(v, janela, adicionar, "botão de confirmar (tamanho mínimo)");
                ConferirDentroDaJanela(v, janela, cancelar, "botão de cancelar (tamanho mínimo)");
                ConferirDentroDaJanela(v, janela, janela.Rodape, "barra de status (tamanho mínimo)");
            }
        }

        /// <summary>
        /// Exibe a janela fora da área visível e roda o layout.
        ///
        /// Tem que ser exibida de verdade: Control.Visible devolve a visibilidade EFETIVA,
        /// então numa janela nunca mostrada todo filho responde false e o teste não
        /// distinguiria "botão escondido" de "janela fechada". Fora da tela e sem barra de
        /// tarefas ela não rouba o foco de ninguém.
        /// </summary>
        private static void ExibirForaDaTela(Form janela)
        {
            janela.StartPosition = FormStartPosition.Manual;
            janela.ShowInTaskbar = false;
            janela.Location = new Point(-32000, -32000);

            janela.Show();
            janela.PerformLayout();
            Application.DoEvents();
        }

        private static void ForcarLayout(Form janela)
        {
            janela.PerformLayout();
            Application.DoEvents();
        }

        /// <summary>Visível de verdade: ele e todos os pais.</summary>
        private static bool EhVisivelDeVerdade(Control controle) => controle.Visible;

        private static void ConferirDentroDaJanela(Verificador v, Form janela, Control controle, string nome)
        {
            var area = RetanguloNaJanela(controle, janela);
            var cliente = janela.ClientRectangle;

            v.Verificar($"{nome} dentro da área cliente",
                cliente.Contains(area),
                $"{nome} em {area}, área cliente {cliente}");
        }

        /// <summary>
        /// Retângulo do controle em coordenadas da janela, somando a posição de cada pai.
        /// É a conta que revela "está desenhado onde ninguém vê".
        /// </summary>
        private static Rectangle RetanguloNaJanela(Control controle, Form janela)
        {
            var area = controle.Bounds;

            for (var pai = controle.Parent; pai != null && !ReferenceEquals(pai, janela); pai = pai.Parent)
                area.Offset(pai.Location);

            return area;
        }

        // ---- Acervo sintético -------------------------------------------------------------------

        /// <summary>
        /// Monta o acervo difícil e devolve a janela de revisão já preenchida.
        /// A janela é criada mas nunca exibida: os testes chamam os mesmos métodos que
        /// os cliques chamam.
        /// </summary>
        private static FormRevisaoDoScan AbrirRevisao(out IReadOnlyList<PastaDescartada> descartes)
        {
            var fs = MontarAcervoSintetico();
            var scanner = new ScannerDeJogos(fs);
            var jogos = scanner.Escanear(new[] { Raiz });

            descartes = scanner.Descartes;
            return new FormRevisaoDoScan(jogos, descartes);
        }

        /// <summary>Raiz virtual do acervo sintético, para quem quiser escaneá-lo.</summary>
        public static string RaizSintetica => Raiz;

        /// <summary>
        /// O mesmo acervo difícil usado nos testes, exposto para o "--revisao-demo":
        /// serve para eu abrir a janela e ver com os próprios olhos a linha amarela, o
        /// combo cheio e a limpeza de título, sem precisar encher o HD antes.
        /// </summary>
        public static SistemaDeArquivosSimulado MontarAcervoSintetico()
        {
            var fs = new SistemaDeArquivosSimulado();

            // 1) Nome sujo de repack: tudo isso tem que sumir do título.
            fs.AdicionarArquivos(Raiz + @"\Cuphead_v1.2.3_[FitGirl]_REPACK_PT-BR",
                "Cuphead.exe|4.000.000", "unins000.exe|700.000");

            // 2) Outro nome sujo, com tag legítima que precisa sobreviver.
            fs.AdicionarArquivos(Raiz + @"\The Witcher (Enhanced Edition) (GOG)",
                "witcher.exe|8.000.000");

            // 3) Pasta com 15 candidatos, para o combo ficar cheio de verdade.
            var cheia = Raiz + @"\Jogo Cheio De Exes";
            fs.AdicionarArquivos(cheia, "jogo cheio de exes.exe|9.000.000");
            for (var i = 1; i <= 12; i++)
                fs.AdicionarArquivo($@"{cheia}\ferramenta{i.ToString("D2", CultureInfo.InvariantCulture)}.exe", 1_500_000);
            fs.AdicionarArquivos(cheia, "setup.exe|900.000.000", "unins000.exe|700.000");

            // 4) Jogo de placar baixo: exe minúsculo, em subpasta, com nome que não lembra
            //    a pasta em nada. O leiame.txt na raiz é o que marca a pasta de cima como
            //    sendo o jogo (senão "dados" viraria o jogo).
            fs.AdicionarArquivos(Raiz + @"\Jogo Duvidoso", "leiame.txt|400");
            fs.AdicionarArquivos(Raiz + @"\Jogo Duvidoso\dados", "xyz.exe|40.000");

            // 5) Pasta sem executável nenhum.
            fs.AdicionarArquivos(Raiz + @"\Só Leia-me", "leiame.txt|900", "capa.jpg|120.000");

            // 6) Pasta só com instalador — não é jogo, é repack por instalar.
            fs.AdicionarArquivos(Raiz + @"\Repack Não Instalado", "setup.exe|900.000.000");

            // 7) Um jogo comum, para a lista não ser só caso extremo.
            fs.AdicionarArquivos(Raiz + @"\Hollow Knight", "hollow knight.exe|30.000.000");

            return fs;
        }

        // ---- Apoio ---------------------------------------------------------------------------------

        private static int Linha(FormRevisaoDoScan janela, string tituloOuPasta)
        {
            for (var i = 0; i < janela.Linhas.Count; i++)
            {
                var linha = janela.Linhas[i];
                if (linha.Titulo.IndexOf(tituloOuPasta, StringComparison.OrdinalIgnoreCase) >= 0) return i;
                if (linha.Deteccao.NomeDaPasta.IndexOf(tituloOuPasta, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }

        private static string DescreverLinhas(FormRevisaoDoScan janela)
        {
            var titulos = new List<string>();
            foreach (var linha in janela.Linhas) titulos.Add(linha.Titulo);

            return titulos.Count == 0 ? "(nenhuma linha)" : string.Join(" | ", titulos.ToArray());
        }

        private static bool Contem(IReadOnlyList<PastaDescartada> descartes, string nomeDaPasta)
        {
            foreach (var descarte in descartes)
            {
                if (descarte.Pasta.IndexOf(nomeDaPasta, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
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
