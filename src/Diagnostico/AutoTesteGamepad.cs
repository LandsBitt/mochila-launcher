// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using Mochila.Dados;
using Mochila.Entrada;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 8: gamepad por XInput.
    ///
    /// Nenhum teste daqui precisa de controle plugado — todos rodam contra uma fonte de
    /// estado falsa, que é a razão de <see cref="IEstadoBrutoDeGamepad"/> existir como
    /// interface. O relógio também é falso: conferir 400 ms de auto-repeat não pode
    /// custar 400 ms de suíte.
    /// </summary>
    public static class AutoTesteGamepad
    {
        private const string NomePastaSandbox = "_autoteste-gamepad-tmp";

        /// <summary>Instante fixo de referência. Qualquer um serve; o que importa é ser estável.</summary>
        private static readonly DateTime T0 = new DateTime(2026, 8, 13, 21, 0, 0, DateTimeKind.Utc);

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                TestarMapeamento(v);
                TestarDeadzone(v);
                TestarDiagonal(v);
                TestarDedupPorPacote(v);
                TestarAutoRepeat(v);
                TestarDpadEAnalogicoJuntos(v);
                TestarBotoes(v);
                TestarDescobertaEDesconexao(v);
                TestarRegraDoLauncherDormindo(v, raizReal);
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

        // ---- Mapeamento ------------------------------------------------------------------------

        private static void TestarMapeamento(Verificador v)
        {
            v.Verificar("A joga o selecionado",
                RoteadorDeComandos.Traduzir(BotaoDoGamepad.A, ContextoDeNavegacao.Grade)
                == ComandoDeNavegacao.Confirmar);

            v.Verificar("Y favorita",
                RoteadorDeComandos.Traduzir(BotaoDoGamepad.Y, ContextoDeNavegacao.Grade)
                == ComandoDeNavegacao.Favoritar);

            v.Verificar("Start abre as configurações",
                RoteadorDeComandos.Traduzir(BotaoDoGamepad.Start, ContextoDeNavegacao.Grade)
                == ComandoDeNavegacao.Menu);

            // O caso que justifica a tabela existir num lugar só: o mesmo gatilho, dois
            // comandos, decididos pelo contexto.
            v.Verificar("gatilho na grade troca a ordenação",
                RoteadorDeComandos.Traduzir(BotaoDoGamepad.Gatilhos, ContextoDeNavegacao.Grade)
                == ComandoDeNavegacao.TrocarOrdenacao);

            v.Verificar("gatilho no modo TV vira salto alfabético",
                RoteadorDeComandos.Traduzir(BotaoDoGamepad.Gatilhos, ContextoDeNavegacao.ModoTV)
                == ComandoDeNavegacao.SaltoAlfabetico);

            var roteador = new RoteadorDeComandos();
            var recebidos = new List<ComandoDeNavegacao>();
            roteador.Registrar(ContextoDeNavegacao.Grade, c => { recebidos.Add(c); return true; });

            roteador.Despachar(ComandoDeNavegacao.Confirmar);
            v.Verificar("o roteador entrega ao tratador do contexto atual",
                recebidos.Count == 1 && recebidos[0] == ComandoDeNavegacao.Confirmar);

            roteador.Contexto = ContextoDeNavegacao.Detalhes;
            v.Verificar("contexto sem tratador não estoura, só não trata",
                !roteador.Despachar(ComandoDeNavegacao.Confirmar) && recebidos.Count == 1);
        }

        // ---- Analógico -------------------------------------------------------------------------

        private static void TestarDeadzone(Verificador v)
        {
            var falso = GamepadFalso.ComControle();
            var nav = Preparar(falso);

            falso.Definir(x: 7000);
            v.Verificar("magnitude 7000 está dentro da deadzone (nenhum comando)",
                nav.Ler(T0).Count == 0);

            falso.Definir(x: 9000);
            var comandos = nav.Ler(T0);
            v.Verificar("magnitude 9000 vira Direita",
                comandos.Count == 1 && comandos[0] == ComandoDeNavegacao.Direita,
                Descrever(comandos));

            falso.Definir(x: -9000);
            comandos = nav.Ler(T0);
            v.Verificar("X negativo vira Esquerda",
                comandos.Count == 1 && comandos[0] == ComandoDeNavegacao.Esquerda,
                Descrever(comandos));

            // No XInput, Y positivo é para cima. Trocar isso deixa a grade andando ao
            // contrário do polegar, que é o tipo de bug que passa despercebido no código.
            falso.Definir(y: 20000);
            comandos = nav.Ler(T0);
            v.Verificar("Y positivo vira Cima",
                comandos.Count == 1 && comandos[0] == ComandoDeNavegacao.Cima,
                Descrever(comandos));

            falso.Definir(y: -20000);
            comandos = nav.Ler(T0);
            v.Verificar("Y negativo vira Baixo",
                comandos.Count == 1 && comandos[0] == ComandoDeNavegacao.Baixo,
                Descrever(comandos));
        }

        /// <summary>
        /// (6000, 6000) tem magnitude ~8485, acima do limiar de 7849, mas cada eixo
        /// sozinho está abaixo dele. Com deadzone quadrada isto emitiria dois comandos e
        /// a seleção andaria na diagonal — daí a exigência de deadzone radial.
        /// </summary>
        private static void TestarDiagonal(Verificador v)
        {
            var falso = GamepadFalso.ComControle();
            var nav = Preparar(falso);

            falso.Definir(x: 6000, y: 6000);
            var comandos = nav.Ler(T0);

            v.Verificar("diagonal acima do limiar emite UM comando, não dois",
                comandos.Count == 1, Descrever(comandos));
        }

        private static void TestarDedupPorPacote(Verificador v)
        {
            var falso = GamepadFalso.ComControle();
            var nav = Preparar(falso);

            falso.Definir(x: 20000);
            v.Verificar("a primeira leitura do empurrão emite", nav.Ler(T0).Count == 1);

            // Sem chamar Definir de novo: o pacote continua o mesmo, que é o que o XInput
            // devolve com o controle parado.
            v.Verificar("pacote repetido não emite, mesmo com o stick fora da deadzone",
                nav.Ler(T0).Count == 0);

            v.Verificar("e continua não emitindo enquanto o repeat não vence",
                nav.Ler(T0.AddMilliseconds(399)).Count == 0);
        }

        private static void TestarAutoRepeat(Verificador v)
        {
            var falso = GamepadFalso.ComControle();
            var nav = Preparar(falso);

            falso.Definir(x: 20000);
            v.Verificar("cruzar o limiar emite o 1º", nav.Ler(T0).Count == 1);
            v.Verificar("aos 399 ms ainda não repete", nav.Ler(T0.AddMilliseconds(399)).Count == 0);
            v.Verificar("aos 400 ms sai o 2º", nav.Ler(T0.AddMilliseconds(400)).Count == 1);
            v.Verificar("119 ms depois do 2º ainda não", nav.Ler(T0.AddMilliseconds(519)).Count == 0);
            v.Verificar("120 ms depois do 2º sai o 3º", nav.Ler(T0.AddMilliseconds(520)).Count == 1);
            v.Verificar("e o 4º sai 120 ms depois", nav.Ler(T0.AddMilliseconds(640)).Count == 1);

            // Soltar zera: o próximo empurrão volta a emitir na hora, não no meio do ciclo.
            falso.Definir();
            v.Verificar("soltar não emite nada", nav.Ler(T0.AddMilliseconds(700)).Count == 0);

            falso.Definir(x: 20000);
            v.Verificar("empurrar de novo emite na hora", nav.Ler(T0.AddMilliseconds(710)).Count == 1);
            v.Verificar("e a espera longa recomeça do zero",
                nav.Ler(T0.AddMilliseconds(1000)).Count == 0);
        }

        /// <summary>
        /// D-pad e analógico compartilham a mesma máquina de repeat. Sem isso, segurar os
        /// dois ao mesmo tempo — que acontece sem querer — andaria com o dobro da
        /// velocidade.
        /// </summary>
        private static void TestarDpadEAnalogicoJuntos(Verificador v)
        {
            var falso = GamepadFalso.ComControle();
            var nav = Preparar(falso);

            falso.Definir(botoes: BitsDpadDireita, x: 20000);
            v.Verificar("os dois juntos emitem UM comando", nav.Ler(T0).Count == 1);
            v.Verificar("aos 399 ms nenhum dos dois repete", nav.Ler(T0.AddMilliseconds(399)).Count == 0);
            v.Verificar("aos 400 ms sai um só, não dois", nav.Ler(T0.AddMilliseconds(400)).Count == 1);

            // Trocar de d-pad para analógico na mesma direção não pode reiniciar a
            // contagem: para a mão, nada aconteceu.
            falso.Definir(x: 20000);
            v.Verificar("largar o d-pad e continuar no analógico não reinicia",
                nav.Ler(T0.AddMilliseconds(450)).Count == 0);
            v.Verificar("o ciclo de 120 ms continua de onde estava",
                nav.Ler(T0.AddMilliseconds(520)).Count == 1);
        }

        private static void TestarBotoes(Verificador v)
        {
            var falso = GamepadFalso.ComControle();
            var nav = Preparar(falso);

            falso.Definir(botoes: BitsA);
            var comandos = nav.Ler(T0);
            v.Verificar("A vira Confirmar",
                comandos.Count == 1 && comandos[0] == ComandoDeNavegacao.Confirmar, Descrever(comandos));

            // Segurar não repete: A repetido lançaria o jogo várias vezes.
            v.Verificar("segurar A não repete", nav.Ler(T0.AddMilliseconds(2000)).Count == 0);

            falso.Definir();
            nav.Ler(T0.AddMilliseconds(2100));

            falso.Definir(lt: 200);
            comandos = nav.Ler(T0.AddMilliseconds(2200));
            v.Verificar("LT passa do limiar e troca a ordenação",
                comandos.Count == 1 && comandos[0] == ComandoDeNavegacao.TrocarOrdenacao, Descrever(comandos));

            falso.Definir(lt: 10);
            v.Verificar("gatilho de leve (abaixo de 30) não conta",
                nav.Ler(T0.AddMilliseconds(2300)).Count == 0);

            falso.Definir(botoes: BitsBack);
            v.Verificar("Back não faz nada nesta fase (reservado para o modo TV)",
                nav.Ler(T0.AddMilliseconds(2400)).Count == 0);
        }

        private static void TestarDescobertaEDesconexao(Verificador v)
        {
            var falso = new GamepadFalso();
            var nav = new GamepadNavegacao(falso, b => RoteadorDeComandos.Traduzir(b, ContextoDeNavegacao.Grade));

            v.Verificar("sem controle, a varredura é de 2 s",
                nav.IntervaloSugeridoEmMs == GamepadNavegacao.IntervaloDeVarreduraEmMs);
            v.Verificar("e nenhum slot está ativo", nav.SlotAtivo < 0);

            nav.Ler(T0);
            v.Verificar("varrer sem nada plugado não emite comando", nav.Ler(T0).Count == 0);

            // Plugou no slot 2 com o launcher aberto: tem que ser achado sem reiniciar nada.
            falso.Definir(slot: 2);
            nav.Ler(T0.AddSeconds(2));

            v.Verificar("controle plugado é achado na varredura", nav.SlotAtivo == 2);
            v.Verificar("com controle, o poll passa a ser de 60 ms",
                nav.IntervaloSugeridoEmMs == GamepadNavegacao.IntervaloDePollEmMs);

            // A leitura que descobre o controle é linha de base: o que já estava
            // pressionado na hora de plugar não é comando meu.
            var falsoTorto = new GamepadFalso();
            falsoTorto.Definir(x: 30000);
            var navTorto = new GamepadNavegacao(falsoTorto, b => RoteadorDeComandos.Traduzir(b, ContextoDeNavegacao.Grade));
            v.Verificar("plugar com o analógico torto não sai andando sozinho",
                navTorto.Ler(T0).Count == 0);

            // Desplugou no meio do uso.
            falso.Desconectar(2);
            nav.Ler(T0.AddSeconds(3));

            v.Verificar("slot desconectado volta imediatamente para a varredura de 2 s",
                nav.IntervaloSugeridoEmMs == GamepadNavegacao.IntervaloDeVarreduraEmMs);
            v.Verificar("e nenhum slot fica ativo", nav.SlotAtivo < 0);
        }

        // ---- A regra que mais importa ----------------------------------------------------------

        /// <summary>
        /// "Enquanto um jogo roda, o launcher dorme" é a regra que o gamepad tinha tudo
        /// para quebrar: é a primeira coisa neste processo capaz de acordar sozinha.
        /// </summary>
        private static void TestarRegraDoLauncherDormindo(Verificador v, string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);
            Caminhos.DefinirPastaBase(sandbox);

            try
            {
                using (var janela = new FormPrincipal())
                {
                    janela.UsarFonteDeGamepadParaDiagnostico(GamepadFalso.ComControle());

                    janela.IniciarGamepadParaDiagnostico();
                    v.Verificar("com a janela na frente, o polling está ligado",
                        janela.GamepadPollandoParaDiagnostico);

                    janela.SairDaFrenteDoJogoParaDiagnostico();
                    v.Verificar("com o jogo aberto, o timer do gamepad está PARADO",
                        !janela.GamepadPollandoParaDiagnostico);

                    janela.IniciarGamepadParaDiagnostico();
                    janela.SimularDesativacaoParaDiagnostico();
                    v.Verificar("alt-tab (Deactivate) também para o polling",
                        !janela.GamepadPollandoParaDiagnostico);

                    janela.IniciarGamepadParaDiagnostico();
                    v.Verificar("e voltar para a janela liga de novo",
                        janela.GamepadPollandoParaDiagnostico);
                }
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
            }
        }

        // ---- Apoio -----------------------------------------------------------------------------

        private const ushort BitsDpadDireita = 0x0008;
        private const ushort BitsBack = 0x0020;
        private const ushort BitsA = 0x1000;

        /// <summary>
        /// A primeira leitura só descobre o controle e vira linha de base — todo teste
        /// começa depois dela.
        /// </summary>
        private static GamepadNavegacao Preparar(GamepadFalso falso)
        {
            var nav = new GamepadNavegacao(falso, b => RoteadorDeComandos.Traduzir(b, ContextoDeNavegacao.Grade));
            nav.Ler(T0);
            return nav;
        }

        private static string Descrever(IList<ComandoDeNavegacao> comandos)
        {
            if (comandos.Count == 0) return "(nenhum)";

            var nomes = new string[comandos.Count];
            for (var i = 0; i < comandos.Count; i++) nomes[i] = comandos[i].ToString();
            return string.Join(", ", nomes);
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

        /// <summary>
        /// Um controle de mentira. O pacote é incrementado a cada mudança, igual ao
        /// XInput de verdade — e NÃO é incrementado quando nada muda, que é justamente o
        /// caso que o dedup precisa enxergar.
        /// </summary>
        private sealed class GamepadFalso : IEstadoBrutoDeGamepad
        {
            private readonly Dictionary<int, EstadoBrutoDeGamepad> _slots =
                new Dictionary<int, EstadoBrutoDeGamepad>();

            public int Slots => 4;

            /// <summary>Um controle neutro no slot 0, que é o caso da maioria dos testes.</summary>
            public static GamepadFalso ComControle()
            {
                var falso = new GamepadFalso();
                falso.Definir();
                return falso;
            }

            public bool TentarLer(int slot, out EstadoBrutoDeGamepad estado)
                => _slots.TryGetValue(slot, out estado);

            public void Definir(int slot = 0, ushort botoes = 0, short x = 0, short y = 0,
                                byte lt = 0, byte rt = 0)
            {
                _slots.TryGetValue(slot, out var anterior);

                _slots[slot] = new EstadoBrutoDeGamepad
                {
                    Pacote = anterior.Pacote + 1,
                    Botoes = botoes,
                    GatilhoEsquerdo = lt,
                    GatilhoDireito = rt,
                    AnalogicoEsquerdoX = x,
                    AnalogicoEsquerdoY = y
                };
            }

            public void Desconectar(int slot) => _slots.Remove(slot);
        }
    }
}
#endif   // DEBUG
