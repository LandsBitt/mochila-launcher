using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Mochila.Scanner;

namespace Mochila.Execucao
{
    /// <summary>
    /// Uma classe COM do pacote DirectX: o CLSID e a DLL que responde por ele.
    /// </summary>
    public sealed class ClasseComDoDirectX
    {
        public ClasseComDoDirectX(string clsid, string dll, string descricao)
        {
            Clsid = clsid;
            Dll = dll;
            Descricao = descricao;
        }

        /// <summary>Com chaves: <c>{5a508685-...}</c>.</summary>
        public string Clsid { get; }

        public string Dll { get; }

        public string Descricao { get; }
    }

    /// <summary>
    /// O pedaço do runtime DirectX que o PATH não resolve: XACT e XAudio2 até a 2.7.
    ///
    /// Essas duas bibliotecas não são carregadas pelo nome do arquivo. O jogo pede ao
    /// Windows "me dê a classe {CLSID}", e o Windows procura no registro qual DLL responde
    /// por ela. O instalador oficial grava isso em HKLM, que exige admin. Mas o Windows
    /// também procura em <b>HKCU\Software\Classes</b>, que é do próprio usuário e não pede
    /// elevação nenhuma, e é aqui que eu gravo.
    ///
    /// Três regras deixam isso seguro:
    ///
    /// 1. <b>Só registro o que o PC não tem.</b> HKCU tem prioridade sobre HKLM, então
    ///    registrar por cima de um DirectX instalado trocaria a DLL do sistema pela do HD.
    ///    PC com o runtime instalado não ganha chave nenhuma.
    /// 2. <b>Toda chave minha leva a marca <see cref="ValorDaMarca"/>.</b> Chave sem a marca
    ///    não é minha e nunca é tocada, nem para sobrescrever nem para apagar.
    /// 3. <b>Tudo é temporário.</b> Gravo antes do jogo e apago quando ele fecha. Se o
    ///    Mochila morrer no meio, a próxima abertura limpa (ver <see cref="Remover"/>):
    ///    a chave aponta para uma letra de drive, e em outro dia o HD pode ser outra letra.
    ///
    /// A tabela saiu do registro de um Windows 11 com o redist de junho de 2010 instalado.
    /// Os CLSIDs são os mesmos em 32 e 64 bits: o que muda é a visão do registro
    /// (<c>Wow6432Node</c> para os jogos de 32 bits), e o <see cref="RegistryView"/> cuida disso.
    /// </summary>
    public static class RegistroComDoDirectX
    {
        /// <summary>Valor gravado em toda chave criada aqui. É o que me deixa apagar só o que é meu.</summary>
        public const string ValorDaMarca = "MochilaRuntimeDirectX";

        private const string RaizDasClasses = @"Software\Classes\CLSID";

