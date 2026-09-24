# Prompt para a sua IA montar os scripts

Copie o bloco abaixo inteiro e cole no ChatGPT, Gemini, Claude ou outra IA. Ela vai
perguntar o nome do jogo e onde ele está instalado, e devolver os dois scripts prontos.

> [!IMPORTANT]
> A IA pode errar o caminho do save. Depois de jogar a primeira vez, confira se a pasta
> `Saves_Portateis` recebeu arquivos. Se ficou vazia, conte isso para a IA e peça outro
> caminho.

Se a sua IA consegue pesquisar na internet, ative a pesquisa: o resultado fica bem melhor.

---

```text
Quero levar os saves de um jogo de PC no meu HD externo. Uso o Mochila Launcher
(https://github.com/LandsBitt/mochila-launcher), que roda um script .bat antes de abrir o
jogo e outro depois que o jogo fecha. Preciso que você monte esses dois scripts.

PASSO 1 - Me pergunte, numa mensagem só, e espere eu responder:
- Nome do jogo e a loja onde comprei (GOG, Steam, Epic, outra).
- Em que pasta do HD ele está instalado.
- (Opcional) A lista de arquivos da pasta do jogo. Diga que eu consigo gerar abrindo o
  Prompt de Comando na pasta do jogo e rodando: dir /b
- (Opcional) Se o jogo já foi aberto neste PC.

PASSO 2 - Descubra onde o jogo grava o save no Windows:
- Pesquise (PCGamingWiki, manifesto do Ludusavi em github.com/mtkennerly/ludusavi-manifest,
  Steam Community). Se você não tem acesso à internet, diga isso claramente.
- Use a lista de arquivos como pista: Unity (pasta NomeDoJogo_Data e UnityPlayer.dll)
  grava em %USERPROFILE%\AppData\LocalLow\<Empresa>\<Jogo>, e a empresa e o jogo estão no
  arquivo NomeDoJogo_Data\app.info (peça para eu abrir e te mandar o conteúdo). Unreal
  costuma gravar em %LOCALAPPDATA%\<Projeto>\Saved\SaveGames.
- Considere a versão da MINHA loja: GOG e Steam podem salvar em lugares diferentes.
- Se o jogo for antigo e salvar dentro da própria pasta (SAVE, SAVES, Profiles...), me
  diga que ele já é portátil e que não precisa de script.
- Se o jogo salvar no registro do Windows, me avise, porque aí os scripts são outros.
- Me diga de onde tirou o caminho e o quanto tem certeza dele.

PASSO 3 - Me entregue os dois arquivos, seguindo EXATAMENTE este modelo e trocando só o
nome do jogo e o SAVES_PC:

--- Saves-Antes.bat ---
@echo off
rem <NOME DO JOGO> - HD -> PC, antes de abrir o jogo.
rem Mochila: detalhes > Execucao... > Script antes. Sem pause (limite de 30 s).
setlocal
set "SAVES_HD=%~dp0Saves_Portateis"
set "SAVES_PC=<CAMINHO>"
if not exist "%SAVES_HD%" mkdir "%SAVES_HD%"
if not exist "%SAVES_PC%" mkdir "%SAVES_PC%"
xcopy "%SAVES_HD%\*" "%SAVES_PC%\" /E /I /Y /H /R /Q >nul
endlocal

--- Saves-Depois.bat ---
@echo off
rem <NOME DO JOGO> - PC -> HD, depois que o jogo fecha.
rem Mochila: detalhes > Execucao... > Script depois.
setlocal
set "SAVES_HD=%~dp0Saves_Portateis"
set "SAVES_PC=<CAMINHO>"
if not exist "%SAVES_PC%" goto :fim
if not exist "%SAVES_HD%" mkdir "%SAVES_HD%"
xcopy "%SAVES_PC%\*" "%SAVES_HD%\" /E /I /Y /H /R /Q >nul
if errorlevel 4 msg "%USERNAME%" "<NOME DO JOGO>: nao consegui copiar os saves para o HD."
:fim
endlocal

Regras para o <CAMINHO>:
- Nunca use C:\Users\<nome>. Use %LOCALAPPDATA%, %APPDATA%,
  %USERPROFILE%\AppData\LocalLow ou %PUBLIC%.
- Se o save estiver em Documentos, troque a linha set "SAVES_PC=..." por:
  for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "[Environment]::GetFolderPath('MyDocuments')"`) do set "SAVES_PC=%%D\<resto do caminho>"
- Os dois arquivos têm que ter o mesmo SAVES_PC.
- Sem acentos dentro dos .bat, sem pause, sem nada que peça para eu apertar tecla.

PASSO 4 - Me explique em poucas linhas:
- Salvar os dois arquivos na pasta do jogo, ao lado do .exe. No Bloco de Notas, usar
  "Salvar como", "Todos os arquivos" e codificação ANSI.
- No Mochila: detalhes do jogo > Execução... > Script antes = Saves-Antes.bat, Script
  depois = Saves-Depois.bat.
- Jogar até salvar, fechar e conferir se a pasta Saves_Portateis recebeu arquivos.
- Cuidado: ao abrir o jogo, o save do HD substitui o do PC.
```
