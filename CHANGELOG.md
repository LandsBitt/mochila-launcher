# Changelog

Todas as mudanças relevantes deste projeto ficam registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/), e a numeração
segue o [Semantic Versioning](https://semver.org/lang/pt-BR/). Enquanto a versão for `0.x`, a
API interna e o formato dos arquivos ainda podem mudar entre versões menores — o que **não**
muda é a promessa de nunca perder dado do `biblioteca.json` na migração.

## [Não lançado]

- Fase 17 — tema configurável, som de navegação e relatório de integridade do acervo.
- Fase 18 — modo Big Picture / TV.

## [0.2.0] — 2026-08-24

O launcher deixa de ser um catalogador e vira launcher: identidade, ritual de uso e memória
do acervo. São as fases 8 a 13 do `ESPEC-v2.md` entregues sobre a base das fases 1 a 7.

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
  entrega e foi para o `ESPEC-v3.md`, com o código já escrito removido do repositório. Ela é
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

- Fases 1 a 7 do `ESPEC.md`: caminhos relativos e JSON próprio, scanner de executáveis com
  pontuação, janela de revisão do scan, grade de capas `OwnerDraw` virtualizada, lançamento
  com `WorkingDirectory` correto e adoção de processo-filho, capas via SteamGridDB com
  fallback local, e a suíte de robustez.
- Tema escuro e o ícone da mochila embutido no executável como recurso.

<!-- Os links de comparação entre versões entram aqui quando o repositório ganhar um remote.
     O formato é o de sempre:
       [Não lançado]: https://github.com/<usuario>/<repo>/compare/v0.2.0...HEAD
       [0.2.0]: https://github.com/<usuario>/<repo>/releases/tag/v0.2.0
       [0.1.0]: https://github.com/<usuario>/<repo>/releases/tag/v0.1.0 -->
