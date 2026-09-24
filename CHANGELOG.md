# Changelog

Todas as mudanças relevantes deste projeto ficam registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/), e a numeração
segue o [Semantic Versioning](https://semver.org/lang/pt-BR/). Enquanto a versão for `0.x`, a
API interna e o formato dos arquivos ainda podem mudar entre versões menores — o que **não**
muda é a promessa de nunca perder dado do `biblioteca.json` na migração.

## [Não lançado]

### Adicionado

- **Guia de saves portáteis** em `docs/saves-portateis/`: como levar o progresso de jogos que
  salvam no perfil do Windows usando os scripts de antes e depois, com modelos
  `Saves-Antes.bat` / `Saves-Depois.bat` prontos (com trava: sem editar o caminho, não fazem
  nada) e um prompt para colar numa IA que monta os scripts. O README ganhou uma pergunta no
  FAQ e um link na seção de opções de execução.
- `.gitattributes` guarda `.bat`/`.cmd` com CRLF, para o arquivo baixado direto do GitHub
  rodar certo no `cmd.exe`.

## [0.3.0-beta.1] — 2026-09-17

Primeira versão pública, marcada como beta.

### Adicionado

- **Ajuda para conseguir a chave do SteamGridDB.** O F10 ganhou o link "Como conseguir uma
  chave (é grátis)", que mostra o passo a passo e oferece abrir a página de API no
  navegador. O aviso de "sem chave" e a dica do menu do card passam a apontar para o F10, e
  o README tem a mesma explicação.
- **Opções de execução por jogo** (fase 15), na tela de detalhes: prioridade do processo
  (`normal` | `acima` | `alta`) e os scripts `.bat`/`.cmd` de **antes** e **depois**. O
  "antes" roda síncrono com teto de 30 s; o "depois" é disparado e esquecido. Os dois rodam
  com `WorkingDirectory` na pasta do jogo, e só aceitam script dentro do HD do launcher.
  Quando o jogo sai do padrão, a ficha ganha uma linha **Execução**.
- **Tema configurável** (fase 17): dois presets (escuro, que continua sendo o padrão, e
  claro) e uma cor de acento em `#RRGGBB`, ambos no `config.json` e editáveis em F10. Vale
  a partir da próxima abertura, e o launcher se oferece para reabrir.
- **Relatório de integridade do acervo** (fase 17), em **F8**: conta jogos não encontrados,
  artes quebradas e pastas novas desde o último scan, numa thread e com botão de cancelar.
  Cada item tem ação direta — inclusive o caminho para a religação da fase 10.

### Alterado

- **Identidade visual nova.** A mochila roxa deu lugar a uma mochila verde-lima sobre placa
  grafite, com um controle no bolso. A arte agora sai de código (`assets\gerar-icone.py`),
  que grava também o PNG de 1024 px e a imagem de preview do GitHub; o
  `preparar-icone.py`, que limpava o fundo branco da arte antiga, saiu.
- **O acento do tema acompanha o ícone.** O azul de fábrica virou o verde-lima da mochila
  (`#A8FF3E`) no tema escuro e um verde escurecido (`#2F7D12`) no claro, onde o lima não
  passaria do piso de contraste. Quem nunca escolheu cor no F10 ganha o verde sozinho.
- O piso de contraste do acento (fase 14) passa a escolher a direção pelo fundo. Ele só
  sabia clarear, porque só existia tema escuro; sobre o fundo claro isso nunca alcançaria o
  piso, e o acento tirado da arte sumiria da tela inteira sem nada explicando por quê.
- A cor do controle marcado (chip de tag ativa, item de menu sob o cursor, opção destacada
  no combo) virou cor de tema. Era um literal repetido em seis lugares, todos calibrados
  para o fundo escuro.

### Corrigido

- **A tela de configurações escondia metade das próprias opções.** No WinForms, irmãos
  ancorados são posicionados do último filho para o primeiro; a lista de pastas é
  `Dock.Fill` e entrava antes dos blocos de baixo, engolindo a área restante. "Grade" (com
  o tamanho do card, a seção "Continuar jogando" e o botão das estatísticas) e "Limpar
  cache de miniaturas" eram desenhados com altura zero — a janela abria bonita e sem eles.
  Bug desde a fase 7, e invisível para teste de comportamento: só apareceu ao fotografar a
  tela. Tem teste de geometria agora.
- O botão "Estatísticas do acervo" das configurações não abria nada: fechar sem salvar caía
  num `return` que pulava o trecho encarregado de abrir a tela depois.

### Notas

- A **fase 16** (saves portáteis) continua no planejamento interno, e a **fase 18** (Big Picture /
  TV) segue por fazer.
- O **som de navegação**, terceiro item da fase 17, ficou de fora desta entrega a pedido.
  Nada foi preparado nem estubado para ele.
- `--autoteste`: **1124 testes, todos verdes**.
- Build Release: `Mochila.exe` com 444 KB, `AnyCPU`, net48, zero NuGet em runtime.

## [0.2.0] — 2026-08-24

O launcher deixa de ser um catalogador e vira launcher: identidade, ritual de uso e memória
do acervo. São as fases 8 a 13 do planejamento interno entregues sobre a base das fases 1 a 7.

### Adicionado

- **Gamepad nativo por XInput** (fase 8), sem NuGet e sem processo residente. O timer de
  polling morre enquanto um jogo está aberto — o launcher dorme de verdade.
- **Camada de comandos** (fase 9): teclado e controle passam a produzir o mesmo
  `ComandoDeNavegacao`, resolvido por contexto num roteador único.
- **Ações do card** (fase 10): menu de contexto, adicionar jogo manualmente, atalho na área
  de trabalho, religação de jogo ausente e o botão surpresa.
- **Impressão digital do executável** (fase 10): FNV-1a de 64 bits sobre
  `nome|tamanho|primeiros 64 KB`, que reconhece jogo movido ou de pasta renomeada sem
  precisar de religação manual.
- **Tela de detalhes** (fase 11), com arte, histórico de sessões, nota, status e tags.
- **`biblioteca.json` versão 3** (fase 12), com migração encadeada 1 → 2 → 3 sem perder
  campo, e um **saco de sobras** que preserva campos gravados por versões mais novas.
- **Busca com operadores** e **ações em lote** sobre multi-seleção (fase 12).
- **Log de sessões e estatísticas** (fase 13), em `sessoes.json` separado da biblioteca, com
  a seção "Continuar jogando" na grade.
- **Tags, nota (0–5) e status** por jogo (quero jogar, jogando, zerado, largado).

### Alterado

- A pasta de estado passou de `_launcher\` para `_mochila\`. A pasta antiga continua sendo
  lida e migrada; nada é apagado.
- **Arquivo do futuro**: um `biblioteca.json` com `versao` maior que a que este binário
  entende abre em **somente leitura**, avisa no rodapé e não grava nada — inclusive não
  contabiliza tempo de sessão. Antes, ele seria regravado por cima, destruindo os campos
  desconhecidos.
- A barra superior quebra em duas linhas quando a janela estreita, em vez de sobrepor os
  controles. No tamanho padrão, o botão "Surpresa" era desenhado por baixo do seletor de
  tamanho do card e ficava impossível de clicar.

### Corrigido

- O `id` do jogo passa a ser validado e saneado contra o conjunto `^[a-z0-9_-]+$`. Ele é
  nome de arquivo em `<id>_thumb.jpg` e na capa, e um `:` vindo de arquivo editado à mão
  virava *alternate data stream* no Windows, com falha estranha em vez de erro claro.
- `Biblioteca.Validar` passa a conferir também os caminhos gravados **fora** de
  `executavelRelativo`. Um caminho absoluto em qualquer outro campo passava batido, quebrando
  em silêncio a regra de portabilidade do projeto.

### Removido

- **Fase 16 — saves portáteis** (redirecionamento por junction e backup de saves) saiu desta
  entrega e voltou para o planejamento interno, com o código já escrito removido do repositório. Ela é
  a única fase que abre exceção à regra de não escrever fora da pasta do launcher e a única
  com estado a recuperar depois de queda; estava segurando as fases 17 e 18. Nada foi
  revogado — a especificação inteira, com as quatro revisões que ela custou, está preservada
  na v3.

### Notas

- As fases **14** (hero art e logo) e **15** (opções de execução por jogo) **não estão
  implementadas**. Os campos delas existem no schema v3 porque a fase 12 fez um bump único e
  de propósito, mas não há tela nem comportamento associado ainda.
- `--autoteste`: **920 testes, todos verdes**.
- Build Release: `Mochila.exe` com 430 KB, `AnyCPU`, net48, zero NuGet em runtime e nenhuma
  DLL ao lado do executável.

## [0.1.0] — 2026-08-13

Marcada no histórico como `V1.01: Estilização`.

### Adicionado

- Fases 1 a 7 do planejamento interno: caminhos relativos e JSON próprio, scanner de executáveis com
  pontuação, janela de revisão do scan, grade de capas `OwnerDraw` virtualizada, lançamento
  com `WorkingDirectory` correto e adoção de processo-filho, capas via SteamGridDB com
  fallback local, e a suíte de robustez.
- Tema escuro e o ícone da mochila embutido no executável como recurso.

<!-- Os links de comparação entre versões entram aqui quando o repositório ganhar um remote.
     O formato é o de sempre:
       [Não lançado]: https://github.com/<usuario>/<repo>/compare/v0.2.0...HEAD
       [0.2.0]: https://github.com/<usuario>/<repo>/releases/tag/v0.2.0
       [0.1.0]: https://github.com/<usuario>/<repo>/releases/tag/v0.1.0 -->
