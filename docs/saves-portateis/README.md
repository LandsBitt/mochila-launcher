# Saves portáteis: seu progresso junto com o HD

O Mochila leva os jogos para qualquer PC, mas muitos jogos não guardam o progresso na
própria pasta: gravam no perfil do Windows (`AppData`, `Documentos`...), que fica no PC.
Você joga no notebook do amigo, volta para casa e o save ficou lá.

Este guia resolve isso com dois scripts pequenos por jogo, rodados pelo próprio Mochila.
Não precisa instalar nada.

**Neste guia:** [Como funciona](#como-funciona) · [O jogo precisa disso?](#o-jogo-precisa-disso) ·
[Passo a passo](#passo-a-passo) · [Descobrir onde o jogo salva](#descobrir-onde-o-jogo-salva) ·
[Cuidados](#cuidados) · [Pedir ajuda a uma IA](prompt-para-ia.md)

---

## Como funciona

```
Você abre o jogo no Mochila
  ├─ Saves-Antes.bat    copia  HD  ──►  PC    seus saves chegam no PC
  ├─ o jogo abre, você joga, ele grava no PC normalmente
  ├─ o jogo fecha
  └─ Saves-Depois.bat   copia  PC  ──►  HD    seus saves voltam para o HD
```

A pasta de cada jogo fica assim:

```
D:\Jogos\Meu Jogo\
├── MeuJogo.exe
├── Saves-Antes.bat        ← HD -> PC
├── Saves-Depois.bat       ← PC -> HD
└── Saves_Portateis\       ← seu progresso mora aqui
```

Os scripts usam caminhos relativos e variáveis do Windows (`%LOCALAPPDATA%`...), então
funcionam com qualquer letra de HD e qualquer usuário do Windows.

## O jogo precisa disso?

| Situação | Como saber | O que fazer |
|---|---|---|
| Jogo **antigo** (antes de ~2007) | Tem uma pasta `SAVE`, `SAVES`, `SaveGames` ou `Profiles` **dentro** da pasta do jogo, com arquivos | Já é portátil. Não precisa de script. |
| Jogo **mais novo** | Não tem save dentro da pasta | Grava no perfil do Windows. **Use este guia.** |

O motivo: a partir do Windows Vista os jogos passaram a gravar no perfil do usuário para
rodar sem permissão de administrador. Por isso o caminho do save fica fixo dentro do jogo.

## Passo a passo

1. **Descubra onde o jogo salva** (veja a [próxima seção](#descobrir-onde-o-jogo-salva)).
2. **Baixe os modelos** [`Saves-Antes.bat`](Saves-Antes.bat) e
   [`Saves-Depois.bat`](Saves-Depois.bat) e coloque os dois na pasta do jogo, ao lado do
   `.exe`.
3. **Edite os dois** no Bloco de Notas: troque o nome do jogo e a linha `SAVES_PC` pelo
   caminho do save. Os dois arquivos precisam ter **o mesmo** `SAVES_PC`.
4. **Ligue no Mochila:** abra os detalhes do jogo (**I** ou duplo clique) →
   **Execução...** → **Script antes:** `Saves-Antes.bat` · **Script depois:**
   `Saves-Depois.bat`.
5. **Confira:** jogue até o jogo salvar, feche, e abra a pasta `Saves_Portateis`. Se
   apareceram arquivos, está funcionando. Se ficou vazia, o caminho do save está errado.

> [!TIP]
> Sem o Mochila também funciona: rode `Saves-Antes.bat`, jogue, feche o jogo e rode
> `Saves-Depois.bat`.

## Descobrir onde o jogo salva

Pesquise `nome do jogo save game location`. As melhores fontes:

- [PCGamingWiki](https://www.pcgamingwiki.com): cada jogo tem uma seção
  *Save game data location*.
- [Manifesto do Ludusavi](https://github.com/mtkennerly/ludusavi-manifest): banco de
  caminhos de save mantido pela comunidade.

Atenção: a versão **GOG** pode salvar num lugar diferente da versão **Steam**. Caminhos
com um número de conta Steam no meio (`<Steam-folder>\userdata\...`) valem só para a Steam.

Pistas dentro da pasta do jogo:

| Se a pasta do jogo tem... | O save costuma ficar em |
|---|---|
| `NomeDoJogo_Data\` e `UnityPlayer.dll` (**Unity**) | `%USERPROFILE%\AppData\LocalLow\<Empresa>\<Jogo>`. Empresa e jogo estão escritos no arquivo `NomeDoJogo_Data\app.info`. |
| `Engine\` e um `.exe` em `Binaries\Win64\` (**Unreal**) | `%LOCALAPPDATA%\<NomeDoProjeto>\Saved\SaveGames` |
| `goggame-<número>.script` (**GOG**) | Às vezes o arquivo diz: procure por `savePath` |

Para confirmar de vez: jogue uma vez, salve, e abra o caminho no **Win + R**. Tem que ter
arquivos com a data de hoje.

### Escrevendo o caminho no script

Use sempre as variáveis do Windows, nunca o seu nome de usuário. `C:\Users\joao\...` não
existe no PC do amigo.

| O caminho real é... | Escreva no script |
|---|---|
| `C:\Users\<você>\AppData\Local\...` | `%LOCALAPPDATA%\...` |
| `C:\Users\<você>\AppData\Roaming\...` | `%APPDATA%\...` |
| `C:\Users\<você>\AppData\LocalLow\...` | `%USERPROFILE%\AppData\LocalLow\...` |
| `C:\Users\<você>\Documents\...` | Use a linha alternativa que está comentada no modelo. A pasta Documentos pode estar no OneDrive, e essa linha pergunta ao Windows onde ela está. |
| `C:\Users\Public\Documents\...` | `%PUBLIC%\Documents\...` |

> [!WARNING]
> Alguns jogos gravam o progresso no **registro do Windows**, e não em arquivos. Nesse
> caso os modelos não servem. A PCGamingWiki informa quando é assim (`HKEY_CURRENT_USER\...`).

## Cuidados

- **Ao abrir o jogo, o save do HD substitui o do PC.** Se o PC já tinha progresso próprio
  daquele jogo, esse progresso é sobrescrito. Na dúvida, faça uma cópia da pasta do PC antes
  da primeira vez.
- **Não tire o HD com o jogo aberto.** Espere o jogo fechar. O `Saves-Depois.bat` leva
  menos de um segundo.
- **Não coloque `pause` nos scripts.** O Mochila espera no máximo 30 segundos pelo
  `Saves-Antes.bat`, e um `pause` faria ele estourar esse tempo.
- **Vai passar a pasta do jogo para alguém?** Esvazie a `Saves_Portateis` antes, senão o
  seu progresso vai junto.

## Pedir ajuda a uma IA

Não quer pesquisar e editar os scripts na mão? Em [prompt-para-ia.md](prompt-para-ia.md)
tem um texto pronto para colar no ChatGPT, Gemini, Claude ou outra IA. Ela faz as
perguntas certas e devolve os dois scripts prontos.
