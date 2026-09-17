using System;
using System.Diagnostics;
using System.IO;
using Mochila.Dados;

namespace Mochila.Execucao
{
    /// <summary>O que aconteceu com um script de gancho. Nada aqui impede o jogo de abrir.</summary>
    public enum ResultadoDoScript
    {
        /// <summary>O jogo não tem esse script configurado. O caso de 99% do acervo.</summary>
        SemScript,

        /// <summary>Rodou até o fim (o "antes") ou foi disparado (o "depois").</summary>
        Ok,

        /// <summary>Está configurado, mas o .bat não está mais lá.</summary>
        NaoEncontrado,

        /// <summary>Passou dos 30 s do "antes" e continua rodando.</summary>
        TempoEsgotado,

        /// <summary>Nem começou — cmd.exe recusou, permissão negada, HD sumiu.</summary>
        NaoRodou
    }

    /// <summary>O resultado de um gancho e o recado que o rodapé mostra, se houver.</summary>
    public readonly struct RetornoDoScript
    {
        public RetornoDoScript(ResultadoDoScript resultado, string aviso = "")
        {
            Resultado = resultado;
            Aviso = aviso ?? "";
        }

        public ResultadoDoScript Resultado { get; }

        /// <summary>Vazio quando não há nada a dizer — que é o caminho normal.</summary>
        public string Aviso { get; }

        public bool TemAviso => Aviso.Length > 0;
    }

    /// <summary>
    /// Os scripts de antes e depois do jogo (fase 15).
    ///
    /// Para acervo antigo isto é o que resolve dgVoodoo, troca de resolução e x360ce sem o
    /// launcher precisar saber o que é cada coisa: eu escrevo o <c>.bat</c>, ele roda.
    ///
    /// <b>O launcher nunca gera nem edita esses arquivos</b> — é regra da spec, não
    /// cautela. Um launcher que escreve o .bat vira dono dele, e da próxima vez que eu
    /// editasse o meu à mão ele seria sobrescrito sem aviso.
    ///
    /// Duas regras mandam aqui:
    ///
    /// 1. <b>Nada disso pode impedir o jogo de abrir.</b> Script ausente, script que
    ///    estoura o tempo, cmd.exe que recusa: tudo vira recado no rodapé e o jogo sobe
    ///    igual. O motivo é simples — o script é conveniência, o jogo é o objetivo.
    ///
    /// 2. <b>O caminho é relativo e do mesmo HD</b>, como todo caminho gravado por este
    ///    launcher. Um <c>.bat</c> em <c>C:\Users\...</c> não existe no próximo PC, e a
    ///    biblioteca teria gravado uma letra de drive.
    /// </summary>
    public static class ScriptsDoJogo
    {
        /// <summary>As duas extensões que valem. Ver <see cref="TemExtensaoAceita"/>.</summary>
        public static readonly string[] ExtensoesAceitas = { ".bat", ".cmd" };

        /// <summary>
        /// Quanto o "antes" pode segurar o lançamento antes de eu desistir de esperar. Os
        /// 30 s são o número da spec.
        ///
        /// É propriedade, e não constante, por um motivo só: o auto-teste do tempo esgotado
        /// precisa provar o comportamento sem parar a suíte por meio minuto. Não há tela
        /// nem campo de config que mexa nisto.
        /// </summary>
        public static TimeSpan LimiteDoAntes { get; set; } = TimeSpan.FromSeconds(30);

        private static readonly RetornoDoScript Nada = new RetornoDoScript(ResultadoDoScript.SemScript);

