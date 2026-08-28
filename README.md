# 🎒 Mochila Launcher

Um launcher de jogos que vive no HD externo junto com os jogos — a mochila que você
carrega de PC em PC. Mostra o acervo numa grade de capas, acha sozinho o executável certo
de cada pasta e abre o jogo — depois sai da frente.

> *A portable game launcher that lives on your external drive. Single ~400 KB exe, no
> installer, no runtime, no registry, no background service. Portuguese-language project.*

![Licença](https://img.shields.io/badge/licen%C3%A7a-GPL--3.0-blue)
![Plataforma](https://img.shields.io/badge/Windows-10%20%7C%2011-lightgrey)
![Executável](https://img.shields.io/badge/exe-~400%20KB-brightgreen)
![Testes](https://img.shields.io/badge/testes-985-success)

<!-- TODO: screenshot da grade e um GIF abrindo um jogo. É a primeira coisa que olham.
     Gere sem ninguém na frente do PC:
       bin\Debug\Mochila.exe --grade-demo 40 --captura docs/grade.png
     e referencie:  ![Grade de capas](docs/grade.png) -->

---

## Por que existe

Um HD externo com jogos de 2003 a 2015 — coisa que não está na Steam e nem precisa estar.
O problema é sempre o mesmo: abrir a pasta, cavar entre `Uninstall\`, `redist\` e três
executáveis parecidos até achar qual abre o jogo. E fazer tudo de novo no notebook do
amigo, onde o HD virou outra letra.

Feito para PC fraco: um `.exe` de ~400 KB (110 KB disso é a arte do ícone), sem
instalação, sem serviço, sem processo residente. Enquanto o jogo roda, o launcher fica
escondido consumindo **0 ms de CPU** e cerca de **4 MB** de RAM.

**Portabilidade é a regra que manda em tudo.** Nada vai para o registro, para o AppData
ou para Documentos. Todo caminho salvo é relativo à pasta do launcher —

```
..\Jogos\Antigos\Carros\Need For Speed Most Wanted Black Edition\speed.exe
```

— então o HD pode ser `E:` num PC e `F:` em outro sem reconfigurar nada, e a pasta inteira
pode ser movida para qualquer lugar do disco.

## Como montar no HD

```
D:\Mochila\
    Mochila.exe
    _mochila\             <- criado sozinho no primeiro uso
        biblioteca.json       lista de jogos
        config.json           chave da API, preferências
        sessoes.json          histórico: o que você jogou, quando e por quanto tempo
        capas\                arte em tamanho cheio (capa, hero e logo)
        cache\                miniaturas prontas da grade
D:\Jogos\...              <- seus jogos, ao lado da pasta do launcher
```

1. Baixe o `Mochila.exe` na aba [Releases](../../releases) e copie para uma subpasta do HD
   (`D:\Mochila\`). Não precisa ser esse nome; só evite jogá-lo solto na raiz.
2. Abra e aperte **F6**. Escolha a pasta dos jogos (`D:\Jogos`, irmã da pasta do
   launcher, ou qualquer outra do mesmo HD).
3. Confira a lista proposta — linha amarela é palpite de baixa confiança, e dá para
   trocar o executável no combo da coluna do meio. Nada é gravado até você confirmar.
4. Clique em **Adicionar N jogo(s)**.

Requisito: Windows 10 ou 11. O .NET Framework 4.8 já vem com eles — não há runtime para
instalar.

> **Aviso do SmartScreen:** na primeira execução o Windows pode mostrar a tela azul de "O
> Windows protegeu o computador". É porque o exe não tem assinatura digital (custa algumas
> centenas de dólares por ano). Clique em *Mais informações* → *Executar assim mesmo*.

### Atalho na raiz do HD

Atalho `.lnk` guarda caminho absoluto e quebra quando a letra do drive muda. Use um `.bat`
de uma linha, salvo como `D:\Jogar.bat`:

```bat
@start "" "%~dp0Mochila\Mochila.exe"
```

O `%~dp0` é a pasta do próprio arquivo, então funciona em qualquer letra.

**Vindo de uma versão anterior?** O programa se chamava só "Launcher" e guardava tudo num
`_launcher\`. Na primeira abertura ele adota essa pasta como `_mochila\`: biblioteca,
capas, cache e tempo jogado seguem intactos, sem reescanear nada. O `Launcher.exe` velho
pode ser apagado. Se por algum motivo você já tiver as duas pastas no HD, a `_mochila\`
é a que vale e a antiga fica onde está, sem ser tocada.

## Teclas

| Tecla | O que faz |
|---|---|
| Setas | Navega pelos cards |
| Enter | Abre o jogo selecionado |
| **I** ou duplo clique | Abre a tela de detalhes do jogo |
| Esc | Fecha os detalhes; senão limpa a busca; com a busca vazia, fecha o launcher |
| F | Marca ou desmarca favorito (com o foco fora da busca) |
| R | Sorteia um jogo entre os que estão à vista e abre |
| **Shift + setas** | Marca vários cards seguidos |
| **Ctrl + clique** | Marca ou desmarca um card |
| **Ctrl + A** | Marca todos os que estão à vista (respeita o filtro) |
| F5 | Recarrega a biblioteca do disco |
| F6 | Escaneia as pastas de jogos |
| **F9** | Estatísticas do acervo (horas por mês, mapa de calor, top 10) |
| F10 | Configurações |
| Home / End | Primeiro / último jogo |
| PageUp / PageDown | Uma tela de cada vez |
| Digitar | Filtra a grade conforme você digita |

Com texto na busca, ← → movem o cursor no texto. Com a busca vazia, elas navegam na
grade.

**Duplo clique abre os detalhes, não o jogo.** Abrir um jogo salva a biblioteca, esconde a
janela e sobe um processo — é a ação cara, e ficou só com Enter, com o A do controle e com
o botão **Jogar** da tela de detalhes.

## Controle

Controle de Xbox (ou qualquer um que fale XInput) funciona sem configurar nada: plugue
com o launcher aberto e ele é encontrado em até 2 segundos.

| Botão | O que faz |
|---|---|
| D-pad / analógico esquerdo | Navega pelos cards |
| A | Abre o jogo selecionado |
| B | Fecha os detalhes; na grade, limpa a busca (com a busca vazia, não faz nada — quem fecha é o Esc) |
| X | Abre e fecha a tela de detalhes |
| Y | Marca ou desmarca favorito |
| LB / RB | Uma tela de cada vez |
| LT / RT | Troca a ordenação |
| Start | Configurações |

Segurar uma direção repete depois de 400 ms. Enquanto uma janela de configurações ou de
revisão está aberta, o controle fica desligado — volte para a janela principal e ele
responde de novo.

**Controle de PlayStation não aparece**: DualShock e DualSense falam HID puro, que o
XInput não enxerga. Use DS4Windows ou o Steam Input para traduzir, ou um controle de Xbox.

Com um jogo aberto, o launcher para de ler o controle junto com todo o resto — é a mesma
regra dos 0 ms de CPU.

## Tela de detalhes

**I**, duplo clique ou **X** no controle abrem a ficha do jogo: a arte em tamanho grande,
tempo total, última vez jogado, quanto você jogou dele **este mês**, as **últimas sessões**,
o caminho do executável, os argumentos de linha de comando (editáveis ali mesmo) e os botões
de capa. **Esc** ou **B** fecham; **← →** passam para o jogo anterior ou seguinte sem sair
da tela.

Não é uma janela nova — é um painel desenhado sobre a grade, e a arte grande é liberada da
memória no instante em que você fecha. Abrir e fechar cinquenta vezes deixa o consumo
exatamente onde estava (`--bench-detalhes` mede isso).

## Histórico e estatísticas

Toda sessão de mais de 5 segundos vira uma linha em `_mochila\sessoes.json` — jogo, quando
começou (em UTC) e quantos segundos durou. Sessão mais curta que isso não conta: é o caso
do jogo que abre um launcher próprio e morre na hora.

**F9** abre as estatísticas: jogado esta semana, horas por mês, mapa de calor do ano e os
dez mais jogados. As barras são desenhadas à mão — não há biblioteca de gráfico aqui, como
não há nenhuma outra dependência.

No topo da grade, a seção **Continuar jogando** traz os cinco jogos mais recentes. Ela se
esconde sozinha quando você ainda não jogou nada e enquanto há busca digitada (filtro é
pergunta com resposta exata, e dividir o resultado em duas seções esconderia metade dele).
Dá para desligá-la nas configurações.

Duas coisas que o histórico **não** faz, de propósito: ele não é lido quando o launcher
abre — só quando você pede detalhes ou estatísticas, então o tempo de abertura não muda
com cinco anos de registro — e ele nunca é compactado nem resumido. Duas sessões por dia
durante cinco anos dão uns 300 KB.

Se o arquivo ficar ilegível (queda de energia no meio de uma gravação), o launcher abre
normalmente com o histórico vazio, avisa na barra de baixo e guarda o arquivo problemático
com nome datado em vez de passar por cima dele. O tempo total de cada jogo não depende
disso: ele mora na biblioteca.

## Etiquetas, nota e status

Cada jogo pode ter tags (`corrida`, `indie`), uma nota de 0 a 5 e um status: **quero
jogar**, **jogando**, **zerado** ou **largado**. Tudo se edita clicando na tela de
detalhes — nas estrelas, nos chips de status e nas tags.

A barra de busca entende operadores, e eles se combinam:

| O que digitar | O que filtra |
|---|---|
| `#corrida` | Jogos com a tag corrida |
| `@zerado` | Jogos com esse status |
| `*4` | Nota 4 ou mais |
| `most wanted` | Texto no título (como sempre foi) |
| `#corrida @zerado *4` | Os três ao mesmo tempo |

Tag e status casam por prefixo, então `#cor` já filtra enquanto você digita, e acento não
importa (`#acao` acha `ação`). Abaixo da barra fica a **faixa de tags do acervo**, ordenada
por uso: clicar liga e desliga o `#tag` na busca.

## Ações em lote

Etiquetar duzentos jogos um a um não é opção. Marque vários com **Ctrl + clique**,
**Shift + setas** ou **Ctrl + A** (que respeita o filtro atual) e use o botão direito:
aplicar ou remover tag, definir nota, definir status, favoritar e remover da biblioteca.

A ação **só alcança o que está à vista**. Se o filtro é `#corrida`, nenhum jogo de fora
dele é tocado — e trocar o filtro não deixa marcação escondida para trás.

## Menu do card

O **botão direito** num card abre tudo que dá para fazer com aquele jogo: editar título e
argumentos, abrir a pasta dele no Explorer, criar um atalho na área de trabalho, etiquetar,
dar nota e status, fixar o executável contra rescan, trocar a capa (arquivo, online, área
de transferência, arte da pasta) e remover da biblioteca. Com vários cards marcados, o menu
some com o que só faz sentido para um jogo e mostra a contagem.

**Remover da biblioteca não apaga o jogo.** Some o card, a capa e o tempo jogado; nenhum
arquivo do jogo é tocado, e um F6 traz ele de volta.

O atalho na área de trabalho é a única coisa que o launcher grava fora da pasta dele, e
só sob confirmação. Ele guarda caminho absoluto — quando o HD mudar de letra, apague e
crie de novo.

## Adicionar jogo sem escanear

Arraste para dentro da janela:

| O que você arrasta | O que acontece |
|---|---|
| Uma imagem, em cima de um card | Vira a capa daquele jogo |
| `.exe`, `.lnk`, `.bat` ou `.cmd` | Cataloga como jogo novo, já fixado contra rescan |
| Uma pasta | Escaneia só ela e abre a janela de revisão |

Um `.lnk` é resolvido para o alvo antes de gravar — o que vai para a biblioteca é o
executável, nunca o atalho. Arquivo de outro drive é recusado com mensagem.

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

### Arte de fundo e logo

Na tela de detalhes (**I**), o botão **Arte de fundo...** baixa mais duas artes daquele
jogo: a **arte larga** (o "hero" do SteamGridDB), que vira o fundo desfocado da tela, e o
**logo** com transparência, que passa a aparecer no lugar do título.

Três coisas que valem saber:

- **É um jogo por vez, sob demanda** — não entra no lote de capas. Hero e logo são dois
  pedidos a mais por jogo; num acervo de 200, a dois pedidos por segundo, isso triplicaria
  um lote que já leva minutos para baixar enfeite de uma tela que você talvez nem abra.
- **Precisa da capa antes.** É a busca de capa que identifica o jogo no serviço. Sem essa
  identidade, o launcher estaria chutando — e arte de fundo de um jogo com capa de outro é
  pior que fundo nenhum.
- **Jogo antigo quase nunca tem.** O botão avisa e segue; não é erro.

O fundo não é desfocado a cada quadro: o hero é reduzido uma vez para 64 px de largura em
`_mochila\cache\<jogo>_hero_blur.jpg` e desenhado esticado. A **cor de acento** da tela
(a régua sob o título, o realce das estrelas e dos chips) sai da cor dominante daquela
arte, com um piso de contraste garantido — capa escura não faz o acento sumir. Trocar o
hero joga o borrão fora sozinho, e **Limpar cache de miniaturas** leva os borrões junto.

## Detalhes que costumam surpreender

**O executável certo.** Uma pasta de jogo antigo tem `unins000.exe`, `dxwebsetup.exe`,
`CrashHandler.exe` e o jogo. O scanner pontua cada candidato por nome, tamanho, pasta,
subsistema do PE e semelhança com o nome da pasta. Se ele errar, corrija no combo e
marque **"Não alterar em rescan"** (Ctrl+L): rescans nunca desfazem uma escolha sua.

**Reorganizei as pastas e não perdi nada.** Cada jogo guarda uma impressão digital do
executável dele (nome, tamanho e um hash dos primeiros 64 KB). Se você renomear ou mover
a pasta e apertar F6, o launcher reconhece que é o mesmo jogo e só corrige o caminho —
tempo jogado, capa e favorito continuam onde estavam. Quando ele não tem certeza (duas
cópias idênticas, ou o executável mudou), a janela de revisão oferece **Ctrl+R** para
religar com o jogo que sumiu. Nunca religa sozinho.

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
- **"SOMENTE LEITURA" no rodapé**: o `biblioteca.json` do HD foi gravado por uma versão
  mais nova do launcher. Ele abre para você olhar e jogar, mas **não grava nada** — nem
  tempo de sessão. Atualize o `Mochila.exe` e tudo volta ao normal. É proteção: regravar um
  arquivo que este binário não entende por inteiro apagaria o que a versão nova escreveu.
- **Trocou capas por fora**: F10 → **Limpar cache de miniaturas**.
- **Um jogo não abre**: quase sempre é o executável errado. F6, e corrija no combo.

## Para desenvolver

```powershell
dotnet build                     # Debug: inclui a suíte de testes e os benches
dotnet build -c Release          # o que vai para o HD

bin\Debug\Mochila.exe --autoteste                       # 985 testes das 14 fases
bin\Debug\Mochila.exe --bench-memoria 200 --ciclos 10   # RAM e handles da grade
bin\Debug\Mochila.exe --bench-detalhes 50               # RAM da tela de detalhes
bin\Debug\Mochila.exe --bench-detalhes 50 --com-hero    # idem, com hero e logo em todos
bin\Debug\Mochila.exe --bench-lancamento --ciclos 4     # RAM com jogo aberto
bin\Debug\Mochila.exe --grade-demo 60                   # acervo sintético
bin\Debug\Mochila.exe --grade-demo 40 --com-historico   # idem, com sessões inventadas
bin\Debug\Mochila.exe --escanear ..\Jogos               # scan sem gravar nada
bin\Debug\Mochila.exe --gerar-icone mochila.ico         # regera o ícone do exe
```

As telas também se fotografam sem ninguém na frente do PC (a janela é desenhada fora da
área visível, sem roubar o foco):

```powershell
bin\Debug\Mochila.exe --grade-demo 40 --captura grade.png
bin\Debug\Mochila.exe --grade-demo 40 --captura detalhes.png --detalhes
bin\Debug\Mochila.exe --grade-demo 40 --captura hero.png --detalhes --com-hero
bin\Debug\Mochila.exe --grade-demo 40 --captura lote.png --marcados
bin\Debug\Mochila.exe --grade-demo 40 --com-historico --captura estat.png --estatisticas
bin\Debug\Mochila.exe --grade-demo 20 --captura estreita.png --tamanho 700 460
```

O ícone é a arte de `assets\mochila.png`, que viaja **dentro** do exe como recurso
embutido — a regra de arquivo único vale para ele também. A janela monta os tamanhos que
precisa em memória (`src/UI/IconeDaMochila.cs`) e `--gerar-icone` grava o `mochila.ico`
de 16 a 256 px que o csproj embute no exe.

Para trocar a arte: substitua `assets\mochila-fonte.png` e rode

```powershell
python assets\preparar-icone.py assets\mochila-fonte.png assets\mochila.png 256
dotnet build -c Debug
bin\Debug\Mochila.exe --gerar-icone mochila.ico
dotnet build -c Release
```

O script tira o fundo branco, recorta no contorno e reduz — precisa de Pillow e numpy,
e é a única coisa no projeto que não é C#. Roda só quando a arte muda; o build não
depende dele.

Testes, benches e acervo sintético existem só em `#if DEBUG` — o Release não leva nada
disso. `--escanear` continua na build final por ser código de produção e ajudar a
investigar um scan estranho.

Sem NuGet no binário entregue, sem banco de dados, sem navegador embutido: C# e WinForms
sobre .NET Framework 4.8.

## Licença

**GPL-3.0.** Veja [LICENSE](LICENSE).

Em português claro:

- ✅ Pode usar, copiar, modificar e distribuir à vontade.
- ✅ Pode usar comercialmente. Se você vende HDs montados, presta serviço de configuração
  ou monta setups para clientes, pode incluir o Mochila numa boa.
- ⚠️ Se você distribuir uma versão modificada, tem que disponibilizar o código-fonte dela
  sob a mesma licença.
- ⚠️ Tem que manter o crédito de autoria.

### Nome e marca

O nome **"Mochila Launcher"** e o ícone **não** estão cobertos pela GPL. Forks e versões
modificadas devem usar outro nome, para que ninguém confunda o seu trabalho com o meu —
nem me atribua os bugs dele.

### Sobre jogos

O Mochila **não distribui, não baixa e não contém jogos**. Ele apenas lista e abre
executáveis que já estão no seu disco. O que você guarda no seu HD é responsabilidade sua.

As capas baixadas do SteamGridDB são conteúdo enviado por usuários da plataforma e sujeito
a direitos de terceiros — por isso a pasta `_mochila/` está no `.gitignore` e nenhuma
imagem acompanha este repositório.
