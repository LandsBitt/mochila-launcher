using System;
using System.Collections.Generic;

namespace Mochila.Entrada
{
    /// <summary>
    /// Transforma estado bruto de controle em comandos: deadzone, auto-repeat e
    /// borda de subida. Não conhece janela, não conhece grade, não toca em disco —
    /// é uma máquina de estado pura, e por isso o <c>--autoteste</c> consegue cobri-la
    /// inteira sem nenhum controle plugado.
    ///
    /// O relógio entra por parâmetro (<see cref="Ler"/> recebe "agora") em vez de sair de
    /// <c>DateTime.Now</c> aqui dentro: testar 400 ms de espera não pode custar 400 ms de
    /// teste.
    /// </summary>
    public sealed class GamepadNavegacao
    {
        /// <summary>XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE, da documentação do XInput.</summary>
        public const int LimiarDoAnalogico = 7849;

        /// <summary>XINPUT_GAMEPAD_TRIGGER_THRESHOLD.</summary>
        public const int LimiarDoGatilho = 30;

        /// <summary>Sem controle achado: varredura preguiçosa dos 4 slots.</summary>
        public const int IntervaloDeVarreduraEmMs = 2000;

        /// <summary>Com controle: leitura de um slot só.</summary>
        public const int IntervaloDePollEmMs = 60;

        public const int EsperaDoPrimeiroRepeatEmMs = 400;
        public const int IntervaloDoRepeatEmMs = 120;

        private readonly IEstadoBrutoDeGamepad _fonte;
        private readonly Func<BotaoDoGamepad, ComandoDeNavegacao> _traduzir;
        private readonly List<ComandoDeNavegacao> _comandos = new List<ComandoDeNavegacao>(4);

        private int _slotAtivo = -1;
        private uint _ultimoPacote;
        private ushort _botoesAnteriores;
        private bool _gatilhoAnterior;

        private BotaoDoGamepad _direcaoMantida = BotaoDoGamepad.Nenhum;
        private DateTime _proximoRepeat;

        public GamepadNavegacao(IEstadoBrutoDeGamepad fonte, Func<BotaoDoGamepad, ComandoDeNavegacao> traduzir)
        {
            _fonte = fonte ?? throw new ArgumentNullException(nameof(fonte));
            _traduzir = traduzir ?? throw new ArgumentNullException(nameof(traduzir));
        }

        /// <summary>Slot em uso, ou -1 quando não há controle.</summary>
        public int SlotAtivo => _slotAtivo;

        /// <summary>
        /// De quanto em quanto tempo vale chamar <see cref="Ler"/>. Quem tem o timer lê
        /// isto depois de cada leitura e se ajusta.
        ///
        /// A diferença não é detalhe: <c>XInputGetState</c> num slot vazio custa na ordem
        /// de milissegundos, e quatro deles a cada 60 ms com nada plugado é CPU roubada
        /// de um notebook fraco — exatamente o que o launcher promete não fazer.
        /// </summary>
        public int IntervaloSugeridoEmMs => _slotAtivo < 0 ? IntervaloDeVarreduraEmMs : IntervaloDePollEmMs;

        /// <summary>
        /// Uma leitura. Devolve os comandos gerados neste instante — quase sempre nenhum.
        ///
        /// A lista devolvida é reaproveitada entre chamadas (nada de alocar a cada 60 ms);
        /// quem chama consome na hora.
        /// </summary>
        public IList<ComandoDeNavegacao> Ler(DateTime agora)
        {
            _comandos.Clear();

            if (_slotAtivo < 0)
            {
                ProcurarControle();
                return _comandos;
            }

            if (!_fonte.TentarLer(_slotAtivo, out var estado))
            {
                // Desplugou no meio do uso: volta imediatamente para a varredura lenta.
                Reiniciar();
                return _comandos;
            }

            // Pacote igual = estado idêntico ao da leitura anterior: não há nada novo para
            // interpretar, e a conta de deadzone e as bordas de subida ficam de fora.
            //
            // O auto-repeat NÃO fica de fora, e isso é de propósito: o XInput só incrementa
            // o pacote quando o estado muda, então segurar uma direção parada produz pacote
            // repetido para sempre. Se o repeat estivesse atrás desta guarda, ele nunca
            // dispararia com um controle de verdade na mão.
            if (estado.Pacote != _ultimoPacote)
            {
                _ultimoPacote = estado.Pacote;
                InterpretarBotoes(estado);
                AtualizarDirecao(DirecaoDe(estado), agora);
            }

            RepetirDirecao(agora);
            return _comandos;
        }

