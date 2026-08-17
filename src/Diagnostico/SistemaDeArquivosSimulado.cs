// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using Mochila.Scanner;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Sistema de arquivos de mentira, montado em memória a partir de uma listagem.
    ///
    /// Existe para os casos de teste obrigatórios do scanner rodarem em qualquer máquina,
    /// sem o HD de jogos plugado e sem criar 6 MB de arquivo falso só para testar o
    /// desempate por tamanho.
    /// </summary>
    public sealed class SistemaDeArquivosSimulado : ISistemaDeArquivos
    {
        private readonly Dictionary<string, List<string>> _subpastas =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<ArquivoEncontrado>> _arquivos =
            new Dictionary<string, List<ArquivoEncontrado>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, InfoExecutavel> _infos =
            new Dictionary<string, InfoExecutavel>(StringComparer.OrdinalIgnoreCase);

        // ---- Montagem ------------------------------------------------------------------------

        /// <summary>Cria a pasta e toda a cadeia de pais que faltar.</summary>
        public SistemaDeArquivosSimulado AdicionarPasta(string caminho)
        {
            var normalizado = Normalizar(caminho);
            if (_subpastas.ContainsKey(normalizado)) return this;

            _subpastas[normalizado] = new List<string>();
            _arquivos[normalizado] = new List<ArquivoEncontrado>();

            var pai = Path.GetDirectoryName(normalizado);
            if (!string.IsNullOrEmpty(pai))
            {
                AdicionarPasta(pai!);
                _subpastas[Normalizar(pai!)].Add(normalizado);
            }
            return this;
        }

        /// <summary>Cria um arquivo (e a pasta dele) com o tamanho informado.</summary>
        public SistemaDeArquivosSimulado AdicionarArquivo(string caminho, long tamanho = 0, InfoExecutavel? info = null)
        {
            var normalizado = Normalizar(caminho);
            var pasta = Path.GetDirectoryName(normalizado)
                        ?? throw new ArgumentException($"Arquivo sem pasta: {caminho}", nameof(caminho));

            AdicionarPasta(pasta);
            _arquivos[Normalizar(pasta)].Add(new ArquivoEncontrado(normalizado, tamanho));

            if (info != null) _infos[normalizado] = info;
            return this;
        }

        /// <summary>
        /// Adiciona vários arquivos de uma vez na mesma pasta, no formato "nome|tamanho".
        /// Deixa a listagem do teste parecida com o que eu vejo no Explorer.
        /// </summary>
        public SistemaDeArquivosSimulado AdicionarArquivos(string pasta, params string[] itens)
        {
            AdicionarPasta(pasta);

            foreach (var item in itens)
            {
                var partes = item.Split('|');
                var nome = partes[0].Trim();
                var tamanho = partes.Length > 1 ? long.Parse(partes[1].Trim().Replace(".", "")) : 0L;

                AdicionarArquivo(Path.Combine(pasta, nome), tamanho);
            }
            return this;
        }

        public SistemaDeArquivosSimulado DefinirInfo(string caminhoDoExe, InfoExecutavel info)
        {
            _infos[Normalizar(caminhoDoExe)] = info;
            return this;
        }

        // ---- ISistemaDeArquivos ----------------------------------------------------------------

        public bool PastaExiste(string caminho) => _subpastas.ContainsKey(Normalizar(caminho));

        public IReadOnlyList<string> ListarSubpastas(string caminho)
            => _subpastas.TryGetValue(Normalizar(caminho), out var lista) ? lista : (IReadOnlyList<string>)Array.Empty<string>();

        public IReadOnlyList<ArquivoEncontrado> ListarArquivos(string caminho)
            => _arquivos.TryGetValue(Normalizar(caminho), out var lista) ? lista : (IReadOnlyList<ArquivoEncontrado>)Array.Empty<ArquivoEncontrado>();

        /// <summary>
        /// Caminhos que tiveram o PE consultado, na ordem. O scanner evita esse I/O de
        /// propósito (é o que dominava o scan no HD externo), então os testes precisam
        /// conseguir afirmar quantas leituras aconteceram.
        /// </summary>
        public List<string> LeiturasDePe { get; } = new List<string>();

        /// <summary>Só devolve info para os arquivos em que o teste pediu explicitamente.</summary>
        public InfoExecutavel? LerInfoExecutavel(string caminho)
        {
            var normalizado = Normalizar(caminho);
            LeiturasDePe.Add(normalizado);

            return _infos.TryGetValue(normalizado, out var info) ? info : null;
        }

        private static string Normalizar(string caminho) => caminho.TrimEnd('\\', '/');
    }
}
#endif   // DEBUG