        public static readonly IReadOnlyList<ClasseComDoDirectX> ClassesDoPacote = new[]
        {
            // XACT 2.x (2005–2007): Guitar Hero III, jogos da Microsoft Game Studios.
            new ClasseComDoDirectX("{0aa000aa-f404-11d9-bd7a-0010dc4f8f81}", "xactengine2_0.dll", "XACT Engine"),
            new ClasseComDoDirectX("{1f1b577e-5e5a-4e8a-ba73-c657ea8e8598}", "xactengine2_1.dll", "XACT Engine"),
            new ClasseComDoDirectX("{c60fae90-4183-4a3f-b2f7-ac1dc49b0e5c}", "xactengine2_2.dll", "XACT Engine"),
            new ClasseComDoDirectX("{1138472b-d187-44e9-81f2-ae1b0e7785f1}", "xactengine2_3.dll", "XACT Engine"),
            new ClasseComDoDirectX("{bc3e0fc6-2e0d-4c45-bc61-d9c328319bd8}", "xactengine2_4.dll", "XACT Engine"),
            new ClasseComDoDirectX("{54b68bc7-3a45-416b-a8c9-19bf19ec1df5}", "xactengine2_5.dll", "XACT Engine"),
            new ClasseComDoDirectX("{3a2495ce-31d0-435b-8ccf-e9f0843fd960}", "xactengine2_6.dll", "XACT Engine"),
            new ClasseComDoDirectX("{cd0d66ec-8057-43f5-acbd-66dfb36fd78c}", "xactengine2_7.dll", "XACT Engine"),
            new ClasseComDoDirectX("{77c56bf4-18a1-42b0-88af-5072ce814949}", "xactengine2_8.dll", "XACT Engine"),
            new ClasseComDoDirectX("{343e68e6-8f82-4a8d-a2da-6e9a944b378c}", "xactengine2_9.dll", "XACT Engine"),
            new ClasseComDoDirectX("{65d822a4-4799-42c6-9b18-d26cf66dd320}", "xactengine2_10.dll", "XACT Engine"),

            // XACT 3.x (2007–2010).
            new ClasseComDoDirectX("{3b80ee2a-b0f5-4780-9e30-90cb39685b03}", "xactengine3_0.dll", "XACT Engine"),
            new ClasseComDoDirectX("{962f5027-99be-4692-a468-85802cf8de61}", "xactengine3_1.dll", "XACT Engine"),
            new ClasseComDoDirectX("{d3332f02-3dd0-4de9-9aec-20d85c4111b6}", "xactengine3_2.dll", "XACT Engine"),
            new ClasseComDoDirectX("{94c1affa-66e7-4961-9521-cfdef3128d4f}", "xactengine3_3.dll", "XACT Engine"),
            new ClasseComDoDirectX("{0977d092-2d95-4e43-8d42-9ddcc2545ed5}", "xactengine3_4.dll", "XACT Engine"),
            new ClasseComDoDirectX("{074b110f-7f58-4743-aea5-12f15b5074ed}", "xactengine3_5.dll", "XACT Engine"),
            new ClasseComDoDirectX("{248d8a3b-6256-44d3-a018-2ac96c459f47}", "xactengine3_6.dll", "XACT Engine"),
            new ClasseComDoDirectX("{bcc782bc-6492-4c22-8c35-f5d72fe73c6e}", "xactengine3_7.dll", "XACT Engine"),

            // XAudio2 2.0–2.7: o motor e os dois efeitos que moram na mesma DLL.
            new ClasseComDoDirectX("{fac23f48-31f5-45a8-b49b-5225d61401aa}", "XAudio2_0.dll", "XAudio2"),
            new ClasseComDoDirectX("{c0c56f46-29b1-44e9-9939-a32ce86867e2}", "XAudio2_0.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{6f6ea3a9-2cf5-41cf-91c1-2170b1540063}", "XAudio2_0.dll", "AudioReverb"),
            new ClasseComDoDirectX("{e21a7345-eb21-468e-be50-804db97cf708}", "XAudio2_1.dll", "XAudio2"),
            new ClasseComDoDirectX("{c1e3f122-a2ea-442c-854f-20d98f8357a1}", "XAudio2_1.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{f4769300-b949-4df9-b333-00d33932e9a6}", "XAudio2_1.dll", "AudioReverb"),
            new ClasseComDoDirectX("{b802058a-464a-42db-bc10-b650d6f2586a}", "XAudio2_2.dll", "XAudio2"),
            new ClasseComDoDirectX("{f5ca7b34-8055-42c0-b836-216129eb7e30}", "XAudio2_2.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{629cf0de-3ecc-41e7-9926-f7e43eebec51}", "XAudio2_2.dll", "AudioReverb"),
            new ClasseComDoDirectX("{4c5e637a-16c7-4de3-9c46-5ed22181962d}", "XAudio2_3.dll", "XAudio2"),
            new ClasseComDoDirectX("{e180344b-ac83-4483-959e-18a5c56a5e19}", "XAudio2_3.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{9cab402c-1d37-44b4-886d-fa4f36170a4c}", "XAudio2_3.dll", "AudioReverb"),
            new ClasseComDoDirectX("{03219e78-5bc3-44d1-b92e-f63d89cc6526}", "XAudio2_4.dll", "XAudio2"),
            new ClasseComDoDirectX("{c7338b95-52b8-4542-aa79-42eb016c8c1c}", "XAudio2_4.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{8bb7778b-645b-4475-9a73-1de3170bd3af}", "XAudio2_4.dll", "AudioReverb"),
            new ClasseComDoDirectX("{4c9b6dde-6809-46e6-a278-9b6a97588670}", "XAudio2_5.dll", "XAudio2"),
            new ClasseComDoDirectX("{2139e6da-c341-4774-9ac3-b4e026347f64}", "XAudio2_5.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{d06df0d0-8518-441e-822f-5451d5c595b8}", "XAudio2_5.dll", "AudioReverb"),
            new ClasseComDoDirectX("{3eda9b49-2085-498b-9bb2-39a6778493de}", "XAudio2_6.dll", "XAudio2"),
            new ClasseComDoDirectX("{e48c5a3f-93ef-43bb-a092-2c7ceb946f27}", "XAudio2_6.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{cecec95a-d894-491a-bee3-5e106fb59f2d}", "XAudio2_6.dll", "AudioReverb"),
            new ClasseComDoDirectX("{5a508685-a254-4fba-9b82-9a24b00306af}", "XAudio2_7.dll", "XAudio2"),
            new ClasseComDoDirectX("{cac1105f-619b-4d04-831a-44e1cbf12d57}", "XAudio2_7.dll", "AudioVolumeMeter"),
            new ClasseComDoDirectX("{6a93130e-1d53-41d1-a9cf-e758800bb179}", "XAudio2_7.dll", "AudioReverb")
        };

        /// <summary>A visão do registro que um jogo desta arquitetura enxerga.</summary>
        public static RegistryView VisaoDe(ArquiteturaPe arquitetura)
            => arquitetura == ArquiteturaPe.X86 ? RegistryView.Registry32 : RegistryView.Registry64;

