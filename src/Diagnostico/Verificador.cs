// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;

namespace Launcher.Diagnostico
{
    /// <summary>
    /// Contador de asserções compartilhado pelas suítes de auto-teste.
    /// Não é framework de teste nenhum — é o mínimo para "Launcher.exe --autoteste"
    /// dizer o que passou e o que falhou, sem NuGet e sem projeto separado.
    /// </summary>
    public sealed class Verificador
    {
        private readonly Action<string> _saida;

        public Verificador(Action<string> saida)
        {
            _saida = saida ?? (_ => { });
        }

        public int Passou { get; private set; }

        public int Falhou { get; private set; }

        public bool TudoPassou => Falhou == 0;

        public void Escrever(string linha) => _saida(linha);

        public void Verificar(string descricao, bool condicao, string? detalhe = null)
        {
            if (condicao)
            {
                Passou++;
                _saida($"  ok    {descricao}");
            }
            else
            {
                Falhou++;
                _saida($"  FALHA {descricao}{(string.IsNullOrEmpty(detalhe) ? "" : $"  -> obtido: {detalhe}")}");
            }
        }

        /// <summary>Compara dois textos e já mostra o obtido quando diverge.</summary>
        public void VerificarTexto(string descricao, string? esperado, string? obtido)
            => Verificar($"{descricao} (esperado: \"{esperado}\")",
                         string.Equals(esperado, obtido, StringComparison.Ordinal),
                         $"\"{obtido}\"");

        public void RegistrarErro(string mensagem)
        {
            Falhou++;
            _saida(mensagem);
        }
    }
}
#endif   // DEBUG
