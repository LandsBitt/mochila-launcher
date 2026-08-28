using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mochila.Capas
{
    /// <summary>Tamanho pedido ao provedor.</summary>
    public enum TamanhoDeCapa
    {
        /// <summary>
        /// Versão reduzida que o próprio serviço entrega pronta. É o que a grade usa 99%
        /// do tempo: baixar a arte cheia só para gerar miniatura gasta banda e memória
        /// à toa num notebook fraco.
        /// </summary>
        Miniatura = 0,

        /// <summary>Resolução cheia (600x900). Só quando eu peço a capa de verdade.</summary>
        Cheia = 1
    }

    /// <summary>
    /// Que arte se está pedindo. Os três endpoints do SteamGridDB são irmãos — mesmo
    /// envelope, mesma ordenação por voto —, e o que muda entre eles cabe numa tabela:
    /// o caminho, as dimensões e os formatos aceitos.
    ///
    /// Existir como enum (em vez de três métodos quase iguais) é o que impede a fase 14 de
    /// duplicar o tratamento de erro, o throttle e a checagem de envelope que a fase 6 já
    /// acertou.
    /// </summary>
    public enum TipoDeArte
    {
        /// <summary>Boxart 600x900. É o que a grade desenha.</summary>
        Capa = 0,

        /// <summary>Arte larga 1920x620, que vira o fundo desfocado da tela de detalhes.</summary>
        Hero = 1,

        /// <summary>
        /// Logo do jogo, <b>com transparência</b>. Único tipo restrito a PNG: convertido
        /// para jpg ele perde o alfa e vira um retângulo com fundo, que é o oposto do que
        /// um logo serve para fazer.
        /// </summary>
        Logo = 2
    }

    /// <summary>
    /// Por que a busca ou o download não deu certo. Existe para a interface saber a
    /// diferença entre "esse jogo não tem capa" e "sua chave está errada" — as duas
    /// coisas pedem reações opostas minhas.
    /// </summary>
    public enum FalhaDeCapa
    {
        Nenhuma = 0,

        /// <summary>Nenhuma chave configurada. Não é erro: é o caminho manual.</summary>
        SemChave,

        /// <summary>401/403: a chave existe e o serviço recusou.</summary>
        ChaveInvalida,

        /// <summary>404, lista vazia, ou success:false. O jogo simplesmente não está lá.</summary>
        NaoEncontrado,

        /// <summary>Sem internet, DNS, conexão recusada.</summary>
        SemRede,

        TempoEsgotado,

        /// <summary>Respondeu, mas não com o que promete (JSON quebrado, envelope estranho).</summary>
        RespostaInvalida,

        /// <summary>Eu cancelei.</summary>
        Cancelado,

        /// <summary>429 e afins: pedi demais, rápido demais.</summary>
        LimiteExcedido
    }

    /// <summary>Um jogo encontrado no catálogo do provedor.</summary>
    public sealed class JogoDeCapa
    {
        public JogoDeCapa(int id, string nome, string? lancamento = null)
        {
            Id = id;
            Nome = nome ?? "";
            Lancamento = lancamento;
        }

        public int Id { get; }

        public string Nome { get; }

        /// <summary>Ano/data de lançamento, quando o provedor informa. Ajuda a desempatar homônimos.</summary>
        public string? Lancamento { get; }

        public override string ToString()
            => string.IsNullOrEmpty(Lancamento) ? Nome : $"{Nome} ({Lancamento})";
    }

    /// <summary>Bytes de uma capa, já baixados.</summary>
    public sealed class CapaBaixada
    {
        public CapaBaixada(byte[] bytes, string extensao, string origem)
        {
            Bytes = bytes;
            Extensao = extensao;
            Origem = origem;
        }

        public byte[] Bytes { get; }

        /// <summary>
        /// ".png" ou ".jpg", vindo do Content-Type (ou da URL). Gravar tudo como .jpg
        /// cegamente corromperia metade das capas, que são PNG.
        /// </summary>
        public string Extensao { get; }

        /// <summary>De onde veio, para diagnóstico. NUNCA contém credencial.</summary>
        public string Origem { get; }
    }

    /// <summary>
    /// Resultado com motivo. O provedor não lança exceção por problema de rede: sem
    /// internet, sem capa e chave errada são situações previstas, e cada uma vira uma
    /// <see cref="FalhaDeCapa"/> aqui. Estourar exceção no meio de um lote de 200 jogos
    /// deixaria a biblioteca pela metade.
    /// </summary>
    public sealed class ResultadoDeCapa<T> where T : class
    {
        private ResultadoDeCapa(T? valor, FalhaDeCapa falha, string mensagem)
        {
            Valor = valor;
            Falha = falha;
            Mensagem = mensagem;
        }

        public T? Valor { get; }

        public FalhaDeCapa Falha { get; }

        /// <summary>
        /// Texto para mostrar na tela. Garantidamente sem a chave da API — nem inteira,
        /// nem em pedaço.
        /// </summary>
        public string Mensagem { get; }

        public bool DeuCerto => Falha == FalhaDeCapa.Nenhuma && Valor != null;

        public static ResultadoDeCapa<T> Certo(T valor) => new ResultadoDeCapa<T>(valor, FalhaDeCapa.Nenhuma, "");

        public static ResultadoDeCapa<T> Erro(FalhaDeCapa falha, string mensagem)
            => new ResultadoDeCapa<T>(null, falha, mensagem);
    }

    /// <summary>
    /// De onde vêm as capas automáticas.
    ///
    /// Tudo que fala com a internet mora atrás desta interface, para trocar de fonte de
    /// arte (ou acrescentar uma segunda) sem encostar na grade, na biblioteca ou na
    /// janela de configuração.
    ///
    /// Contrato que toda implementação tem que cumprir:
    ///  - nunca lança por causa de rede: devolve <see cref="FalhaDeCapa"/>;
    ///  - nunca põe credencial em URL, em mensagem de erro ou em log;
    ///  - respeita o CancellationToken, porque o lote é cancelável;
    ///  - com <see cref="Configurado"/> falso, não toca na rede.
    /// </summary>
    public interface ICapaProvider
    {
        /// <summary>Nome curto para aparecer na interface ("SteamGridDB").</summary>
        string Nome { get; }

        /// <summary>false quando falta chave/credencial. A interface pula direto para o manual.</summary>
        bool Configurado { get; }

        /// <summary>
        /// Procura o jogo pelo título. Devolve os candidatos, do mais provável para o
        /// menos — quando vêm nomes divergentes, eu escolho na tela em vez de o programa
        /// chutar.
        /// </summary>
        Task<ResultadoDeCapa<IReadOnlyList<JogoDeCapa>>> BuscarJogo(string termo, CancellationToken cancelamento);

        /// <summary>
        /// Baixa a melhor capa do jogo (a mais votada pela comunidade) no tamanho pedido.
        /// </summary>
        Task<ResultadoDeCapa<CapaBaixada>> BaixarCapa(int idDoJogo, TamanhoDeCapa tamanho, CancellationToken cancelamento);

        /// <summary>
        /// A mesma coisa, para qualquer uma das artes. <see cref="BaixarCapa"/> continua
        /// existindo porque é o caminho mais usado e o nome dele diz o que faz.
        ///
        /// Jogo sem hero ou sem logo no acervo do provedor é <see cref="FalhaDeCapa.NaoEncontrado"/>,
        /// não exceção: é o caso comum para jogo antigo, e não pode derrubar um lote.
        /// </summary>
        Task<ResultadoDeCapa<CapaBaixada>> BaixarArte(int idDoJogo, TipoDeArte tipo, TamanhoDeCapa tamanho,
                                                     CancellationToken cancelamento);
    }
}