        /// <summary>
        /// Esquece o controle e o que estava sendo segurado. Chamado quando o timer para
        /// (alt-tab, jogo abrindo): voltar para o launcher não pode herdar uma direção
        /// pressionada de dez minutos atrás e sair repetindo sozinho.
        /// </summary>
        public void Reiniciar()
        {
            _slotAtivo = -1;
            _ultimoPacote = 0;
            _botoesAnteriores = 0;
            _gatilhoAnterior = false;
            _direcaoMantida = BotaoDoGamepad.Nenhum;
            _proximoRepeat = DateTime.MinValue;
        }

        // ---- Descoberta ------------------------------------------------------------------------

        /// <summary>
        /// Varre os slots atrás de um controle. O primeiro que responder vira o ativo, e a
        /// leitura dele serve só de linha de base: o que já estava pressionado no instante
        /// em que eu pluguei não é comando meu — senão plugar com o analógico torto sai
        /// andando pela grade sozinho.
        /// </summary>
        private void ProcurarControle()
        {
            for (var slot = 0; slot < _fonte.Slots; slot++)
            {
                if (!_fonte.TentarLer(slot, out var estado)) continue;

                _slotAtivo = slot;
                _ultimoPacote = estado.Pacote;
                _botoesAnteriores = estado.Botoes;
                _gatilhoAnterior = GatilhoPressionado(estado);
                _direcaoMantida = BotaoDoGamepad.Nenhum;
                _proximoRepeat = DateTime.MinValue;
                return;
            }
        }

        // ---- Direção ---------------------------------------------------------------------------

        /// <summary>
        /// A direção de agora, vinda do d-pad ou do analógico esquerdo — nunca das duas.
        /// Uma direção só por leitura: diagonal precisa escolher um lado, senão um empurrão
        /// na quina anda duas casas.
        /// </summary>
        private static BotaoDoGamepad DirecaoDe(EstadoBrutoDeGamepad estado)
        {
            // D-pad primeiro: quem usa d-pad quer precisão, e ele não tem deadzone.
            if ((estado.Botoes & BitsDoXInput.DPadCima) != 0) return BotaoDoGamepad.Cima;
            if ((estado.Botoes & BitsDoXInput.DPadBaixo) != 0) return BotaoDoGamepad.Baixo;
            if ((estado.Botoes & BitsDoXInput.DPadEsquerda) != 0) return BotaoDoGamepad.Esquerda;
            if ((estado.Botoes & BitsDoXInput.DPadDireita) != 0) return BotaoDoGamepad.Direita;

            // Deadzone RADIAL: compara a magnitude do vetor, não cada eixo. Com deadzone
            // por eixo (quadrada), a diagonal passa nos dois e vira dois comandos.
            double x = estado.AnalogicoEsquerdoX;
            double y = estado.AnalogicoEsquerdoY;

            if (x * x + y * y < (double)LimiarDoAnalogico * LimiarDoAnalogico)
                return BotaoDoGamepad.Nenhum;

            // Eixo dominante. No empate (diagonal exata) manda o vertical — o que importa
            // é sair um comando só, e não qual dos dois.
            if (Math.Abs(x) > Math.Abs(y))
                return x > 0 ? BotaoDoGamepad.Direita : BotaoDoGamepad.Esquerda;

            // No XInput, Y positivo é para cima.
            return y > 0 ? BotaoDoGamepad.Cima : BotaoDoGamepad.Baixo;
        }

