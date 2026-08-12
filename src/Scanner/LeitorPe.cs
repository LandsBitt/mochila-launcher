using System;
using System.IO;
using System.Text;

namespace Launcher.Scanner
{
    /// <summary>
    /// Leitura direta do cabeçalho PE de um .exe, sem carregar o binário no processo
    /// (nada de LoadLibrary: o launcher não vai mapear executável de origem desconhecida
    /// só para descobrir se é console ou não).
    ///
    /// Extrai dois sinais do placar:
    ///  - Subsystem (2 = GUI, 3 = console);
    ///  - manifesto embutido (RT_MANIFEST) pedindo requireAdministrator.
    ///
    /// Qualquer erro devolve o que já deu para descobrir, ou null. O scanner precisa
    /// funcionar mesmo diante de arquivo truncado, arquivo em uso ou .exe de 16 bits.
    /// </summary>
    public static class LeitorPe
    {
        private const ushort AssinaturaMz = 0x5A4D;          // "MZ"
        private const uint AssinaturaPe = 0x00004550;        // "PE\0\0"
        private const ushort MagicPe32Plus = 0x20B;
        private const int OffsetELfanew = 0x3C;

        /// <summary>Subsystem fica no offset 68 do optional header — igual em PE32 e PE32+.</summary>
        private const int OffsetSubsystem = 68;

        private const int TipoRecursoManifesto = 24;         // RT_MANIFEST
        private const uint BitDeSubdiretorio = 0x8000_0000;

        /// <summary>Manifesto legítimo não passa disso; corta binário corrompido apontando lixo.</summary>
        private const int TamanhoMaximoDeManifesto = 512 * 1024;

