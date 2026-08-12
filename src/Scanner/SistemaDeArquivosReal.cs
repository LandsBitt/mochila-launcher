using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Launcher.Scanner
{
    /// <summary>
    /// Implementação que fala com o disco de verdade.
    ///
    /// Nenhum método lança: uma pasta protegida, um symlink quebrado ou o HD sendo
    /// removido no meio do scan viram "lista vazia", não um scan abortado.
    /// </summary>
    public sealed class SistemaDeArquivosReal : ISistemaDeArquivos
    {
        /// <summary>
        /// Cache de leitura de PE. Um mesmo .exe pode ser consultado mais de uma vez
        /// (candidato e desempate), e abrir arquivo em HD externo é caro.
        /// </summary>
        private readonly Dictionary<string, InfoExecutavel?> _cacheDeInfo =
            new Dictionary<string, InfoExecutavel?>(StringComparer.OrdinalIgnoreCase);

        // ---- Medição -----------------------------------------------------------------------
        //
        // Existe para responder "o scan está lento por quê?" com número, não com palpite:
        // HD externo com cache frio, listagem de diretório ou leitura de PE. Usa timestamp
        // cru em vez de Stopwatch para não alocar um objeto por chamada.

        private long _ticksDeListagem;
        private long _ticksDePe;
        private long _ticksDeMetadados;

        /// <summary>Quantidade de listagens de pasta (arquivos + subpastas).</summary>
        public int Listagens { get; private set; }

        /// <summary>Quantos .exe tiveram o cabeçalho PE lido de fato (fora do cache).</summary>
        public int ExecutaveisLidos { get; private set; }

        public int LeiturasDePeServidasPeloCache { get; private set; }

        public double MillisegundosDeListagem => _ticksDeListagem * 1000.0 / Stopwatch.Frequency;

        public double MillisegundosDePe => _ticksDePe * 1000.0 / Stopwatch.Frequency;

        /// <summary>Parte do tempo de PE gasta no FileVersionInfo, que abre o arquivo de novo.</summary>
        public double MillisegundosDeMetadados => _ticksDeMetadados * 1000.0 / Stopwatch.Frequency;

        public bool PastaExiste(string caminho)
        {
            try
            {
                return !string.IsNullOrEmpty(caminho) && Directory.Exists(caminho);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public IReadOnlyList<string> ListarSubpastas(string caminho)
        {
            var inicio = Stopwatch.GetTimestamp();
            Listagens++;
            try
            {
                var subpastas = Directory.GetDirectories(caminho);
                var resultado = new List<string>(subpastas.Length);

                foreach (var subpasta in subpastas)
                {
                    if (EhOculta(subpasta)) continue;
                    resultado.Add(subpasta);
                }
                return resultado;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
            finally
            {
                _ticksDeListagem += Stopwatch.GetTimestamp() - inicio;
            }
        }

        public IReadOnlyList<ArquivoEncontrado> ListarArquivos(string caminho)
        {
            var inicio = Stopwatch.GetTimestamp();
            Listagens++;
            try
            {
                var arquivos = Directory.GetFiles(caminho);
                var resultado = new List<ArquivoEncontrado>(arquivos.Length);

                foreach (var arquivo in arquivos)
                {
                    long tamanho;
                    try
                    {
                        tamanho = new FileInfo(arquivo).Length;
                    }
                    catch (Exception)
                    {
                        tamanho = 0;
                    }
                    resultado.Add(new ArquivoEncontrado(arquivo, tamanho));
                }
                return resultado;
            }
            catch (Exception)
            {
                return Array.Empty<ArquivoEncontrado>();
            }
            finally
            {
                _ticksDeListagem += Stopwatch.GetTimestamp() - inicio;
            }
        }

        public InfoExecutavel? LerInfoExecutavel(string caminho)
        {
            if (_cacheDeInfo.TryGetValue(caminho, out var emCache))
            {
                LeiturasDePeServidasPeloCache++;
                return emCache;
            }

            var inicio = Stopwatch.GetTimestamp();
            try
            {
                var info = LeitorPe.Ler(caminho);
                if (info != null)
                {
                    var inicioMetadados = Stopwatch.GetTimestamp();
                    PreencherMetadados(caminho, info);
                    _ticksDeMetadados += Stopwatch.GetTimestamp() - inicioMetadados;
                }

                _cacheDeInfo[caminho] = info;
                ExecutaveisLidos++;
                return info;
            }
            finally
            {
                _ticksDePe += Stopwatch.GetTimestamp() - inicio;
            }
        }

        /// <summary>FileDescription/ProductName vêm do recurso de versão, via API do .NET mesmo.</summary>
        private static void PreencherMetadados(string caminho, InfoExecutavel info)
        {
            try
            {
                var versao = FileVersionInfo.GetVersionInfo(caminho);
                info.FileDescription = Normalizar(versao.FileDescription);
                info.ProductName = Normalizar(versao.ProductName);
            }
            catch (Exception)
            {
                // Sem recurso de versão (comum em jogo antigo) o placar só ignora o sinal.
            }
        }

        private static string? Normalizar(string? texto)
            => string.IsNullOrWhiteSpace(texto) ? null : texto!.Trim();

        /// <summary>
        /// Pastas ocultas/de sistema ficam de fora: "System Volume Information", "$RECYCLE.BIN"
        /// e afins não têm jogo dentro e só fazem o scan demorar.
        /// </summary>
        private static bool EhOculta(string caminho)
        {
            try
            {
                var atributos = File.GetAttributes(caminho);
                return (atributos & (FileAttributes.Hidden | FileAttributes.System)) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
