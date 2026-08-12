using System;
using System.IO;
using System.Reflection;

namespace Launcher.Dados
{
    /// <summary>
    /// Ponto único de verdade sobre onde ficam as coisas no disco.
    ///
    /// Regra inegociável de portabilidade: a raiz é a pasta onde está o Launcher.exe.
    /// Nada de registro, AppData, Documents ou caminho absoluto do sistema. Todo caminho
    /// de jogo gravado em disco é RELATIVO a essa raiz, porque a letra do drive muda de
    /// PC para PC (o HD pode ser E: aqui e F: ali).
    /// </summary>
    public static class Caminhos
    {
        /// <summary>Nome da pasta que guarda todo o estado do launcher.</summary>
        public const string NomePastaEstado = "_launcher";

        private static readonly char[] SeparadoresDeCaminho = { '\\', '/' };
        private static readonly string SeparadorTexto = Path.DirectorySeparatorChar.ToString();

        private static string _pastaBase = DetectarPastaBase();

        /// <summary>Pasta onde vive o Launcher.exe. Raiz de todos os caminhos relativos.</summary>
        public static string PastaBase => _pastaBase;

        /// <summary>
        /// Aponta a raiz portátil para outra pasta. Existe para os testes automatizados
        /// (e para simular "o HD virou outra letra"); a aplicação normal não chama isso.
        /// </summary>
        public static void DefinirPastaBase(string caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) throw new ArgumentNullException(nameof(caminho));
            _pastaBase = Path.GetFullPath(caminho).TrimEnd(Path.DirectorySeparatorChar);
        }

        /// <summary>Volta a raiz para a pasta real do executável.</summary>
        public static void RestaurarPastaBase() => _pastaBase = DetectarPastaBase();

        private static string DetectarPastaBase()
        {
            var local = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(local))
                return Path.GetDirectoryName(Path.GetFullPath(local!))!;