        /// <summary>
        /// Grava em HKCU as classes que o PC não tem, apontando para as DLLs de
        /// <paramref name="pastaDasDlls"/>. Devolve quantas gravou.
        /// </summary>
        /// <param name="sistemaTem">
        /// Pergunta "o Windows já resolve este CLSID nesta visão?". O padrão olha o HKLM; o
        /// teste troca por uma resposta fixa, porque o PC de desenvolvimento tem tudo instalado.
        /// </param>
        public static int Registrar(ArquiteturaPe arquitetura, string pastaDasDlls,
                                    IEnumerable<ClasseComDoDirectX>? classes = null,
                                    Func<string, RegistryView, bool>? sistemaTem = null)
        {
            var visao = VisaoDe(arquitetura);
            sistemaTem ??= SistemaTem;
            var gravadas = 0;

            using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, visao))
            {
                foreach (var classe in classes ?? ClassesDoPacote)
                {
                    var dll = Path.Combine(pastaDasDlls, classe.Dll);
                    if (!File.Exists(dll)) continue;
                    if (sistemaTem(classe.Clsid, visao)) continue;

                    var caminhoDaChave = RaizDasClasses + "\\" + classe.Clsid;

                    using (var existente = hkcu.OpenSubKey(caminhoDaChave))
                    {
                        // Alguém (outro programa, o próprio usuário) já registrou este CLSID
                        // no perfil. Não é meu, e não mexo.
                        if (existente != null && existente.GetValue(ValorDaMarca) is null) continue;
                    }

                    using (var chave = hkcu.CreateSubKey(caminhoDaChave))
                    using (var servidor = chave!.CreateSubKey("InprocServer32"))
                    {
                        chave.SetValue("", classe.Descricao);
                        chave.SetValue(ValorDaMarca, "1");
                        servidor!.SetValue("", dll);
                        servidor.SetValue("ThreadingModel", "Both");
                    }

                    gravadas++;
                }
            }

            return gravadas;
        }

        /// <summary>
        /// Apaga, nas duas visões, as chaves que eu gravei. Chave sem a marca fica. Nunca
        /// lança: é chamado no fim da sessão e na abertura do Mochila, e em nenhum dos dois
        /// lugares um registro teimoso vale uma caixa de erro.
        /// </summary>
        public static int Remover(IEnumerable<ClasseComDoDirectX>? classes = null)
        {
            var apagadas = 0;

            foreach (var visao in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, visao))
                    using (var raiz = hkcu.OpenSubKey(RaizDasClasses, writable: true))
                    {
                        if (raiz is null) continue;

                        foreach (var classe in classes ?? ClassesDoPacote)
                        {
                            try
                            {
                                bool minha;
                                using (var chave = raiz.OpenSubKey(classe.Clsid))
                                    minha = chave?.GetValue(ValorDaMarca) != null;

                                if (!minha) continue;

                                raiz.DeleteSubKeyTree(classe.Clsid, throwOnMissingSubKey: false);
                                apagadas++;
                            }
                            catch (Exception)
                            {
                                // Uma chave presa não impede as outras.
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Perfil sem acesso ao registro (política da máquina): nada a limpar.
                }
            }

            return apagadas;
        }

        /// <summary>
        /// O HKLM tem este CLSID apontando para uma DLL que existe? Classe registrada com o
        /// arquivo apagado conta como ausente: o jogo falharia do mesmo jeito.
        /// </summary>
        public static bool SistemaTem(string clsid, RegistryView visao)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, visao))
                using (var servidor = hklm.OpenSubKey(RaizDasClasses + "\\" + clsid + "\\InprocServer32"))
                {
                    if (servidor?.GetValue("") is not string caminho || caminho.Trim().Length == 0) return false;

                    var expandido = Environment.ExpandEnvironmentVariables(caminho.Trim().Trim('"'));

                    // Caminho só com o nome ("xactengine2_7.dll") é resolvido pelo Windows
                    // na pasta do sistema; aqui basta saber que alguém registrou.
                    return !Path.IsPathRooted(expandido) || File.Exists(expandido) || ExisteNaPastaDoSistema(expandido, visao);
                }
            }
            catch (Exception)
            {
                // Sem conseguir ler o HKLM, assumo que o PC tem: registrar por cima de um
                // DirectX instalado é o erro que a regra 1 existe para evitar.
                return true;
            }
        }

        /// <summary>
        /// Um processo de 64 bits que pergunta por <c>C:\Windows\SysWOW64\x.dll</c> enxerga o
        /// arquivo, mas um de 32 bits perguntando por <c>System32</c> cai no SysWOW64. O
        /// inverso também vale. Conferir o nome nas duas pastas tira o Mochila dessa dança.
        /// </summary>
        private static bool ExisteNaPastaDoSistema(string caminho, RegistryView visao)
        {
            var nome = Path.GetFileName(caminho);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            var pasta = visao == RegistryView.Registry32 && Environment.Is64BitOperatingSystem
                ? Path.Combine(windows, "SysWOW64")
                : Path.Combine(windows, Environment.Is64BitProcess || !Environment.Is64BitOperatingSystem ? "System32" : "Sysnative");

            return File.Exists(Path.Combine(pasta, nome));
        }
    }
}
