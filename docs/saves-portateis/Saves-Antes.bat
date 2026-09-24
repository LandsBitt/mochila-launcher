@echo off
rem ============================================================
rem  Saves portateis - HD -> PC, antes de abrir o jogo.
rem  Guia: https://github.com/LandsBitt/mochila-launcher/tree/main/docs/saves-portateis
rem
rem  1. Coloque este arquivo na pasta do jogo, ao lado do .exe.
rem  2. Troque a linha SAVES_PC pelo caminho do save do jogo
rem     (o mesmo que estiver no Saves-Depois.bat).
rem  3. Mochila: detalhes do jogo > Execucao... > Script antes.
rem
rem  Sem "pause": o Mochila espera no maximo 30 s por este script.
rem ============================================================
setlocal
set "SAVES_HD=%~dp0Saves_Portateis"

rem Exemplos:
rem   set "SAVES_PC=%LOCALAPPDATA%\Empresa\Jogo"
rem   set "SAVES_PC=%APPDATA%\Empresa\Jogo"
rem   set "SAVES_PC=%USERPROFILE%\AppData\LocalLow\Empresa\Jogo"   (jogos Unity)
set "SAVES_PC=%LOCALAPPDATA%\TROQUE-AQUI"

rem Save em Documentos? Apague a linha acima e tire o "rem" da linha abaixo:
rem for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "[Environment]::GetFolderPath('MyDocuments')"`) do set "SAVES_PC=%%D\My Games\Jogo"

rem Trava: sem caminho configurado, nao faz nada.
if not "%SAVES_PC:TROQUE-AQUI=%"=="%SAVES_PC%" goto :fim

if not exist "%SAVES_HD%" mkdir "%SAVES_HD%"
if not exist "%SAVES_PC%" mkdir "%SAVES_PC%"

xcopy "%SAVES_HD%\*" "%SAVES_PC%\" /E /I /Y /H /R /Q >nul

:fim
endlocal