        /// <summary>
        /// Só .bat e .cmd. O <c>.exe</c> fica de fora de propósito: para abrir outro
        /// executável existe o campo do executável, e um "script" que é exe transformaria
        /// este gancho num segundo lançador, com regra de espera e de processo-filho
        /// próprias.
        /// </summary>
        public static bool TemExtensaoAceita(string? caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return false;

            string extensao;
            try
            {
                extensao = Path.GetExtension(caminho);
            }
            catch (Exception)
            {
                return false;      // caminho com caractere inválido
            }

            foreach (var aceita in ExtensoesAceitas)
            {
                if (string.Equals(extensao, aceita, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Valida o arquivo escolhido no seletor e devolve o relativo que vai para o JSON.
        /// Devolve false com um motivo pronto para caixa de diálogo.
        /// </summary>
        public static bool TentarAceitar(string? caminhoAbsoluto, out string relativo, out string recusa)
        {
            relativo = "";
            recusa = "";

            if (string.IsNullOrWhiteSpace(caminhoAbsoluto))
            {
                recusa = "Nenhum arquivo escolhido.";
                return false;
            }

            if (!TemExtensaoAceita(caminhoAbsoluto))
            {
                recusa = $"\"{NomeDoArquivo(caminhoAbsoluto!)}\" não é um .bat nem um .cmd." +
                         Environment.NewLine +
                         "Só esses dois valem como script: para abrir outro programa existe o " +
                         "campo do executável do jogo.";
                return false;
            }

            // A mesma régua do executável e da capa: fora do HD do launcher, não entra na
            // biblioteca. Um script em C:\ ou num compartilhamento de rede simplesmente não
            // existe no notebook do amigo, e o caminho gravado traria uma letra de drive
            // junto — que é a regra que este projeto não quebra.
            if (!Caminhos.TentarParaRelativo(caminhoAbsoluto, out var convertido))
            {
                recusa = $"\"{caminhoAbsoluto}\" está fora do HD do launcher." + Environment.NewLine +
                         "O script precisa viajar junto com os jogos, senão ele não existe no " +
                         "próximo PC e o caminho salvo apontaria para o nada.";
                return false;
            }

            relativo = convertido;
            return true;
        }

        /// <summary>
        /// Roda o script de antes e ESPERA por ele, com teto de 30 s.
        ///
        /// Esperar é o ponto: o script existe para preparar o terreno (trocar um .ini, subir
        /// o x360ce, mexer na resolução) e o jogo não pode subir no meio disso.
        /// </summary>
        public static RetornoDoScript RodarAntes(string? relativo, string? pastaDoJogo)
        {
            if (string.IsNullOrWhiteSpace(relativo)) return Nada;

            if (ResolverOuAvisar(relativo!, "antes", out var caminho) is { } problema) return problema;

            Process? processo = null;
            try
            {
                processo = Iniciar(caminho!, PastaDeTrabalho(pastaDoJogo, caminho!));

                if (processo is null)
                {
                    return new RetornoDoScript(ResultadoDoScript.NaoRodou,
                        $"O Windows não abriu o script de antes (\"{relativo}\"). Abri o jogo mesmo assim.");
                }

                if (processo.WaitForExit((int)LimiteDoAntes.TotalMilliseconds))
                    return new RetornoDoScript(ResultadoDoScript.Ok);

                // Estourou o tempo e o script CONTINUA RODANDO — de propósito.
                //
                // Matá-lo seria a escolha destrutiva: ele pode estar no meio de uma cópia de
                // arquivo do jogo, e um script interrompido pela metade deixa o estado pior
                // do que se nunca tivesse rodado. Eu paro de esperar; ele termina sozinho.
                return new RetornoDoScript(ResultadoDoScript.TempoEsgotado,
                    $"O script de antes (\"{relativo}\") passou de {LimiteDoAntes.TotalSeconds:F0} s e " +
                    "continua rodando. Abri o jogo assim mesmo — se ele tem um \"pause\", tire: a " +
                    "janela dele fica escondida e não há como apertar tecla nenhuma.");
            }
            catch (Exception erro)
            {
                return new RetornoDoScript(ResultadoDoScript.NaoRodou,
                    $"Não consegui rodar o script de antes (\"{relativo}\"): {erro.Message}. " +
                    "Abri o jogo mesmo assim.");
            }
            finally
            {
                // Soltar o handle não mata o processo: no caso do tempo esgotado, o script
                // segue vivo e termina por conta própria.
                processo?.Dispose();
            }
        }

        /// <summary>
        /// Dispara o script de depois e NÃO espera por ele.
        ///
        /// Aqui não há o que esperar: o jogo já fechou e a janela está voltando. Segurar a
        /// volta do launcher por um script de limpeza seria pagar o custo no único momento
        /// em que eu estou olhando para a tela.
        /// </summary>
        public static RetornoDoScript RodarDepois(string? relativo, string? pastaDoJogo)
        {
            if (string.IsNullOrWhiteSpace(relativo)) return Nada;

            if (ResolverOuAvisar(relativo!, "depois", out var caminho) is { } problema) return problema;

            try
            {
                var processo = Iniciar(caminho!, PastaDeTrabalho(pastaDoJogo, caminho!));

                if (processo is null)
                {
                    return new RetornoDoScript(ResultadoDoScript.NaoRodou,
                        $"O Windows não abriu o script de depois (\"{relativo}\").");
                }

                processo.Dispose();
                return new RetornoDoScript(ResultadoDoScript.Ok);
            }
            catch (Exception erro)
            {
                return new RetornoDoScript(ResultadoDoScript.NaoRodou,
                    $"Não consegui rodar o script de depois (\"{relativo}\"): {erro.Message}.");
            }
        }

        // ---- Apoio ---------------------------------------------------------------------------

        /// <summary>
        /// Resolve o relativo para um caminho de verdade. Devolve o aviso pronto quando o
        /// arquivo sumiu, e null quando está tudo certo (com o caminho no <c>out</c>).
        /// </summary>
        private static RetornoDoScript? ResolverOuAvisar(string relativo, string quando, out string? caminho)
        {
            caminho = Caminhos.ParaAbsolutoOuNulo(relativo);

            if (caminho is not null && File.Exists(caminho)) return null;

            // Script configurado que não está mais lá é o caso comum de HD reorganizado, e
            // NÃO é motivo para não abrir o jogo — a spec grifa isso.
            return new RetornoDoScript(ResultadoDoScript.NaoEncontrado,
                $"O script de {quando} (\"{relativo}\") não está mais lá." +
                (quando == "antes" ? " Abri o jogo mesmo assim." : ""));
        }

        /// <summary>
        /// <c>cmd.exe /c call "script"</c>, e não o .bat direto.
        ///
        /// Não é firula: o <c>CreateProcess</c> — que é o que o .NET usa com
        /// <c>UseShellExecute = false</c> — não sabe rodar arquivo de lote. Sem o cmd, todo
        /// script morreria com "não é um aplicativo Win32 válido".
        ///
        /// O <c>call</c> na frente existe pelo motivo oposto ao que parece: com a linha
        /// começando por aspas, o <c>/c</c> do cmd tem uma regra própria de comer o primeiro
        /// e o último par — e um caminho com espaço se parte em dois no meio do caminho.
        /// Começando por <c>call</c>, a linha não dispara essa regra.
        /// </summary>
        private static Process? Iniciar(string caminhoDoScript, string pastaDeTrabalho)
        {
            var interpretador = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(interpretador)) interpretador = "cmd.exe";

            return Process.Start(new ProcessStartInfo
            {
                FileName = interpretador!,
                Arguments = $"/c call \"{caminhoDoScript}\"",

                // WorkingDirectory na pasta do JOGO, não na do script: é o que a spec pede,
                // e é o que faz um .bat de duas linhas com caminho relativo funcionar.
                WorkingDirectory = pastaDeTrabalho,
                UseShellExecute = false,

                // Sem janela preta piscando na frente do jogo. O preço é que um script com
                // "pause" trava até o teto dos 30 s — e é exatamente isso que o aviso do
                // tempo esgotado diz.
                CreateNoWindow = true
            });
        }

        private static string PastaDeTrabalho(string? pastaDoJogo, string caminhoDoScript)
        {
            if (!string.IsNullOrEmpty(pastaDoJogo) && Directory.Exists(pastaDoJogo)) return pastaDoJogo!;

            // Jogo sem pasta resolvível é caso de exe inválido, e o lançamento nem chega
            // aqui. Mas se chegar, a pasta do próprio script é melhor que o diretório de
            // trabalho do launcher.
            return Path.GetDirectoryName(caminhoDoScript) ?? Caminhos.PastaBase;
        }

        private static string NomeDoArquivo(string caminho)
        {
            try
            {
                return Path.GetFileName(caminho);
            }
            catch (Exception)
            {
                return caminho;
            }
        }
    }
}
