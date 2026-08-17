using System.Collections.Generic;

namespace Mochila.Scanner
{
    /// <summary>Um arquivo visto pelo scanner.</summary>
    public sealed class ArquivoEncontrado
    {
        public ArquivoEncontrado(string caminho, long tamanho)
        {
            Caminho = caminho;
            Tamanho = tamanho;
        }

        /// <summary>Caminho completo (absoluto no disco real, ou virtual nos testes).</summary>
        public string Caminho { get; }

        public long Tamanho { get; }
    }

    /// <summary>Valor de IMAGE_OPTIONAL_HEADER.Subsystem que interessa ao placar.</summary>
    public enum SubsistemaPe
    {
        Desconhecido = 0,
        Gui = 2,
        Console = 3
    }

    /// <summary>
    /// O que dá para saber olhando dentro do .exe. Tudo é opcional: se a leitura do PE
    /// falhar (arquivo em uso, binário estranho, sistema de arquivos simulado), o scanner
    /// segue sem esses sinais em vez de derrubar o scan inteiro.
    /// </summary>
    public sealed class InfoExecutavel
    {
        public SubsistemaPe Subsistema { get; set; } = SubsistemaPe.Desconhecido;

        /// <summary>Manifesto embutido pedindo elevação — quase sempre é instalador/updater.</summary>
        public bool PedeAdministrador { get; set; }

        public string? FileDescription { get; set; }

        public string? ProductName { get; set; }
    }

    /// <summary>
    /// Tudo que o scanner precisa do disco. Existe como interface para os casos de teste
    /// obrigatórios rodarem contra listagens simuladas, sem depender do HD estar plugado.
    /// </summary>
    public interface ISistemaDeArquivos
    {
        bool PastaExiste(string caminho);

        /// <summary>Subpastas imediatas. Devolve vazio (não lança) se a pasta sumir no meio do scan.</summary>
        IReadOnlyList<string> ListarSubpastas(string caminho);

        /// <summary>Arquivos imediatos da pasta, com tamanho.</summary>
        IReadOnlyList<ArquivoEncontrado> ListarArquivos(string caminho);

        /// <summary>Lê subsistema, manifesto e metadados do executável. null quando não dá para ler.</summary>
        InfoExecutavel? LerInfoExecutavel(string caminho);
    }
}
