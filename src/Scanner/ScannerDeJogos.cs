using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Mochila.Scanner
{
    /// <summary>Um jogo identificado pelo scanner, ainda não gravado na biblioteca.</summary>
    public sealed class JogoDetectado
    {
        public JogoDetectado(string pastaDoJogo, string tituloProposto, List<CandidatoExecutavel> candidatos)
        {
            PastaDoJogo = pastaDoJogo;
            TituloProposto = tituloProposto;
            Candidatos = candidatos;
        }

        /// <summary>Caminho completo da pasta que o scanner considerou "um jogo".</summary>
        public string PastaDoJogo { get; }

        public string NomeDaPasta => Path.GetFileName(PastaDoJogo.TrimEnd('\\', '/'));

        public string TituloProposto { get; }

        /// <summary>Todos os candidatos, do melhor placar para o pior. Alimenta o combo da revisão.</summary>
        public List<CandidatoExecutavel> Candidatos { get; }

        public CandidatoExecutavel Escolhido => Candidatos[0];

        /// <summary>Linha amarela na janela de revisão: o scanner não está seguro da escolha.</summary>
        public bool BaixaConfianca => Escolhido.Placar < Pontuador.PlacarDeConfianca;
    }

    /// <summary>Pasta que o scanner olhou e resolveu não propor como jogo.</summary>
    public sealed class PastaDescartada
    {
        public PastaDescartada(string pasta, string motivo)
        {
            Pasta = pasta;
            Motivo = motivo;
        }

        public string Pasta { get; }

        public string Motivo { get; }
    }

    /// <summary>Andamento do scan, para a barra de progresso da fase 3.</summary>
    public sealed class ProgressoScan
    {
        public ProgressoScan(string pastaAtual, int pastasVisitadas, int jogosEncontrados)
        {
            PastaAtual = pastaAtual;
            PastasVisitadas = pastasVisitadas;
            JogosEncontrados = jogosEncontrados;
        }

        public string PastaAtual { get; }
        public int PastasVisitadas { get; }
        public int JogosEncontrados { get; }
    }

    /// <summary>
    /// Varre as pastas raiz e identifica um jogo por pasta de jogo.
    ///
    /// Não toca na biblioteca e não grava nada: devolve o que achou para a janela de
    /// revisão decidir. O scan também não é recursivo sem fim — respeita limite de
    /// profundidade e para nas pastas de lixo (redist, docs, uninstall...).
    /// </summary>
    public sealed class ScannerDeJogos
    {
        /// <summary>Níveis de subpasta que o scanner desce procurando .exe dentro de um jogo.</summary>
        public const int ProfundidadeMaximaDeBusca = 4;

        /// <summary>
        /// Níveis de pasta de categoria aceitos antes do jogo em si
        /// (@"Jogos\Antigos\Carros\Need For Speed..." usa 2).
        /// </summary>
        public const int ProfundidadeMaximaDeCategoria = 3;

        private static readonly string[] ExtensoesDeUltimoRecurso = { ".bat", ".cmd", ".lnk" };

        private readonly ISistemaDeArquivos _sistemaDeArquivos;
        private readonly FiltroDeExclusao _filtro;
        private readonly List<PastaDescartada> _descartes = new List<PastaDescartada>();

        // Contadores só do scan em andamento. Um scan por vez, sempre — a fase 3 roda
        // isto numa thread só, com botão cancelar.
        private int _pastasVisitadas;
        private int _jogosEncontrados;

        public ScannerDeJogos(ISistemaDeArquivos sistemaDeArquivos, FiltroDeExclusao? filtro = null)
        {
            _sistemaDeArquivos = sistemaDeArquivos ?? throw new ArgumentNullException(nameof(sistemaDeArquivos));
            _filtro = filtro ?? FiltroDeExclusao.Padrao;
        }

        /// <summary>Pastas visitadas no último scan.</summary>
        public int PastasVisitadas => _pastasVisitadas;

        /// <summary>
        /// Pastas olhadas e descartadas no último scan, com o motivo. É como eu confiro
        /// se algum indie ficou de fora sem eu perceber.
        /// </summary>
        public IReadOnlyList<PastaDescartada> Descartes => _descartes;

        /// <summary>
        /// Escaneia as pastas raiz (caminhos completos) e devolve os jogos encontrados,
        /// ordenados por título.
        /// </summary>
        public List<JogoDetectado> Escanear(IEnumerable<string> pastasRaiz,
                                            IProgress<ProgressoScan>? progresso = null,
                                            CancellationToken cancelamento = default)
        {
            var jogos = new List<JogoDetectado>();
            var pastasJaVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _pastasVisitadas = 0;
            _jogosEncontrados = 0;
            _descartes.Clear();

            foreach (var raiz in pastasRaiz)
            {
                cancelamento.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(raiz) || !_sistemaDeArquivos.PastaExiste(raiz)) continue;

                // A raiz em si não é jogo: cada subpasta imediata é que é candidata.
                foreach (var subpasta in _sistemaDeArquivos.ListarSubpastas(raiz))
                {
                    cancelamento.ThrowIfCancellationRequested();

                    var pastasDeJogo = new List<string>();
                    ResolverPastasDeJogo(subpasta, 0, pastasDeJogo, progresso, cancelamento);

                    foreach (var pastaDeJogo in pastasDeJogo)
                    {
                        cancelamento.ThrowIfCancellationRequested();

                        // A mesma pasta pode ser alcançada por duas raízes ("Jogos" e "Jogos\Antigos").
                        if (!pastasJaVistas.Add(Normalizar(pastaDeJogo))) continue;

                        var jogo = AnalisarPastaDeJogo(pastaDeJogo, cancelamento);
                        if (jogo is null)
                        {
                            _descartes.Add(new PastaDescartada(pastaDeJogo,
                                "nenhum candidato aproveitável (só instalador/redistribuível ou nada)"));
                            continue;
                        }

                        jogos.Add(jogo);
                        _jogosEncontrados = jogos.Count;
                        progresso?.Report(new ProgressoScan(pastaDeJogo, _pastasVisitadas, _jogosEncontrados));
                    }
                }
            }

            jogos.Sort((a, b) => string.Compare(a.TituloProposto, b.TituloProposto, StringComparison.CurrentCultureIgnoreCase));
            return jogos;
        }

        // ---- Descoberta: o que é jogo e o que é pasta de categoria ---------------------------

        /// <summary>
        /// Decide se <paramref name="pasta"/> é um jogo ou apenas uma prateleira com jogos
        /// dentro (@"Jogos\Antigos\Carros\"), e vai empilhando as pastas de jogo em
        /// <paramref name="destino"/>.
        /// </summary>
        private void ResolverPastasDeJogo(string pasta, int nivel, List<string> destino,
                                          IProgress<ProgressoScan>? progresso, CancellationToken cancelamento)
        {
            cancelamento.ThrowIfCancellationRequested();

            var nome = Path.GetFileName(pasta.TrimEnd('\\', '/'));
            if (Pontuador.SegmentoEhIrrelevante(nome)) return;
            if (string.Equals(nome, Dados.Caminhos.NomePastaEstado, StringComparison.OrdinalIgnoreCase)) return;

            if (_filtro.PastaIgnorada(pasta))
            {
                _descartes.Add(new PastaDescartada(pasta, "na lista de pastas ignoradas do config.json"));
                return;
            }

            _pastasVisitadas++;
            progresso?.Report(new ProgressoScan(pasta, _pastasVisitadas, _jogosEncontrados));

            // Sinal forte: tem executável na própria raiz ou numa pasta de binários conhecida.
            if (TemExecutavelProprio(pasta))
            {
                destino.Add(pasta);
                return;
            }

            var subpastas = _sistemaDeArquivos.ListarSubpastas(pasta);

            if (nivel >= ProfundidadeMaximaDeCategoria || subpastas.Count == 0)
            {
                // Fundo do poço: entra se houver algum ponto de entrada em algum lugar dentro
                // dela. Aqui o .bat/.cmd/.lnk vale, porque é o caso do jogo portátil ou
                // emulado que não tem .exe nenhum.
                if (TemAlgumPontoDeEntrada(pasta, 0)) destino.Add(pasta);
                else _descartes.Add(new PastaDescartada(pasta, "nenhum executável encontrado"));
                return;
            }

            var achadosAbaixo = new List<string>();
            foreach (var subpasta in subpastas)
                ResolverPastasDeJogo(subpasta, nivel + 1, achadosAbaixo, progresso, cancelamento);

            if (achadosAbaixo.Count >= 2)
            {
                // Duas ou mais coisas com executável dentro: é prateleira, não jogo.
                destino.AddRange(achadosAbaixo);
                return;
            }

            if (achadosAbaixo.Count == 1)
            {
                // Um só. Se a pasta de cima tem arquivos soltos, ela é o jogo e o executável
                // mora numa subpasta (@"MeuJogo\System\jogo.exe"). Se não tem nada solto,
                // ela é só um invólucro (@"Carros\" com um jogo dentro) e vale o de baixo.
                destino.Add(TemArquivoQueImporta(pasta) ? pasta : achadosAbaixo[0]);
            }
        }

        /// <summary>
        /// Executável na raiz da pasta ou em Bin\, Binaries\Win64\, game\, x64\...
        /// Só conta .exe de propósito: um .bat solto numa pasta de categoria
        /// (@"Carros\organizar.bat") não pode fazer a prateleira inteira virar "um jogo".
        /// </summary>
        private bool TemExecutavelProprio(string pasta)
        {
            if (TemExecutavelDireto(pasta)) return true;

            foreach (var subpasta in _sistemaDeArquivos.ListarSubpastas(pasta))
            {
                var nome = Path.GetFileName(subpasta.TrimEnd('\\', '/'));
                if (!EhPastaDeBinarios(nome) || _filtro.PastaIgnorada(subpasta)) continue;

                if (TemExecutavelDireto(subpasta)) return true;

                // "Binaries\Win64\": o exe está um nível abaixo do nome conhecido.
                foreach (var neta in _sistemaDeArquivos.ListarSubpastas(subpasta))
                {
                    if (EhPastaDeBinarios(Path.GetFileName(neta.TrimEnd('\\', '/'))) && TemExecutavelDireto(neta))
                        return true;
                }
            }
            return false;
        }

        private static bool EhPastaDeBinarios(string nome)
            => string.Equals(nome, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nome, "binaries", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nome, "win64", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nome, "win32", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nome, "game", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nome, "x64", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// .exe direto na pasta, ignorando os descartáveis. Um dxwebsetup.exe solto não
        /// prova que a pasta é um jogo — e essa pasta pode ser justamente a prateleira
        /// que precisa continuar sendo percorrida.
        /// </summary>
        private bool TemExecutavelDireto(string pasta)
        {
            foreach (var arquivo in _sistemaDeArquivos.ListarArquivos(pasta))
            {
                if (!EhExe(arquivo.Caminho)) continue;
                if (Pontuador.NomeEhDescartavel(arquivo.Caminho)) continue;
                if (_filtro.ExecutavelIgnorado(arquivo.Caminho)) continue;

                return true;
            }
            return false;
        }

        /// <summary>
        /// Arquivo solto que sugere "aqui mora um jogo instalado" (leiame, .pak, .ini...).
        /// Instalador avulso não conta: prateleira com um dxwebsetup.exe dentro continua
        /// sendo prateleira.
        /// </summary>
        private bool TemArquivoQueImporta(string pasta)
        {
            foreach (var arquivo in _sistemaDeArquivos.ListarArquivos(pasta))
            {
                if (Pontuador.NomeEhDescartavel(arquivo.Caminho)) continue;
                if (_filtro.ExecutavelIgnorado(arquivo.Caminho)) continue;

                return true;
            }
            return false;
        }

        private bool TemAlgumPontoDeEntrada(string pasta, int profundidade)
        {
            foreach (var arquivo in _sistemaDeArquivos.ListarArquivos(pasta))
            {
                if (!EhExe(arquivo.Caminho) && !EhUltimoRecurso(arquivo.Caminho)) continue;
                if (_filtro.ExecutavelIgnorado(arquivo.Caminho)) continue;

                return true;
            }

            if (profundidade >= ProfundidadeMaximaDeBusca) return false;

            foreach (var subpasta in _sistemaDeArquivos.ListarSubpastas(pasta))
            {
                var nome = Path.GetFileName(subpasta.TrimEnd('\\', '/'));
                if (Pontuador.SegmentoEhIrrelevante(nome) || _filtro.PastaIgnorada(subpasta)) continue;

                if (TemAlgumPontoDeEntrada(subpasta, profundidade + 1)) return true;
            }
            return false;
        }

        // ---- Análise de uma pasta de jogo ------------------------------------------------------

        /// <summary>
        /// Junta os candidatos da pasta, pontua e devolve o jogo proposto.
        /// null quando não há candidato nenhum.
        /// </summary>
        public JogoDetectado? AnalisarPastaDeJogo(string pastaDoJogo, CancellationToken cancelamento = default)
        {
            var executaveis = new List<CandidatoExecutavel>();
            var ultimoRecurso = new List<CandidatoExecutavel>();

            ColetarCandidatos(pastaDoJogo, pastaDoJogo, 0, executaveis, ultimoRecurso, cancelamento);

            if (executaveis.Count == 0 && ultimoRecurso.Count == 0) return null;

            var todos = new List<CandidatoExecutavel>(executaveis.Count + ultimoRecurso.Count);
            todos.AddRange(executaveis);
            todos.AddRange(ultimoRecurso);

            var nomeDaPasta = Path.GetFileName(pastaDoJogo.TrimEnd('\\', '/'));
            var ordenados = Pontuador.Avaliar(todos, nomeDaPasta, _sistemaDeArquivos);

            // .bat/.cmd/.lnk só entram na lista quando nenhum .exe conseguiu placar positivo.
            if (TemExeComPlacarPositivo(executaveis))
                ordenados.RemoveAll(candidato => !candidato.EhExe);

            // Pasta só de instalador não é jogo. Ela vira descarte, não uma linha na revisão.
            if (ordenados.Count == 0 || ordenados.TrueForAll(candidato => candidato.Excluido)) return null;

            return new JogoDetectado(pastaDoJogo, TituloDePasta.Limpar(nomeDaPasta), ordenados);
        }

        private static bool TemExeComPlacarPositivo(List<CandidatoExecutavel> executaveis)
        {
            foreach (var executavel in executaveis)
            {
                if (!executavel.Excluido && executavel.Placar > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Percorre a pasta do jogo recolhendo .exe (e .bat/.cmd/.lnk à parte).
        /// Desce no máximo <see cref="ProfundidadeMaximaDeBusca"/> níveis e não entra nas
        /// subpastas de uma pasta de lixo — mas ainda olha os arquivos da própria pasta de
        /// lixo, porque o placar sabe puni-los (-80) e é bom que eles apareçam na lista de
        /// candidatos da revisão em vez de sumirem.
        /// </summary>
        private void ColetarCandidatos(string pastaDoJogo, string pastaAtual, int profundidade,
                                       List<CandidatoExecutavel> executaveis,
                                       List<CandidatoExecutavel> ultimoRecurso,
                                       CancellationToken cancelamento)
        {
            cancelamento.ThrowIfCancellationRequested();

            foreach (var arquivo in _sistemaDeArquivos.ListarArquivos(pastaAtual))
            {
                // Executável da lista de ignorados não vira candidato nem para a revisão manual.
                if (_filtro.ExecutavelIgnorado(arquivo.Caminho)) continue;

                var relativo = RelativoAoJogo(pastaDoJogo, arquivo.Caminho);

                if (EhExe(arquivo.Caminho))
                {
                    executaveis.Add(new CandidatoExecutavel(arquivo.Caminho, relativo, arquivo.Tamanho));
                }
                else if (EhUltimoRecurso(arquivo.Caminho))
                {
                    ultimoRecurso.Add(new CandidatoExecutavel(arquivo.Caminho, relativo, arquivo.Tamanho)
                    {
                        EhExe = false
                    });
                }
            }

            if (profundidade >= ProfundidadeMaximaDeBusca) return;

            var nomeAtual = Path.GetFileName(pastaAtual.TrimEnd('\\', '/'));
            if (profundidade > 0 && Pontuador.SegmentoEhIrrelevante(nomeAtual)) return;

            foreach (var subpasta in _sistemaDeArquivos.ListarSubpastas(pastaAtual))
            {
                if (_filtro.PastaIgnorada(subpasta)) continue;

                ColetarCandidatos(pastaDoJogo, subpasta, profundidade + 1, executaveis, ultimoRecurso, cancelamento);
            }
        }

        // ---- Apoio -------------------------------------------------------------------------------

        private static bool EhExe(string caminho)
            => string.Equals(Path.GetExtension(caminho), ".exe", StringComparison.OrdinalIgnoreCase);

        private static bool EhUltimoRecurso(string caminho)
        {
            var extensao = Path.GetExtension(caminho);
            foreach (var aceita in ExtensoesDeUltimoRecurso)
            {
                if (string.Equals(extensao, aceita, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string RelativoAoJogo(string pastaDoJogo, string caminhoDoArquivo)
        {
            var prefixo = pastaDoJogo.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

            return caminhoDoArquivo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
                ? caminhoDoArquivo.Substring(prefixo.Length)
                : Path.GetFileName(caminhoDoArquivo);
        }

        private static string Normalizar(string caminho) => caminho.TrimEnd('\\', '/');
    }
}
