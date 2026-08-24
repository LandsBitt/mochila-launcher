// Código de diagnóstico: existe só na build de Debug.
//
// O launcher entregue não leva acervo sintético, bench nem suíte de testes junto —
// nada disso serve para quem só quer abrir um jogo, e cada KB conta num exe que
// roda de HD externo.
#if DEBUG

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Mochila.Dados;
using Mochila.Modelo;
using Mochila.Util;

namespace Mochila.Diagnostico
{
    /// <summary>
    /// Verificação automatizada da fase 12: <c>biblioteca.json</c> versão 3, busca com
    /// operadores e ações em lote.
    ///
    /// A parte mais importante daqui não é a que tem tela: é a migração. Um erro de
    /// escrita nesse caminho não aparece na hora — aparece meses depois, com o tempo
    /// jogado zerado e sem como voltar atrás. Por isso os testes de arquivo vêm primeiro e
    /// são os mais detalhados.
    /// </summary>
    public static class AutoTesteBiblioteca3
    {
        private const string NomePastaSandbox = "_autoteste-v3-tmp";

        public static bool Executar(Action<string> saida)
        {
            var v = new Verificador(saida);
            var raizReal = Caminhos.PastaBase;

            try
            {
                PrepararSandbox(raizReal);

                TestarMigracaoDaVersao1(v);
                TestarMigracaoDaVersao2(v);
                TestarCamposNovosNoDisco(v);
                TestarCampoDesconhecido(v);
                TestarSobrasDoConfig(v);
                TestarArquivoDoFuturo(v);
                TestarValoresForaDaFaixa(v);
                TestarValidacaoDosCaminhosGravados(v);
                TestarConjuntoDeCaracteresDoId(v);
                TestarNormalizacaoDeTags(v);
                TestarParserDaBusca(v);
                TestarFiltroComOperadores(v);

                Caminhos.RestaurarPastaBase();
                TestarSelecaoEAcoesEmLote(v);
            }
            catch (Exception ex)
            {
                v.RegistrarErro($"ERRO INESPERADO: {ex}");
            }
            finally
            {
                Caminhos.RestaurarPastaBase();
                LimparSandbox(v, raizReal);
            }

            v.Escrever("");
            v.Escrever($"Resultado: {v.Passou} passou, {v.Falhou} falhou.");
            return v.TudoPassou;
        }

        // ---- Migração ----------------------------------------------------------------------------

        /// <summary>
        /// Um biblioteca.json versão 1 de verdade, com <c>minutosJogados</c>. Ele tem que
        /// chegar em v3 com o tempo intacto — 137 minutos são 8220 segundos, não zero.
        /// </summary>
        private static void TestarMigracaoDaVersao1(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Migração 1 -> 3");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "v1.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 1,
  ""pastasEscaneadas"": [""Jogos""],
  ""jogos"": [
    {
      ""id"": ""nfsmw-black"",
      ""titulo"": ""Need for Speed: Most Wanted"",
      ""executavelRelativo"": ""Jogos\\NFS\\speed.exe"",
      ""capaArquivo"": ""nfsmw-black.jpg"",
      ""minutosJogados"": 137,
      ""favorito"": true
    }
  ]
}");

            var lida = Biblioteca.Carregar(arquivo);
            var jogo = lida.ObterPorId("nfsmw-black");

            v.Verificar("leu a biblioteca v1", jogo is not null);
            if (jogo is null) return;

            v.Verificar("minutosJogados vira segundos sem perder nada",
                jogo.SegundosJogados == 137 * 60, jogo.SegundosJogados.ToString());
            v.Verificar("favorito preservado", jogo.Favorito);
            v.Verificar("capa preservada", jogo.CapaArquivo == "nfsmw-black.jpg");
            v.Verificar("pasta escaneada preservada", lida.PastasEscaneadas.Count == 1);

            v.Verificar("campos do v3 ausentes caem no padrão",
                jogo.Tags.Count == 0 && jogo.Nota == 0 && jogo.Status == StatusDoJogo.Nenhum &&
                jogo.OpcoesDeExecucao.EhPadrao && jogo.BackupDeSave.EhPadrao);

            // A migração acontece na primeira gravação, sem passo separado.
            lida.Salvar(arquivo);
            var conteudo = ArquivoTexto.Ler(arquivo);

            v.Verificar("regravou como versão 3", conteudo.Contains("\"versao\": 3"));
            v.Verificar("e o campo velho não voltou para o arquivo", !conteudo.Contains("minutosJogados"));

            var relida = Biblioteca.Carregar(arquivo);
            v.Verificar("o tempo continua intacto depois da migração",
                relida.ObterPorId("nfsmw-black")?.SegundosJogados == 137 * 60);
        }

