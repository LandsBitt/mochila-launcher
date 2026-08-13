# Launcher portátil de jogos

Um launcher de jogos que vive no HD externo junto com os jogos. Mostra o acervo numa
grade de capas, acha sozinho o executável certo de cada pasta e abre o jogo — depois sai
da frente.

Feito para PC fraco: um `.exe` de ~250 KB (30 KB disso é o ícone), sem instalação, sem
serviço, sem processo residente. Enquanto o jogo roda, o launcher fica escondido consumindo **0 ms de CPU** e
cerca de **4 MB** de RAM.

**Portabilidade é a regra que manda em tudo.** Nada vai para o registro, para o AppData
ou para Documentos. Todo caminho salvo é relativo à pasta do launcher, então o HD pode
ser `E:` num PC e `F:` em outro sem reconfigurar nada — e a pasta inteira pode ser movida
para qualquer lugar do disco.

## Como montar no HD

```
D:\Launcher\
    Launcher.exe
    _launcher\            <- criado sozinho no primeiro uso
        biblioteca.json       lista de jogos
        config.json           chave da API, preferências
        capas\                arte em tamanho cheio
        cache\                miniaturas prontas da grade
D:\Jogos\...              <- seus jogos, ao lado da pasta do launcher
```

1. Copie o `Launcher.exe` para uma subpasta do HD (`D:\Launcher\`). Não precisa ser esse
   nome; só evite jogá-lo solto na raiz.
2. Abra e aperte **F6**. Escolha a pasta dos jogos (`D:\Jogos`, irmã da pasta do
   launcher, ou qualquer outra do mesmo HD).
3. Confira a lista proposta — linha amarela é palpite de baixa confiança, e dá para
   trocar o executável no combo da coluna do meio. Nada é gravado até você confirmar.
4. Clique em **Adicionar N jogo(s)**.

Requisito: Windows 10 ou 11. O .NET Framework 4.8 já vem com eles — não há runtime para
instalar.

## Teclas

| Tecla | O que faz |
|---|---|
| Setas | Navega pelos cards |
| Enter | Abre o jogo selecionado |
| Esc | Limpa a busca; com a busca vazia, fecha o launcher |
| F | Marca ou desmarca favorito (com o foco fora da busca) |
| F5 | Recarrega a biblioteca do disco |
| F6 | Escaneia as pastas de jogos |
| F10 | Configurações |
| Home / End | Primeiro / último jogo |
| PageUp / PageDown | Uma tela de cada vez |
| Digitar | Filtra a grade conforme você digita |

Com texto na busca, ← → movem o cursor no texto. Com a busca vazia, elas navegam na
grade.

No card, o **botão direito** abre o menu de capas: escolher do arquivo, buscar online,
colar da área de transferência, usar a arte da pasta do jogo, remover. **Arrastar uma
imagem** para cima de um card também define a capa dele.

## Capas

Sem nenhuma configuração, o launcher já cobre o acervo: procura arte solta na pasta do
jogo (`cover.jpg`, `NFSU_icon.ico` e afins), senão extrai o ícone do executável, senão
desenha um card com o título sobre uma cor derivada do nome. **Nenhum card fica vazio.**

Para baixar capas de verdade, informe uma chave do [SteamGridDB](https://www.steamgriddb.com/profile/preferences/api)
em **F10 → Chave do SteamGridDB**. Sem chave, o launcher não faz nenhuma conexão de rede.
A chave viaja só no cabeçalho `Authorization`, nunca em URL, log ou mensagem de erro, e
aparece mascarada na tela.

Com a chave configurada, o menu do card ganha **Baixar capas que faltam...** — um lote
com freio de dois pedidos por segundo, cancelável, que não roda com jogo aberto.

## Detalhes que costumam surpreender

**O executável certo.** Uma pasta de jogo antigo tem `unins000.exe`, `dxwebsetup.exe`,
`CrashHandler.exe` e o jogo. O scanner pontua cada candidato por nome, tamanho, pasta,
subsistema do PE e semelhança com o nome da pasta. Se ele errar, corrija no combo e
marque **"Não alterar em rescan"** (Ctrl+L): rescans nunca desfazem uma escolha sua.

**Jogo com launcher próprio.** Quando o `.exe` que você abriu morre em segundos e deixa
outro processo rodando, o launcher procura esse processo dentro da pasta do jogo, se
prende a ele e continua escondido — em vez de reaparecer por cima de um jogo em tela
cheia. Se não achar ninguém, volta sem roubar o foco.

**Plataformas.** Steam, Epic, Riot, Battle.net, Ubisoft, EA/Origin e GOG Galaxy são
ignoradas no scan: dependem de instalação e login, não rodam de HD externo. A lista está
em `config.json` e é editável. Isso não afeta jogo indie com launcher próprio, que
continua sendo catalogado.

**Tempo jogado** é contado em segundos e exibido em minutos e horas. Sessão de menos de
5 segundos não conta — quase sempre é um launcher passando o bastão.

## Se algo der errado

- **Biblioteca corrompida** (queda de energia no meio de uma gravação): o launcher abre
  com uma biblioteca vazia e guarda o arquivo problemático como
  `biblioteca.json.corrompido-<data>`. Nada é sobrescrito; um F6 recataloga tudo.
- **HD desconectado com o launcher aberto**: os cards viram "não encontrado" e as
  gravações avisam em vez de quebrar. Reconecte e aperte F5.
- **Trocou capas por fora**: F10 → **Limpar cache de miniaturas**.
- **Um jogo não abre**: quase sempre é o executável errado. F6, e corrija no combo.

## Para desenvolver

```powershell
dotnet build                     # Debug: inclui a suíte de testes e os benches
dotnet build -c Release          # o que vai para o HD

bin\Debug\Launcher.exe --autoteste                       # 550 testes das 7 fases
bin\Debug\Launcher.exe --bench-memoria 200 --ciclos 10   # RAM e handles da grade
bin\Debug\Launcher.exe --bench-lancamento --ciclos 4     # RAM com jogo aberto
bin\Debug\Launcher.exe --grade-demo 60                   # acervo sintético
bin\Debug\Launcher.exe --escanear ..\Jogos               # scan sem gravar nada
bin\Debug\Launcher.exe --gerar-icone launcher.ico        # regera o ícone do exe
```

O gamepad do ícone é desenhado por código (`src/UI/IconeDoLauncher.cs`), não é um arquivo
de arte: a janela monta o dele em memória e `--gerar-icone` grava o `launcher.ico` que o
csproj embute no exe. Mexeu no desenho, rode o comando e recompile.

Testes, benches e acervo sintético existem só em `#if DEBUG` — o Release não leva nada
disso. `--escanear` continua na build final por ser código de produção e ajudar a
investigar um scan estranho.

Sem NuGet no binário entregue, sem banco de dados, sem navegador embutido: C# e WinForms
sobre .NET Framework 4.8.