        /// <summary>
        /// Borda de subida da direção: só cruzar o limiar emite. Enquanto continua
        /// segurando, quem manda é <see cref="RepetirDirecao"/>.
        /// </summary>
        private void AtualizarDirecao(BotaoDoGamepad direcao, DateTime agora)
        {
            if (direcao == BotaoDoGamepad.Nenhum)
            {
                _direcaoMantida = BotaoDoGamepad.Nenhum;
                return;
            }

            // Mesma direção de antes: pode ter trocado d-pad por analógico no meio, e isso
            // não pode reiniciar a contagem — nem dobrar a velocidade se os dois estiverem
            // pressionados juntos.
            if (direcao == _direcaoMantida) return;

            _direcaoMantida = direcao;
            _proximoRepeat = agora.AddMilliseconds(EsperaDoPrimeiroRepeatEmMs);
            Emitir(direcao);
        }

        /// <summary>
        /// Auto-repeat: 400 ms de espera e depois um a cada 120 ms. Sem isso, atravessar
        /// 200 capas exige 200 toques; com pressa demais, um toque atravessa a grade.
        /// </summary>
        private void RepetirDirecao(DateTime agora)
        {
            if (_direcaoMantida == BotaoDoGamepad.Nenhum) return;
            if (agora < _proximoRepeat) return;

            _proximoRepeat = agora.AddMilliseconds(IntervaloDoRepeatEmMs);
            Emitir(_direcaoMantida);
        }

        // ---- Botões ----------------------------------------------------------------------------

        /// <summary>
        /// Botão dispara na borda de subida e não repete: segurar A não pode lançar o jogo
        /// dez vezes. Os bits do d-pad passam batido aqui de propósito — direção é assunto
        /// da máquina de repeat.
        /// </summary>
        private void InterpretarBotoes(EstadoBrutoDeGamepad estado)
        {
            var novos = (ushort)(estado.Botoes & ~_botoesAnteriores);
            _botoesAnteriores = estado.Botoes;

            if ((novos & BitsDoXInput.A) != 0) Emitir(BotaoDoGamepad.A);
            if ((novos & BitsDoXInput.B) != 0) Emitir(BotaoDoGamepad.B);
            if ((novos & BitsDoXInput.X) != 0) Emitir(BotaoDoGamepad.X);
            if ((novos & BitsDoXInput.Y) != 0) Emitir(BotaoDoGamepad.Y);
            if ((novos & BitsDoXInput.OmbroEsquerdo) != 0) Emitir(BotaoDoGamepad.OmbroEsquerdo);
            if ((novos & BitsDoXInput.OmbroDireito) != 0) Emitir(BotaoDoGamepad.OmbroDireito);
            if ((novos & BitsDoXInput.Start) != 0) Emitir(BotaoDoGamepad.Start);
            if ((novos & BitsDoXInput.Back) != 0) Emitir(BotaoDoGamepad.Back);

            // Gatilho é analógico, mas para navegar vale como botão: ou passou do limiar
            // ou não passou. Os dois fazem a mesma coisa, então contam como um só.
            var gatilhoAgora = GatilhoPressionado(estado);
            if (gatilhoAgora && !_gatilhoAnterior) Emitir(BotaoDoGamepad.Gatilhos);
            _gatilhoAnterior = gatilhoAgora;
        }

        private static bool GatilhoPressionado(EstadoBrutoDeGamepad estado)
            => estado.GatilhoEsquerdo >= LimiarDoGatilho || estado.GatilhoDireito >= LimiarDoGatilho;

        private void Emitir(BotaoDoGamepad botao)
        {
            var comando = _traduzir(botao);
            if (comando != ComandoDeNavegacao.Nenhum) _comandos.Add(comando);
        }
    }
}
