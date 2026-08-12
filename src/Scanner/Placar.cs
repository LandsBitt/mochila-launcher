using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Launcher.Scanner
{
    /// <summary>
    /// Cada regra do placar tem um id estável. Serve para os testes conferirem que a
    /// pontuação veio do sinal certo (o caso do Underground 2 exige que os pontos de
    /// similaridade tenham sido dados de verdade, não que o resultado tenha acertado
    /// por acaso) e para a janela de revisão explicar a escolha.
    /// </summary>
    public enum RegraPlacar
    {
        NomeDeInstalador,
        NomeDeRedistribuivel,
        NomeDeRelatorioDeErro,
        NomeDeFerramenta,
        NomeDeLauncherOuUpdater,
        PastaIrrelevante,
        MaiorExecutavel,
        TamanhoRazoavel,
        TamanhoMinusculo,
        NaRaizDaPasta,
        PastaDeBinarios,
        ProfundidadeExtra,
        TokensEmComum,
        NomeIgualAoDaPasta,
        SiglaDoNomeDaPasta,
        SubsistemaGui,
        SubsistemaConsole,
        PedeAdministrador,
        MetadadosParecidos,
        NaoEhExecutavel,
        PontoDeEntradaEmSubpasta
    }

    /// <summary>Uma linha do placar: quanto e por quê.</summary>
    public sealed class ParcelaPlacar
    {
        public ParcelaPlacar(RegraPlacar regra, int pontos, string explicacao)
        {
            Regra = regra;
            Pontos = pontos;
            Explicacao = explicacao;
        }

        public RegraPlacar Regra { get; }

        public int Pontos { get; }

        public string Explicacao { get; }

        public override string ToString()
            => $"{(Pontos >= 0 ? "+" : "")}{Pontos.ToString(CultureInfo.InvariantCulture)} {Explicacao}";
    }

    /// <summary>Um executável concorrendo a "o jogo é este".</summary>
    public sealed class CandidatoExecutavel
    {
        private readonly List<ParcelaPlacar> _parcelas = new List<ParcelaPlacar>();

        public CandidatoExecutavel(string caminho, string caminhoRelativoAoJogo, long tamanho)
        {
            Caminho = caminho;
            CaminhoRelativoAoJogo = caminhoRelativoAoJogo;
            Tamanho = tamanho;
        }

        /// <summary>Caminho completo do arquivo.</summary>
        public string Caminho { get; }

        /// <summary>Caminho a partir da pasta do jogo (ex.: @"Bin\game.exe"). É o que a UI mostra.</summary>
        public string CaminhoRelativoAoJogo { get; }

        public string NomeDoArquivo => Path.GetFileName(Caminho);

        public long Tamanho { get; }

        /// <summary>false para os candidatos de último recurso (.bat, .cmd, .lnk).</summary>
        public bool EhExe { get; set; } = true;

        public int Placar { get; private set; }

        public IReadOnlyList<ParcelaPlacar> Parcelas => _parcelas;

        /// <summary>
        /// Fora da disputa: é instalador, redistribuível ou relatório de erro. Nenhum bônus
        /// resgata um candidato excluído — num HD de repacks o instalador quase sempre tem
        /// o nome do jogo ("NeedForSpeed_Setup.exe") e ganharia por similaridade.
        /// Continua na lista para eu poder forçá-lo na revisão manual, se um dia precisar.
        /// </summary>
        public bool Excluido { get; private set; }

        public string? MotivoDaExclusao { get; private set; }

        /// <summary>
        /// Escolhido por regra, não por pontos: é o launcher/bootstrapper na raiz e o
        /// executável do jogo está numa subpasta.
        /// </summary>
        public bool Promovido { get; private set; }

        public string? MotivoDaPromocao { get; private set; }

        public void Excluir(string motivo)
        {
            Excluido = true;
            MotivoDaExclusao = motivo;
        }

        public void Promover(string motivo)
        {
            Promovido = true;
            MotivoDaPromocao = motivo;
        }

        public void Somar(RegraPlacar regra, int pontos, string explicacao)
        {
            if (pontos == 0) return;
            _parcelas.Add(new ParcelaPlacar(regra, pontos, explicacao));
            Placar += pontos;
        }

        /// <summary>Quantos pontos uma regra específica rendeu (0 se não se aplicou).</summary>
        public int PontosDe(RegraPlacar regra)
        {
            var total = 0;
            foreach (var parcela in _parcelas)
            {
                if (parcela.Regra == regra) total += parcela.Pontos;
            }
            return total;
        }

        public bool Aplicou(RegraPlacar regra) => PontosDe(regra) != 0;

        /// <summary>Placar detalhado em texto — vira tooltip na janela de revisão e saída do --escanear.</summary>
        public string Detalhar()
        {
            var sb = new StringBuilder();
            sb.Append(CaminhoRelativoAoJogo)
              .Append("  [placar ")
              .Append(Placar.ToString(CultureInfo.InvariantCulture))
              .Append(']');

            if (Excluido) sb.Append("  [EXCLUÍDO: ").Append(MotivoDaExclusao).Append(']');
            if (Promovido) sb.Append("  [").Append(MotivoDaPromocao).Append(']');

            foreach (var parcela in _parcelas)
                sb.Append(Environment.NewLine).Append("      ").Append(parcela);

            return sb.ToString();
        }
    }
}