        private static void TestarMigracaoDaVersao2(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Migração 2 -> 3");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "v2.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 2,
  ""pastasEscaneadas"": [""Jogos""],
  ""jogos"": [
    {
      ""id"": ""carbon"",
      ""titulo"": ""NFS Carbon"",
      ""executavelRelativo"": ""Jogos\\Carbon\\nfsc.exe"",
      ""impressao"": ""nfsc.exe|6029312|c3a1f0d29b4e7c15"",
      ""segundosJogados"": 5400,
      ""ultimaVezJogado"": ""2026-08-10T21:30:00Z"",
      ""executavelFixadoPeloUsuario"": true
    }
  ]
}");

            var lida = Biblioteca.Carregar(arquivo);
            var jogo = lida.ObterPorId("carbon");

            v.Verificar("v2 continua sendo lido", jogo is not null);
            if (jogo is null) return;

            v.Verificar("a impressão da fase 10 atravessa o bump",
                jogo.Impressao == "nfsc.exe|6029312|c3a1f0d29b4e7c15", jogo.Impressao);
            v.Verificar("segundos preservados", jogo.SegundosJogados == 5400);
            v.Verificar("data preservada em UTC",
                jogo.UltimaVezJogado == new DateTime(2026, 8, 10, 21, 30, 0, DateTimeKind.Utc));
            v.Verificar("marca de executável fixado preservada", jogo.ExecutavelFixadoPeloUsuario);

