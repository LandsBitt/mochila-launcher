// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Servidor de mentira para testar o provedor de capas sem chave real e sem rede.
    ///
    /// Guarda todas as requisições que passaram, e é isso que permite provar o que
    /// importa: que a chave viaja no header Authorization e não aparece em URL nenhuma.
    /// </summary>
    public sealed class HttpSimulado : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public HttpSimulado(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        /// <summary>Toda URL pedida, na ordem.</summary>
        public List<string> UrlsPedidas { get; } = new List<string>();

        /// <summary>Todo header Authorization visto, na ordem ("Bearer abc").</summary>
        public List<string> AutorizacoesVistas { get; } = new List<string>();

        public int Pedidos => UrlsPedidas.Count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao,
                                                               CancellationToken cancelamento)
        {
            // AbsoluteUri, não ToString(): ToString() DESESCAPA o caminho, então "%20"
            // viraria espaço e o teste de codificação não veria o que foi enviado de
            // verdade. Para procurar credencial vazada, também vale o texto real.
            UrlsPedidas.Add(requisicao.RequestUri?.AbsoluteUri ?? "");
            AutorizacoesVistas.Add(requisicao.Headers.Authorization?.ToString() ?? "");

            cancelamento.ThrowIfCancellationRequested();

            var resposta = _responder(requisicao);
            resposta.RequestMessage = requisicao;

            return Task.FromResult(resposta);
        }

        // ---- Respostas prontas -------------------------------------------------------------

        public static HttpResponseMessage Json(string corpo, HttpStatusCode status = HttpStatusCode.OK)
            => new HttpResponseMessage(status)
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json")
            };

        public static HttpResponseMessage Imagem(byte[] bytes, string tipo)
        {
            var conteudo = new ByteArrayContent(bytes);
            conteudo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = conteudo };
        }

        public static HttpResponseMessage Status(HttpStatusCode status)
            => new HttpResponseMessage(status) { Content = new StringContent("", Encoding.UTF8, "application/json") };

        /// <summary>Handler que sempre estoura o tempo (é como o HttpClient sinaliza timeout).</summary>
        public static HttpSimulado QueDemora()
            => new HttpSimulado(_ => throw new TaskCanceledException("tempo esgotado"));

        /// <summary>Handler que sempre falha por falta de rede.</summary>
        public static HttpSimulado SemRede()
            => new HttpSimulado(_ => throw new HttpRequestException("An error occurred while sending the request."));
    }
}
#endif   // DEBUG