        public static InfoExecutavel? Ler(string caminho)
        {
            try
            {
                // FileShare.ReadWrite: o jogo pode estar aberto enquanto eu escaneio.
                using (var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete, 4096))
                using (var leitor = new BinaryReader(fluxo))
                {
                    return LerCabecalho(fluxo, leitor);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static InfoExecutavel? LerCabecalho(FileStream fluxo, BinaryReader leitor)
        {
            if (fluxo.Length < OffsetELfanew + 4) return null;

            if (leitor.ReadUInt16() != AssinaturaMz) return null;

            fluxo.Position = OffsetELfanew;
            long inicioPe = leitor.ReadInt32();
            if (inicioPe <= 0 || inicioPe + 24 > fluxo.Length) return null;

            fluxo.Position = inicioPe;
            if (leitor.ReadUInt32() != AssinaturaPe) return null;

            // ---- COFF header ----
            leitor.ReadUInt16();                              // Machine
            int quantidadeDeSecoes = leitor.ReadUInt16();
            leitor.ReadUInt32();                              // TimeDateStamp
            leitor.ReadUInt32();                              // PointerToSymbolTable
            leitor.ReadUInt32();                              // NumberOfSymbols
            int tamanhoDoOpcional = leitor.ReadUInt16();
            leitor.ReadUInt16();                              // Characteristics

            var info = new InfoExecutavel();

            long inicioOpcional = fluxo.Position;
            if (tamanhoDoOpcional < OffsetSubsystem + 2 || inicioOpcional + tamanhoDoOpcional > fluxo.Length)
                return info;

            var magic = leitor.ReadUInt16();
            var ehPe32Plus = magic == MagicPe32Plus;

            fluxo.Position = inicioOpcional + OffsetSubsystem;
            var subsistema = leitor.ReadUInt16();
            info.Subsistema = subsistema == (int)SubsistemaPe.Gui ? SubsistemaPe.Gui
                            : subsistema == (int)SubsistemaPe.Console ? SubsistemaPe.Console
                            : SubsistemaPe.Desconhecido;

            // ---- Data directories: NumberOfRvaAndSizes vem depois dos campos que mudam de largura ----
            var offsetNumeroDeDiretorios = ehPe32Plus ? 108 : 92;
            if (tamanhoDoOpcional < offsetNumeroDeDiretorios + 4 + (3 * 8)) return info;

            fluxo.Position = inicioOpcional + offsetNumeroDeDiretorios;
            var quantidadeDeDiretorios = leitor.ReadUInt32();
            if (quantidadeDeDiretorios <= 2) return info;      // índice 2 = recursos

            fluxo.Position = inicioOpcional + offsetNumeroDeDiretorios + 4 + (2 * 8);
            var rvaDosRecursos = leitor.ReadUInt32();
            var tamanhoDosRecursos = leitor.ReadUInt32();
            if (rvaDosRecursos == 0 || tamanhoDosRecursos == 0) return info;

            var secoes = LerSecoes(fluxo, leitor, inicioOpcional + tamanhoDoOpcional, quantidadeDeSecoes);
            var baseDosRecursos = RvaParaOffset(secoes, rvaDosRecursos);
            if (baseDosRecursos < 0) return info;

            var manifesto = LerManifesto(fluxo, leitor, secoes, baseDosRecursos);
            if (manifesto != null)
            {
                info.PedeAdministrador =
                    manifesto.IndexOf("requireAdministrator", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return info;
        }

        // ---- Seções e conversão RVA -> offset no arquivo -------------------------------------

        private sealed class Secao
        {
            public uint EnderecoVirtual;
            public uint TamanhoVirtual;
            public uint TamanhoBruto;
            public uint OffsetBruto;
        }

        private static Secao[] LerSecoes(FileStream fluxo, BinaryReader leitor, long inicio, int quantidade)
        {
            if (quantidade <= 0 || quantidade > 96) return Array.Empty<Secao>();
            if (inicio + (quantidade * 40) > fluxo.Length) return Array.Empty<Secao>();

            var secoes = new Secao[quantidade];
            for (var i = 0; i < quantidade; i++)
            {
                fluxo.Position = inicio + (i * 40);
                fluxo.Position += 8;                                   // Name[8]
                var tamanhoVirtual = leitor.ReadUInt32();
                var enderecoVirtual = leitor.ReadUInt32();
                var tamanhoBruto = leitor.ReadUInt32();
                var offsetBruto = leitor.ReadUInt32();

                secoes[i] = new Secao
                {
                    TamanhoVirtual = tamanhoVirtual,
                    EnderecoVirtual = enderecoVirtual,
                    TamanhoBruto = tamanhoBruto,
                    OffsetBruto = offsetBruto
                };
            }
            return secoes;
        }

        private static long RvaParaOffset(Secao[] secoes, uint rva)
        {
            foreach (var secao in secoes)
            {
                // TamanhoVirtual pode vir 0 em binários antigos; nesse caso vale o bruto.
                var tamanho = Math.Max(secao.TamanhoVirtual, secao.TamanhoBruto);
                if (rva >= secao.EnderecoVirtual && rva < secao.EnderecoVirtual + tamanho)
                    return secao.OffsetBruto + (rva - secao.EnderecoVirtual);
            }
            return -1;
        }

        // ---- Árvore de recursos: tipo -> nome -> idioma -> dados -----------------------------

        private static string? LerManifesto(FileStream fluxo, BinaryReader leitor, Secao[] secoes, long baseDosRecursos)
        {
            // Nível 1: tipo do recurso. Procuramos o id 24 (RT_MANIFEST).
            var offsetPorTipo = ProcurarEntrada(fluxo, leitor, baseDosRecursos, baseDosRecursos, TipoRecursoManifesto);
            if (offsetPorTipo < 0) return null;

            // Níveis 2 e 3 (nome e idioma): qualquer um serve, pegamos o primeiro.
            var offsetPorNome = ProcurarEntrada(fluxo, leitor, baseDosRecursos, offsetPorTipo, null);
            if (offsetPorNome < 0) return null;

            var offsetDosDados = ProcurarEntrada(fluxo, leitor, baseDosRecursos, offsetPorNome, null);
            if (offsetDosDados < 0) return null;

            // IMAGE_RESOURCE_DATA_ENTRY
            if (offsetDosDados + 8 > fluxo.Length) return null;
            fluxo.Position = offsetDosDados;
            var rvaDoConteudo = leitor.ReadUInt32();
            var tamanho = leitor.ReadUInt32();

            if (tamanho == 0 || tamanho > TamanhoMaximoDeManifesto) return null;

            var offsetDoConteudo = RvaParaOffset(secoes, rvaDoConteudo);
            if (offsetDoConteudo < 0 || offsetDoConteudo + tamanho > fluxo.Length) return null;

            fluxo.Position = offsetDoConteudo;
            var bytes = leitor.ReadBytes((int)tamanho);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Percorre um IMAGE_RESOURCE_DIRECTORY e devolve o offset apontado pela entrada
        /// de id <paramref name="idProcurado"/> (ou pela primeira entrada, se for null).
        /// Devolve -1 quando não acha.
        /// </summary>
        private static long ProcurarEntrada(FileStream fluxo, BinaryReader leitor,
                                            long baseDosRecursos, long offsetDoDiretorio, int? idProcurado)
        {
            if (offsetDoDiretorio + 16 > fluxo.Length) return -1;

            fluxo.Position = offsetDoDiretorio + 12;                  // pula flags/timestamp/versões
            int entradasPorNome = leitor.ReadUInt16();
            int entradasPorId = leitor.ReadUInt16();

            var total = entradasPorNome + entradasPorId;
            if (total <= 0 || total > 4096) return -1;

            var inicioDasEntradas = offsetDoDiretorio + 16;
            if (inicioDasEntradas + (total * 8) > fluxo.Length) return -1;

            for (var i = 0; i < total; i++)
            {
                fluxo.Position = inicioDasEntradas + (i * 8);
                var nomeOuId = leitor.ReadUInt32();
                var ponteiro = leitor.ReadUInt32();

                // Entradas por nome vêm primeiro e têm o bit alto ligado no campo de nome.
                var ehPorNome = (nomeOuId & BitDeSubdiretorio) != 0;

                if (idProcurado.HasValue)
                {
                    if (ehPorNome || nomeOuId != (uint)idProcurado.Value) continue;
                }

                return baseDosRecursos + (ponteiro & ~BitDeSubdiretorio);
            }

            return -1;
        }
    }
}