            lida.Salvar(arquivo);
            v.Verificar("regravou como versão 3", Biblioteca.Carregar(arquivo).Versao == 3);
        }

        /// <summary>
        /// Ida e volta dos campos que o bump trouxe — inclusive os que só ganham tela nas
        /// fases 15 e 16. Se eles não sobrevivem ao disco agora, o bump único não serviu
        /// para nada.
        /// </summary>
        private static void TestarCamposNovosNoDisco(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Campos do schema v3 no disco");

            var biblioteca = new Biblioteca();
            var jogo = new Jogo
            {
                Id = "nfsmw-black",
                Titulo = "Need for Speed: Most Wanted (Black Edition)",
                ExecutavelRelativo = @"Jogos\NFS\speed.exe",
                HeroArquivo = "nfsmw-black_hero.jpg",
                LogoArquivo = "nfsmw-black_logo.png",
                Nota = 4,
                Status = StatusDoJogo.Zerado
            };
            jogo.Tags.Add("corrida");
            jogo.Tags.Add("ea");
            jogo.OpcoesDeExecucao.Prioridade = PrioridadeDoProcesso.Alta;
            jogo.OpcoesDeExecucao.ScriptAntes = @"Jogos\NFS\dgvoodoo.bat";
            jogo.BackupDeSave.Ativo = true;
            jogo.BackupDeSave.Pasta = @"%APPDATA%\NFS Most Wanted";
            jogo.BackupDeSave.VersoesMantidas = 3;

            biblioteca.Jogos.Add(jogo);

            var arquivo = Path.Combine(Caminhos.PastaEstado, "v3.json");
            biblioteca.Salvar(arquivo);

            var lido = Biblioteca.Carregar(arquivo).ObterPorId("nfsmw-black");
            v.Verificar("releu o jogo", lido is not null);
            if (lido is null) return;

            v.Verificar("hero e logo preservados",
                lido.HeroArquivo == "nfsmw-black_hero.jpg" && lido.LogoArquivo == "nfsmw-black_logo.png");
            v.Verificar("tags preservadas na ordem",
                lido.Tags.Count == 2 && lido.Tags[0] == "corrida" && lido.Tags[1] == "ea",
                string.Join(",", lido.Tags.ToArray()));
            v.Verificar("nota preservada", lido.Nota == 4);
            v.Verificar("status preservado", lido.Status == StatusDoJogo.Zerado);
            v.Verificar("prioridade preservada", lido.OpcoesDeExecucao.Prioridade == PrioridadeDoProcesso.Alta);
            v.Verificar("scriptAntes preservado", lido.OpcoesDeExecucao.ScriptAntes == @"Jogos\NFS\dgvoodoo.bat");
            v.Verificar("scriptDepois continua nulo", lido.OpcoesDeExecucao.ScriptDepois is null);
            v.Verificar("backup ligado preservado", lido.BackupDeSave.Ativo);
            v.Verificar("versoesMantidas preservado", lido.BackupDeSave.VersoesMantidas == 3);
            v.Verificar("pasta do backup preservada",
                lido.BackupDeSave.Pasta == @"%APPDATA%\NFS Most Wanted", lido.BackupDeSave.Pasta ?? "(nulo)");

            var conteudo = ArquivoTexto.Ler(arquivo);
            v.Verificar("o token do %APPDATA% volta como token, não expandido",
                conteudo.Contains(@"%APPDATA%\\NFS Most Wanted"), conteudo.Contains("Users").ToString());
            v.Verificar("status vai para o disco com hífen", conteudo.Contains("\"status\": \"zerado\""));
        }

        /// <summary>
        /// O saco de sobras. Um campo que este binário não conhece — de uma versão futura
        /// ou de uma edição minha à mão — tem que voltar ao disco exatamente como entrou,
        /// inclusive quando é objeto ou lista.
        /// </summary>
        private static void TestarCampoDesconhecido(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Campo desconhecido sobrevive à ida e volta");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "sobras.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 3,
  ""pastasEscaneadas"": [""Jogos""],
  ""jogos"": [
    {
      ""id"": ""futuro"",
      ""titulo"": ""Jogo do Futuro"",
      ""executavelRelativo"": ""Jogos\\Futuro\\jogo.exe"",
      ""nota"": 2,
      ""conquistas"": { ""total"": 42, ""obtidas"": [""primeira"", ""segunda""] },
      ""plataformaId"": ""ps1""
    }
  ]
}");

            var biblioteca = Biblioteca.Carregar(arquivo);
            var jogo = biblioteca.ObterPorId("futuro");

            v.Verificar("os campos conhecidos foram lidos normalmente", jogo?.Nota == 2);
            v.Verificar("os desconhecidos foram guardados",
                jogo != null && jogo.Sobras.Count == 2, jogo?.Sobras.Count.ToString());

            // Uma gravação qualquer (favoritar, contar sessão) não pode apagá-los.
            jogo!.Favorito = true;
            biblioteca.Salvar(arquivo);

            var conteudo = ArquivoTexto.Ler(arquivo);
            v.Verificar("o campo simples voltou ao disco", conteudo.Contains("\"plataformaId\": \"ps1\""));
            v.Verificar("o objeto aninhado voltou como objeto, não como lista de pares",
                conteudo.Contains("\"total\": 42") && !conteudo.Contains("KeyValuePair"), conteudo);
            v.Verificar("a lista de dentro dele voltou inteira",
                conteudo.Contains("\"primeira\"") && conteudo.Contains("\"segunda\""));

            var relido = Biblioteca.Carregar(arquivo).ObterPorId("futuro");
            v.Verificar("e continuam legíveis na releitura", relido != null && relido.Sobras.Count == 2);
            v.Verificar("sem virar campo conhecido por engano", relido?.Nota == 2 && relido.Favorito);
        }

        private static void TestarSobrasDoConfig(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Campo desconhecido no config.json");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "config-futuro.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 1,
  ""steamGridDbApiKey"": """",
  ""tamanhoCard"": ""G"",
  ""temaClaro"": true,
  ""corDeAcento"": ""#78B4FF""
}");

            var config = Config.Carregar(arquivo);
            v.Verificar("os campos conhecidos foram lidos", config.TamanhoCard == TamanhoCard.G);
            v.Verificar("os desconhecidos foram guardados", config.Sobras.Count == 2,
                config.Sobras.Count.ToString());

            // Trocar o tamanho do card não pode apagar a preferência de uma versão nova.
            config.TamanhoCard = TamanhoCard.P;
            config.Salvar(arquivo);

            var conteudo = ArquivoTexto.Ler(arquivo);
            v.Verificar("a preferência desconhecida voltou ao disco",
                conteudo.Contains("\"corDeAcento\": \"#78B4FF\"") && conteudo.Contains("\"temaClaro\": true"),
                conteudo);
        }

        /// <summary>
        /// Arquivo de uma versão que este binário não conhece: abre para olhar e não grava
        /// nada. É a regra que impede um downgrade de destruir o que a versão nova
        /// escreveu — e ela precisa valer inclusive para o lançamento de um jogo.
        /// </summary>
        private static void TestarArquivoDoFuturo(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Biblioteca de versão mais nova (somente leitura)");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "v4.json");
            const string original = @"{
  ""versao"": 4,
  ""pastasEscaneadas"": [""Jogos""],
  ""jogos"": [
    {
      ""id"": ""futuro"",
      ""titulo"": ""Jogo de Amanhã"",
      ""executavelRelativo"": ""Jogos\\Futuro\\jogo.exe"",
      ""segundosJogados"": 600,
      ""campoQueAindaNaoExiste"": ""importante""
    }
  ]
}";
            ArquivoTexto.EscreverAtomico(arquivo, original);

            var biblioteca = Biblioteca.Carregar(arquivo);

            v.Verificar("abre normalmente", biblioteca.Jogos.Count == 1);
            v.Verificar("e entra em somente leitura", biblioteca.SomenteLeitura);
            v.Verificar("a versão lida é a do arquivo, não a nossa", biblioteca.Versao == 4);

            // A sessão de um jogo: contabilizar e salvar. Nenhuma das duas pode encostar
            // no arquivo.
            biblioteca.Jogos[0].SegundosJogados += 3600;
            biblioteca.Jogos[0].Favorito = true;
            biblioteca.Salvar(arquivo);

            v.Verificar("Salvar não grava nada (o arquivo continua idêntico)",
                ArquivoTexto.Ler(arquivo) == original);

            var relida = Biblioteca.Carregar(arquivo);
            v.Verificar("o tempo alterado em memória não chegou ao disco",
                relida.Jogos[0].SegundosJogados == 600, relida.Jogos[0].SegundosJogados.ToString());
            v.Verificar("nem o favorito", !relida.Jogos[0].Favorito);
            v.Verificar("e a versão do arquivo continua 4", relida.Versao == 4);

            // Biblioteca da versão atual não é somente leitura, obviamente.
            var normal = new Biblioteca();
            v.Verificar("biblioteca nova grava normalmente", !normal.SomenteLeitura);
        }

        /// <summary>
        /// A spec avisa que <c>Biblioteca.Validar</c> só conferia <c>ExecutavelRelativo</c> e
        /// <c>PastasEscaneadas</c> — e que por isso um caminho absoluto gravado em qualquer
        /// outro campo passaria batido. Estes são os scripts da fase 15 entrando na
        /// validação antes de existir tela para eles.
        /// </summary>
        private static void TestarValidacaoDosCaminhosGravados(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Validação dos caminhos gravados fora de executavelRelativo");

            var biblioteca = new Biblioteca();
            var jogo = new Jogo { Id = "teste", Titulo = "Teste", ExecutavelRelativo = @"Jogos\t\t.exe" };
            biblioteca.Jogos.Add(jogo);

            v.Verificar("biblioteca sem script é válida", biblioteca.Validar().Count == 0,
                string.Join(" | ", biblioteca.Validar().ToArray()));

            jogo.OpcoesDeExecucao.ScriptAntes = @"D:\scripts\dgvoodoo.bat";
            v.Verificar("script com letra de drive é recusado por Validar", biblioteca.Validar().Count == 1);

            var destino = Path.Combine(Caminhos.PastaEstado, "nao-deve-existir-script.json");
            v.Verificar("e Salvar recusa gravar isso",
                Lanca<InvalidOperationException>(() => biblioteca.Salvar(destino)));
            v.Verificar("o arquivo inválido não foi criado", !File.Exists(destino));

            jogo.OpcoesDeExecucao.ScriptAntes = @"Jogos\t\dgvoodoo.bat";
            v.Verificar("e o mesmo script relativo passa", biblioteca.Validar().Count == 0);

            jogo.OpcoesDeExecucao.ScriptDepois = @"C:\Windows\System32\limpar.cmd";
            v.Verificar("scriptDepois absoluto também é recusado", biblioteca.Validar().Count == 1);
        }

        /// <summary>
        /// O <c>id</c> é nome de arquivo em <c>&lt;id&gt;_thumb.jpg</c> e na capa, e vira
        /// nome de pasta quando os saves portáteis chegarem (ESPEC-v3). O invariante é o
        /// conjunto de caracteres, não a igualdade com <c>Textos.Slug</c> — a v3
        /// (emuladores) vai gerar id composto com sublinhado, e a regra escrita assim não
        /// precisa ser reaberta lá.
        /// </summary>
        private static void TestarConjuntoDeCaracteresDoId(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Conjunto de caracteres do id (é nome de arquivo da capa e do thumb)");

            v.Verificar("slug comum é id válido", Textos.EhIdValido("nfsmw-black"));
            v.Verificar("id composto com sublinhado é válido (a v3 vai gerar)",
                Textos.EhIdValido("ps1_crash"));
            v.Verificar("dois-pontos não é id válido (viraria alternate data stream)",
                !Textos.EhIdValido("ps1:crash"));
            v.Verificar("barra não é id válido", !Textos.EhIdValido(@"ps1\crash"));
            v.Verificar("espaço não é id válido", !Textos.EhIdValido("nfs mw"));
            v.Verificar("maiúscula não é id válido", !Textos.EhIdValido("NFS"));
            v.Verificar("vazio não é id válido", !Textos.EhIdValido(""));

            // Todo id que o launcher gera cabe na regra, venha de que título vier.
            var biblioteca = new Biblioteca();
            var titulos = new[]
            {
                "Need for Speed: Most Wanted (Black Edition)", "Coração de Aço — Edição Especial",
                "!!!", "S.T.A.L.K.E.R.: Shadow of Chernobyl", "F.E.A.R. 2 / Project Origin",
                "Tom Clancy's Splinter Cell", "50% Off", "*.*"
            };

            var todosValidos = true;
            foreach (var titulo in titulos)
            {
                if (!Textos.EhIdValido(biblioteca.GerarId(titulo))) todosValidos = false;
            }

            v.Verificar("todo id gerado pelo launcher cabe no conjunto permitido", todosValidos);

            // Id vindo de arquivo editado à mão é saneado na leitura, antes de virar
            // nome de arquivo em qualquer lugar.
            var arquivo = Path.Combine(Caminhos.PastaEstado, "id-torto.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 3,
  ""jogos"": [
    { ""id"": ""NFS: Most Wanted"", ""titulo"": ""NFS"", ""executavelRelativo"": ""Jogos\\NFS\\speed.exe"" }
  ]
}");

            var lida = Biblioteca.Carregar(arquivo);
            v.Verificar("id torto do arquivo é saneado na leitura",
                lida.Jogos.Count == 1 && Textos.EhIdValido(lida.Jogos[0].Id), lida.Jogos[0].Id);
            v.Verificar("e o saneamento preserva o que dá para preservar",
                lida.Jogos[0].Id == "nfs-most-wanted", lida.Jogos[0].Id);
            v.Verificar("a biblioteca com id saneado grava sem reclamar", lida.Validar().Count == 0);

            // Sublinhado sobrevive ao saneamento (Slug o transformaria em hífen).
            v.Verificar("saneamento preserva sublinhado", Textos.SanearId("ps1_crash") == "ps1_crash");
        }

        private static void TestarValoresForaDaFaixa(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Lixo no arquivo não derruba a leitura");

            var arquivo = Path.Combine(Caminhos.PastaEstado, "lixo.json");
            ArquivoTexto.EscreverAtomico(arquivo, @"{
  ""versao"": 3,
  ""jogos"": [
    {
      ""id"": ""torto"",
      ""titulo"": ""Torto"",
      ""executavelRelativo"": ""Jogos\\Torto\\t.exe"",
      ""nota"": 99,
      ""status"": ""inventado"",
      ""tags"": [""  MUNDO ABERTO  "", """", ""corrida""],
      ""backupDeSave"": { ""ativo"": true, ""versoesMantidas"": 0 }
    }
  ]
}");

            var jogo = Biblioteca.Carregar(arquivo).ObterPorId("torto");
            v.Verificar("leu o jogo", jogo is not null);
            if (jogo is null) return;

            v.Verificar("nota acima de 5 é grampeada", jogo.Nota == 5, jogo.Nota.ToString());
            v.Verificar("status inventado vira 'nenhum'", jogo.Status == StatusDoJogo.Nenhum);
            v.Verificar("tag vazia é descartada", jogo.Tags.Count == 2, string.Join(",", jogo.Tags.ToArray()));
            v.Verificar("tag com espaço vira hífen e minúscula",
                jogo.Tags[0] == "mundo-aberto", jogo.Tags[0]);
            v.Verificar("versoesMantidas zero cai no mínimo de 1",
                jogo.BackupDeSave.VersoesMantidas >= 1, jogo.BackupDeSave.VersoesMantidas.ToString());
        }

        private static void TestarNormalizacaoDeTags(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Regras das tags");

            var tags = new List<string>();

            v.Verificar("acrescenta uma tag", Etiquetas.Acrescentar(tags, "Corrida"));
            v.Verificar("gravada em minúscula", tags[0] == "corrida", tags[0]);
            v.Verificar("a mesma tag não entra duas vezes", !Etiquetas.Acrescentar(tags, "CORRIDA"));
            v.Verificar("nem com acentuação diferente", !Etiquetas.Acrescentar(tags, "corridá"));

            v.Verificar("tag vazia não entra", !Etiquetas.Acrescentar(tags, "   "));
            v.Verificar("o '#' que eu digito junto é descartado",
                Etiquetas.Acrescentar(tags, "#indie") && tags.Contains("indie"));

            Etiquetas.Acrescentar(tags, "Mundo Aberto");
            v.Verificar("espaço vira hífen (senão a busca por termos quebraria)",
                tags.Contains("mundo-aberto"), string.Join(",", tags.ToArray()));

            v.Verificar("casa por prefixo, sem acento", Etiquetas.Casa(tags, "cor"));
            v.Verificar("e não casa com o que não começa igual", !Etiquetas.Casa(tags, "rida"));

            v.Verificar("remove sem ligar para caixa", Etiquetas.Remover(tags, "CORRIDA"));
            v.Verificar("remover o que não existe devolve false", !Etiquetas.Remover(tags, "corrida"));

            // Acento na tag é preservado na grafia e ignorado na comparação.
            var comAcento = new List<string>();
            Etiquetas.Acrescentar(comAcento, "Ação");
            v.Verificar("a grafia com acento é preservada", comAcento[0] == "ação", comAcento[0]);
            v.Verificar("mas '#acao' encontra", Etiquetas.Casa(comAcento, "acao"));

            // Frequência: ordena por uso e desempata pelo nome.
            var jogos = new List<Jogo>();
            for (var i = 0; i < 5; i++)
            {
                var jogo = new Jogo { Id = $"j{i}", Titulo = $"J{i}", ExecutavelRelativo = $@"Jogos\j{i}\j.exe" };
                Etiquetas.Acrescentar(jogo.Tags, "corrida");
                if (i < 2) Etiquetas.Acrescentar(jogo.Tags, "indie");
                jogos.Add(jogo);
            }

            var frequencia = Etiquetas.PorFrequencia(jogos);
            v.Verificar("a tag mais usada vem primeiro",
                frequencia.Count == 2 && frequencia[0].Tag == "corrida" && frequencia[0].Quantidade == 5,
                frequencia.Count > 0 ? $"{frequencia[0].Tag}={frequencia[0].Quantidade}" : "vazio");
        }

        // ---- Busca com operadores ----------------------------------------------------------------

        private static void TestarParserDaBusca(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Parser da busca");

            var tag = ConsultaDeBusca.Analisar("#corrida");
            v.Verificar("#corrida vira filtro de tag",
                tag.Tags.Count == 1 && tag.Tags[0] == "corrida" && tag.Termos.Count == 0);

            var status = ConsultaDeBusca.Analisar("@zerado");
            v.Verificar("@zerado vira filtro de status",
                status.Status.Count == 1 && status.Status[0] == "zerado");

            var nota = ConsultaDeBusca.Analisar("*4");
            v.Verificar("*4 vira nota mínima 4", nota.NotaMinima == 4);

            var texto = ConsultaDeBusca.Analisar("most wanted");
            v.Verificar("texto solto continua sendo termo de título",
                texto.Termos.Count == 2 && !texto.TemOperador);

            var combinado = ConsultaDeBusca.Analisar("#corrida @zerado *4");
            v.Verificar("os três combinam",
                combinado.Tags.Count == 1 && combinado.Status.Count == 1 && combinado.NotaMinima == 4);

            var misturado = ConsultaDeBusca.Analisar("#corrida speed @jogando");
            v.Verificar("operador e texto se misturam na mesma busca",
                misturado.Tags.Count == 1 && misturado.Termos.Count == 1 && misturado.Status.Count == 1,
                $"tags={misturado.Tags.Count} termos={misturado.Termos.Count}");

            var duasTags = ConsultaDeBusca.Analisar("#corrida #ea");
            v.Verificar("duas tags somam (é E, não OU)", duasTags.Tags.Count == 2);

            // Operador sem conteúdo é o estado normal enquanto eu digito.
            var sozinhos = ConsultaDeBusca.Analisar("# @ *");
            v.Verificar("'#', '@' e '*' sozinhos são ignorados", sozinhos.Vazia,
                $"tags={sozinhos.Tags.Count} status={sozinhos.Status.Count} nota={sozinhos.NotaMinima}");

            v.Verificar("busca vazia não filtra nada", ConsultaDeBusca.Analisar("").Vazia);
            v.Verificar("só espaços também não", ConsultaDeBusca.Analisar("    ").Vazia);
            v.Verificar("*9 é grampeado em 5", ConsultaDeBusca.Analisar("*9").NotaMinima == 5);
            v.Verificar("*abc não vira nota", ConsultaDeBusca.Analisar("*abc").NotaMinima == 0);

            v.Verificar("acento e caixa não importam no operador",
                ConsultaDeBusca.Analisar("#AÇÃO").Tags[0] == "ação",
                ConsultaDeBusca.Analisar("#AÇÃO").Tags[0]);
        }

        private static void TestarFiltroComOperadores(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Filtro com operadores");

            var corrida = Montar("corrida-zerado", "Need for Speed", StatusDoJogo.Zerado, 5, "corrida", "ea");
            var indie = Montar("indie", "Celeste", StatusDoJogo.QueroJogar, 3, "indie");
            var semNada = Montar("sem-nada", "Jogo Pelado", StatusDoJogo.Nenhum, 0);

            var acervo = new List<Jogo> { corrida, indie, semNada };

            v.Verificar("#corrida traz só o de corrida",
                Filtrar(acervo, "#corrida") == "corrida-zerado");

            v.Verificar("@zerado traz só o zerado",
                Filtrar(acervo, "@zerado") == "corrida-zerado");

            v.Verificar("@quero casa por prefixo com quero-jogar",
                Filtrar(acervo, "@quero") == "indie");

            v.Verificar("*3 traz os de nota 3 para cima",
                Filtrar(acervo, "*3") == "corrida-zerado,indie");

            v.Verificar("combinado filtra pela interseção",
                Filtrar(acervo, "#corrida @zerado *4") == "corrida-zerado");

            v.Verificar("operador mais texto de título",
                Filtrar(acervo, "#corrida speed") == "corrida-zerado");

            v.Verificar("texto que não casa esvazia a lista",
                Filtrar(acervo, "#corrida celeste") == "");

            v.Verificar("dois status diferentes não casam com nada (um jogo tem um só)",
                Filtrar(acervo, "@zerado @jogando") == "");

            v.Verificar("tag inexistente devolve lista vazia, não erro",
                Filtrar(acervo, "#naoexiste") == "");

            v.Verificar("busca vazia devolve tudo",
                Filtrar(acervo, "").Split(',').Length == 3);
        }

        // ---- Multi-seleção e ações em lote --------------------------------------------------------

        /// <summary>
        /// A regra que a spec cobra por escrito: <b>ação em lote sobre a seleção filtrada
        /// não toca em jogo fora do filtro</b>. Aqui ela é testada com a janela de verdade,
        /// digitando na busca e usando o mesmo Ctrl+A do teclado.
        /// </summary>
        private static void TestarSelecaoEAcoesEmLote(Verificador v)
        {
            v.Escrever("");
            v.Escrever("Multi-seleção e ações em lote");

            AcervoDeDemonstracao.Montar(24);

            try
            {
                using (var janela = new FormPrincipal())
                {
                    janela.StartPosition = FormStartPosition.Manual;
                    janela.ShowInTaskbar = false;
                    janela.Size = new Size(1200, 800);
                    janela.Location = new Point(-32000, -32000);
                    janela.Show();

                    var grade = janela.GradeParaDiagnostico;
                    var acervo = janela.BibliotecaParaDiagnostico;

                    // Sem marcação, a "seleção" é o card selecionado — é o que faz o mesmo
                    // menu servir para um jogo e para trinta.
                    grade.Selecionar(0);
                    var umSo = grade.SelecaoParaAcao();
                    v.Verificar("sem marcação, a ação vale para o card selecionado",
                        umSo.Count == 1 && umSo[0].Id == grade.JogoSelecionado?.Id);

                    // Ctrl+clique marca; o teste chama o mesmo método do clique.
                    grade.AlternarMarcacao(1);
                    grade.AlternarMarcacao(2);
                    v.Verificar("Ctrl+clique marca dois", grade.QuantidadeMarcada == 2);
                    v.Verificar("e a ação passa a valer para os marcados, não para o selecionado",
                        grade.SelecaoParaAcao().Count == 2);

                    grade.AlternarMarcacao(2);
                    v.Verificar("Ctrl+clique de novo desmarca", grade.QuantidadeMarcada == 1);

                    // Shift+setas: intervalo contínuo a partir da âncora.
                    grade.LimparMarcacao();
                    grade.Selecionar(0);
                    janela.TratarSelecaoMultiplaParaDiagnostico(Keys.Right, Keys.Shift);
                    janela.TratarSelecaoMultiplaParaDiagnostico(Keys.Right, Keys.Shift);

                    v.Verificar("Shift+→ duas vezes marca três cards", grade.QuantidadeMarcada == 3,
                        grade.QuantidadeMarcada.ToString());

                    janela.TratarSelecaoMultiplaParaDiagnostico(Keys.Left, Keys.Shift);
                    v.Verificar("Shift+← encolhe a faixa em vez de somar", grade.QuantidadeMarcada == 2,
                        grade.QuantidadeMarcada.ToString());

                    // Ctrl+A dentro do filtro: é aqui que a regra da spec é provada.
                    janela.BuscarParaDiagnostico("#corrida");
                    var visiveis = grade.Jogos.Count;

                    v.Verificar("a busca por tag filtrou a grade", visiveis > 0 && visiveis < acervo.Jogos.Count,
                        $"{visiveis} de {acervo.Jogos.Count}");

                    janela.TratarSelecaoMultiplaParaDiagnostico(Keys.A, Keys.Control);
                    v.Verificar("Ctrl+A marca só o que está à vista",
                        grade.QuantidadeMarcada == visiveis, grade.QuantidadeMarcada.ToString());

                    var alvos = grade.SelecaoParaAcao();

                    // As notas de fora, anotadas ANTES: o acervo sintético já nasce com
                    // notas variadas, e comparar com "== 5" acusaria mudança onde não houve.
                    var notasDeFora = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var jogo in acervo.Jogos)
                    {
                        if (!alvos.Contains(jogo)) notasDeFora[jogo.Id] = jogo.Nota;
                    }

                    janela.LoteParaDiagnostico.DefinirNota(alvos, 5);

                    var todosMarcadosComCinco = true;
                    foreach (var jogo in alvos)
                    {
                        if (jogo.Nota != 5) todosMarcadosComCinco = false;
                    }

                    var nenhumForaMudou = true;
                    foreach (var par in notasDeFora)
                    {
                        if (acervo.ObterPorId(par.Key)?.Nota != par.Value) nenhumForaMudou = false;
                    }

                    v.Verificar("a nota em lote alcançou todos os filtrados", todosMarcadosComCinco);
                    v.Verificar("e NENHUM jogo fora do filtro foi tocado", nenhumForaMudou);

                    v.Verificar("a mudança foi para o disco",
                        Biblioteca.Carregar().ObterPorId(alvos[0].Id)?.Nota == 5);

                    // Status e tag em lote pelo mesmo caminho.
                    janela.LoteParaDiagnostico.DefinirStatus(alvos, StatusDoJogo.Jogando);
                    var todosJogando = true;
                    foreach (var jogo in alvos)
                    {
                        if (jogo.Status != StatusDoJogo.Jogando) todosJogando = false;
                    }
                    v.Verificar("status em lote aplicado a todos", todosJogando);

                    // Filtro que exclui: a marcação não pode sobreviver escondida.
                    janela.BuscarParaDiagnostico("#indie");
                    var sobreviventes = 0;
                    foreach (var jogo in grade.Jogos)
                    {
                        if (grade.EstaMarcado(jogo)) sobreviventes++;
                    }

                    v.Verificar("trocar o filtro não deixa marcação escondida",
                        grade.QuantidadeMarcada == sobreviventes,
                        $"marcados={grade.QuantidadeMarcada} visíveis marcados={sobreviventes}");

                    janela.BuscarParaDiagnostico("");
                    janela.Close();
                }
            }
            finally
            {
                AcervoDeDemonstracao.Limpar();
            }
        }

        // ---- Apoio -------------------------------------------------------------------------------

        private static Jogo Montar(string id, string titulo, StatusDoJogo status, int nota, params string[] tags)
        {
            var jogo = new Jogo
            {
                Id = id,
                Titulo = titulo,
                ExecutavelRelativo = $@"Jogos\{id}\jogo.exe",
                Status = status,
                Nota = nota
            };

            foreach (var tag in tags) Etiquetas.Acrescentar(jogo.Tags, tag);
            return jogo;
        }

        /// <summary>Os ids que sobraram do filtro, em ordem, como texto — fácil de comparar.</summary>
        private static string Filtrar(List<Jogo> acervo, string busca)
        {
            var resultado = FiltroDaBiblioteca.Aplicar(acervo, busca, OrdenacaoBiblioteca.Alfabetica, false);

            var ids = new List<string>(resultado.Count);
            foreach (var jogo in resultado) ids.Add(jogo.Id);

            ids.Sort(StringComparer.Ordinal);
            return string.Join(",", ids.ToArray());
        }

        /// <summary>Verdadeiro se a acao lancar exatamente a excecao esperada.</summary>
        private static bool Lanca<TExcecao>(Action acao) where TExcecao : Exception
        {
            try
            {
                acao();
                return false;
            }
            catch (TExcecao)
            {
                return true;
            }
        }

        private static void PrepararSandbox(string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox, "hd");
            Directory.CreateDirectory(sandbox);
            Caminhos.DefinirPastaBase(sandbox);
            Caminhos.GarantirEstrutura();
        }

        private static void LimparSandbox(Verificador v, string raizReal)
        {
            var sandbox = Path.Combine(raizReal, NomePastaSandbox);
            try
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception ex)
            {
                v.Escrever($"Aviso: não consegui apagar {sandbox} ({ex.Message})");
            }
        }
    }
}
#endif   // DEBUG