            // Fallback (ex.: hospedado por outro processo)
            return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        }

        // ---- Pastas e arquivos de estado -------------------------------------------------

        public static string PastaEstado => Path.Combine(_pastaBase, NomePastaEstado);

        public static string ArquivoBiblioteca => Path.Combine(PastaEstado, "biblioteca.json");

        public static string ArquivoConfig => Path.Combine(PastaEstado, "config.json");

        /// <summary>Capas em tamanho cheio (600x900).</summary>
        public static string PastaCapas => Path.Combine(PastaEstado, "capas");

        /// <summary>Thumbnails já redimensionados, gerados sob demanda.</summary>
        public static string PastaCache => Path.Combine(PastaEstado, "cache");

        /// <summary>Cria _launcher\, capas\ e cache\ se ainda não existirem.</summary>
        public static void GarantirEstrutura()
        {
            Directory.CreateDirectory(PastaEstado);
            Directory.CreateDirectory(PastaCapas);
            Directory.CreateDirectory(PastaCache);
        }

        // ---- Conversão absoluto <-> relativo ---------------------------------------------

        /// <summary>
        /// Converte um caminho absoluto em relativo à pasta do launcher.
        /// Lança se o caminho estiver fora da raiz portátil — gravar algo de fora
        /// significaria gravar uma letra de drive no JSON, que é exatamente o bug
        /// que quebra o launcher no próximo PC.
        /// </summary>
        public static string ParaRelativo(string caminhoAbsoluto)
        {
            if (!TentarParaRelativo(caminhoAbsoluto, out var relativo))
            {
                throw new CaminhoForaDaRaizException(
                    $"O caminho \"{caminhoAbsoluto}\" está em outro drive (o launcher está em \"{_pastaBase}\"). " +
                    "Só arquivos do mesmo HD podem ser salvos na biblioteca.");
            }
            return relativo;
        }

        /// <summary>
        /// Versão sem exceção. Devolve false quando o caminho está em outro drive,
        /// em outra máquina (UNC) ou acima da pasta do launcher.
        /// </summary>
        public static bool TentarParaRelativo(string? caminhoAbsoluto, out string relativo)
        {
            relativo = "";
            if (string.IsNullOrWhiteSpace(caminhoAbsoluto)) return false;

            string alvo;
            try
            {
                alvo = Path.GetFullPath(caminhoAbsoluto);
            }
            catch (Exception)
            {
                return false;
            }

            // A Uri base precisa terminar em separador para ser tratada como pasta.
            var baseComBarra = _pastaBase.EndsWith(SeparadorTexto, StringComparison.Ordinal)
                ? _pastaBase
                : _pastaBase + Path.DirectorySeparatorChar;

            if (!Uri.TryCreate(baseComBarra, UriKind.Absolute, out var uriBase)) return false;
            if (!Uri.TryCreate(alvo, UriKind.Absolute, out var uriAlvo)) return false;

            // Drives (ou hosts UNC) diferentes: MakeRelativeUri devolveria um caminho absoluto.
            if (!string.Equals(uriBase.Scheme, uriAlvo.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(uriBase.Host, uriAlvo.Host, StringComparison.OrdinalIgnoreCase)) return false;

            var uriRelativa = uriBase.MakeRelativeUri(uriAlvo);
            if (uriRelativa.IsAbsoluteUri) return false;

            // Uri usa "/" e escapa espaços como %20; voltamos para o formato Windows.
            var texto = Uri.UnescapeDataString(uriRelativa.OriginalString)
                           .Replace('/', Path.DirectorySeparatorChar);

            if (!EhRelativoValido(texto)) return false;

            relativo = texto;
            return true;
        }

        /// <summary>
        /// Resolve um caminho relativo salvo no JSON contra a pasta atual do launcher.
        /// É aqui que a troca de letra de drive fica transparente.
        /// </summary>
        public static string ParaAbsoluto(string relativo)
        {
            if (!EhRelativoValido(relativo))
            {
                throw new CaminhoForaDaRaizException(
                    $"O caminho \"{relativo}\" não é relativo válido. A biblioteca só aceita " +
                    "caminhos do mesmo HD do launcher.");
            }
            return Path.GetFullPath(Path.Combine(_pastaBase, relativo));
        }

        /// <summary>Como <see cref="ParaAbsoluto"/>, mas devolve null em vez de lançar.</summary>
        public static string? ParaAbsolutoOuNulo(string? relativo)
        {
            if (!EhRelativoValido(relativo)) return null;
            try
            {
                return Path.GetFullPath(Path.Combine(_pastaBase, relativo!));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Um caminho só pode ir para o JSON se for relativo de verdade: sem letra de
        /// drive, sem raiz e sem UNC.
        ///
        /// ".." é PERMITIDO, desde que o resultado continue dentro do mesmo drive. O
        /// launcher mora numa subpasta (D:\Launcher\Launcher.exe) e os jogos são irmãos
        /// dela (D:\Jogos\), então "..\Jogos\..." é o caminho normal do acervo — proibir
        /// ".." obrigaria a jogar o exe na raiz do HD. O que não pode é sair do drive:
        /// "..\..\..\Windows" viraria caminho de máquina, não de HD portátil.
        /// </summary>
        public static bool EhRelativoValido(string? relativo)
        {
            if (relativo is null || relativo.Trim().Length == 0) return false;
            if (relativo.IndexOf(':') >= 0) return false;            // "D:\jogos" ou "D:jogos"
            if (Path.IsPathRooted(relativo)) return false;           // "\jogos", "\\servidor\..."
            if (relativo.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

            var segmentos = relativo.Split(SeparadoresDeCaminho, StringSplitOptions.RemoveEmptyEntries);
            if (segmentos.Length == 0) return false;

            return ContinuaNoDrive(relativo);
        }

        /// <summary>
        /// Confere que o relativo não tenta subir além da raiz do drive, CONTANDO os
        /// níveis em vez de resolver o caminho.
        ///
        /// Resolver não serve aqui: Path.GetFullPath grampeia o excesso na raiz, então
        /// "..\..\..\..\Windows\notepad.exe" viraria "D:\Windows\notepad.exe" e passaria
        /// por caminho legítimo do HD. Só que, plugado o HD como C:, esse mesmo relativo
        /// apontaria para o Windows da máquina. Um caminho que tenta escapar da raiz está
        /// errado mesmo quando o sistema operacional o conserta em silêncio.
        /// </summary>
        private static bool ContinuaNoDrive(string relativo)
        {
            var profundidade = ProfundidadeDaPastaBase();

            foreach (var segmento in relativo.Split(SeparadoresDeCaminho, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segmento == ".") continue;

                if (segmento == "..")
                {
                    profundidade--;
                    if (profundidade < 0) return false;    // passou da raiz do drive
                }
                else
                {
                    profundidade++;
                }
            }

            // Zero significa a própria raiz do drive: não é alvo válido para um jogo.
            return profundidade > 0;
        }

        /// <summary>Quantas pastas separam a pasta base da raiz do drive.</summary>
        private static int ProfundidadeDaPastaBase()
        {
            try
            {
                var raiz = Path.GetPathRoot(_pastaBase);
                if (string.IsNullOrEmpty(raiz)) return 0;

                var resto = _pastaBase.Substring(raiz!.Length);
                return resto.Split(SeparadoresDeCaminho, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Lê um caminho gravado e devolve SEMPRE um relativo.
        ///
        /// Migração automática: arquivo de uma versão antiga (ou editado na mão) pode
        /// trazer "D:\Jogos\...". Convertido aqui na leitura, ele volta ao disco relativo
        /// na próxima gravação, sem passo manual nenhum. O que não dá para converter — um
        /// caminho de OUTRO drive — é devolvido como está, e a validação recusa depois:
        /// melhor uma mensagem clara do que apagar em silêncio o que eu tinha catalogado.
        /// </summary>
        public static string ParaRelativoMigrando(string? gravado)
        {
            var texto = NormalizarRelativo(gravado);
            if (texto.Length == 0) return "";

            if (EhRelativoValido(texto)) return texto;

            return TentarParaRelativo(texto, out var convertido) ? convertido : texto;
        }

        /// <summary>
        /// Padroniza um relativo vindo do JSON: separador do Windows, sem "./" na frente
        /// e sem barra sobrando no fim. Não valida — use <see cref="EhRelativoValido"/> para isso.
        /// </summary>
        public static string NormalizarRelativo(string? relativo)
        {
            if (relativo is null || relativo.Trim().Length == 0) return "";

            var texto = relativo.Replace('/', Path.DirectorySeparatorChar).Trim();
            while (texto.StartsWith(".\\", StringComparison.Ordinal)) texto = texto.Substring(2);
            return texto.TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    /// <summary>Caminho que não pode ser representado dentro da pasta portátil do launcher.</summary>
    public sealed class CaminhoForaDaRaizException : Exception
    {
        public CaminhoForaDaRaizException(string mensagem) : base(mensagem) { }
    }
}
