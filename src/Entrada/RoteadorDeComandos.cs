using System;
using System.Collections.Generic;

namespace Mochila.Entrada
{
    /// <summary>
    /// O lugar único onde está escrito o que cada botão faz, e para onde o comando vai.
    ///
    /// Duas responsabilidades que precisam morar juntas:
    ///
    /// 1. <b>Traduzir</b> botão físico em <see cref="ComandoDeNavegacao"/>, por contexto.
    ///    O mesmo LT/RT é "trocar ordenação" na grade e "salto alfabético" no modo TV —
    ///    espalhar isso por dois arquivos é como as duas versões saem de sincronia.
    /// 2. <b>Despachar</b> o comando para quem manda naquele contexto.
    ///
    /// A partir da fase 9 o teclado entra aqui também, e aí existe um caminho de navegação
    /// só. Enquanto isso, o teclado continua pelo <c>RoteadorDeTeclas</c> e este roteador
    /// atende só o gamepad.
    /// </summary>
    public sealed class RoteadorDeComandos
    {
        private readonly Dictionary<ContextoDeNavegacao, Func<ComandoDeNavegacao, bool>> _tratadores =
            new Dictionary<ContextoDeNavegacao, Func<ComandoDeNavegacao, bool>>();

        /// <summary>Onde o launcher está agora. Quem abre a tela nova troca isto.</summary>
        public ContextoDeNavegacao Contexto { get; set; } = ContextoDeNavegacao.Grade;

        public void Registrar(ContextoDeNavegacao contexto, Func<ComandoDeNavegacao, bool> tratador)
        {
            if (tratador is null) throw new ArgumentNullException(nameof(tratador));
            _tratadores[contexto] = tratador;
        }

        /// <summary>
        /// Entrega o comando a quem cuida do contexto atual. Devolve false quando ninguém
        /// tratou — comando sem dono não é erro, é feature que ainda não chegou (por
        /// exemplo, <see cref="ComandoDeNavegacao.Detalhes"/> antes da fase 11).
        /// </summary>
        public bool Despachar(ComandoDeNavegacao comando)
        {
            if (comando == ComandoDeNavegacao.Nenhum) return false;

            return _tratadores.TryGetValue(Contexto, out var tratador) && tratador(comando);
        }

        /// <summary>Traduz no contexto atual.</summary>
        public ComandoDeNavegacao Traduzir(BotaoDoGamepad botao) => Traduzir(botao, Contexto);

        /// <summary>
        /// A tabela de mapeamento, inteira. Estática de propósito: dá para conferir o
        /// mapeamento no teste sem instanciar roteador nenhum.
        /// </summary>
        public static ComandoDeNavegacao Traduzir(BotaoDoGamepad botao, ContextoDeNavegacao contexto)
        {
            switch (botao)
            {
                case BotaoDoGamepad.Cima: return ComandoDeNavegacao.Cima;
                case BotaoDoGamepad.Baixo: return ComandoDeNavegacao.Baixo;
                case BotaoDoGamepad.Esquerda: return ComandoDeNavegacao.Esquerda;
                case BotaoDoGamepad.Direita: return ComandoDeNavegacao.Direita;

                case BotaoDoGamepad.A: return ComandoDeNavegacao.Confirmar;
                case BotaoDoGamepad.B: return ComandoDeNavegacao.Voltar;
                case BotaoDoGamepad.X: return ComandoDeNavegacao.Detalhes;
                case BotaoDoGamepad.Y: return ComandoDeNavegacao.Favoritar;

                case BotaoDoGamepad.OmbroEsquerdo: return ComandoDeNavegacao.PaginaAnterior;
                case BotaoDoGamepad.OmbroDireito: return ComandoDeNavegacao.PaginaSeguinte;

                case BotaoDoGamepad.Start: return ComandoDeNavegacao.Menu;

                // O único que já muda de contexto — e a razão de a tabela existir.
                case BotaoDoGamepad.Gatilhos:
                    return contexto == ContextoDeNavegacao.ModoTV
                        ? ComandoDeNavegacao.SaltoAlfabetico
                        : ComandoDeNavegacao.TrocarOrdenacao;

                // Reservado para entrar/sair do modo TV na fase 18.
                case BotaoDoGamepad.Back: return ComandoDeNavegacao.Nenhum;

                default: return ComandoDeNavegacao.Nenhum;
            }
        }
    }
}
