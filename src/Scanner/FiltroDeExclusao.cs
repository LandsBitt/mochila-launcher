using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Launcher.Modelo;

namespace Launcher.Scanner
{
    /// <summary>
    /// Pastas e executáveis que o scanner ignora por completo — não viram jogo, não viram
    /// candidato e não aparecem nem na revisão manual.
    ///
    /// Diferente da penalidade de pasta irrelevante (redist, docs, tools), que só tira
    /// pontos: aqui é "nem olhe". Serve para coisas que não têm como funcionar num HD
    /// externo, como o ecossistema Riot, que depende de instalação, serviços e registro.
    ///
    /// A lista vem do config.json, então dá para acrescentar itens sem recompilar.
    /// </summary>
    public sealed class FiltroDeExclusao
    {
        private readonly List<Regex> _pastas;
        private readonly List<Regex> _executaveis;

        public FiltroDeExclusao(IEnumerable<string> pastasIgnoradas, IEnumerable<string> executaveisIgnorados)
        {
            _pastas = Compilar(pastasIgnoradas);
            _executaveis = Compilar(executaveisIgnorados);
        }

        /// <summary>Filtro montado com os padrões de fábrica (usado quando não há config).</summary>
        public static FiltroDeExclusao Padrao =>
            new FiltroDeExclusao(Config.PastasIgnoradasPadrao, Config.ExecutaveisIgnoradosPadrao);

        /// <summary>Filtro que não exclui nada. Útil em teste de regra isolada.</summary>
        public static FiltroDeExclusao Nenhum =>
            new FiltroDeExclusao(Array.Empty<string>(), Array.Empty<string>());

        public static FiltroDeExclusao De(Config config)
            => new FiltroDeExclusao(config.PastasIgnoradas, config.ExecutaveisIgnorados);

        /// <summary>Casa pelo nome da pasta, não pelo caminho inteiro.</summary>
        public bool PastaIgnorada(string? caminhoOuNome)
        {
            if (string.IsNullOrEmpty(caminhoOuNome)) return false;

            var nome = Path.GetFileName(caminhoOuNome!.TrimEnd('\\', '/'));
            return Casa(_pastas, nome);
        }

        /// <summary>Casa com o nome do arquivo, com ou sem extensão ("RiotClient" pega "RiotClient.exe").</summary>
        public bool ExecutavelIgnorado(string? caminhoOuNome)
        {
            if (string.IsNullOrEmpty(caminhoOuNome)) return false;

            var nome = Path.GetFileName(caminhoOuNome);
            var semExtensao = Path.GetFileNameWithoutExtension(caminhoOuNome);

            return Casa(_executaveis, nome) || Casa(_executaveis, semExtensao);
        }

        private static bool Casa(List<Regex> padroes, string? texto)
        {
            if (string.IsNullOrEmpty(texto)) return false;

            foreach (var padrao in padroes)
            {
                if (padrao.IsMatch(texto!)) return true;
            }
            return false;
        }

        /// <summary>
        /// Cada entrada é comparada inteira, ignorando maiúsculas. "*" e "?" funcionam como
        /// no Explorer, para eu poder escrever "Riot*" sem precisar listar tudo.
        /// </summary>
        private static List<Regex> Compilar(IEnumerable<string> entradas)
        {
            var compilados = new List<Regex>();
            if (entradas is null) return compilados;

            foreach (var entrada in entradas)
            {
                if (string.IsNullOrWhiteSpace(entrada)) continue;

                var sb = new StringBuilder("^");
                foreach (var c in entrada.Trim())
                {
                    if (c == '*') sb.Append(".*");
                    else if (c == '?') sb.Append('.');
                    else sb.Append(Regex.Escape(c.ToString()));
                }
                sb.Append('$');

                try
                {
                    compilados.Add(new Regex(sb.ToString(),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
                }
                catch (ArgumentException)
                {
                    // Entrada estranha no config.json não pode derrubar o scan.
                }
            }
            return compilados;
        }
    }
}
