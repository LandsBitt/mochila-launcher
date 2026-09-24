@echo off
rem ============================================================
rem  Saves portateis - PC -> HD, depois que o jogo fecha.
rem  Guia: https://github.com/LandsBitt/mochila-launcher/tree/main/docs/saves-portateis
rem
rem  1. Coloque este arquivo na pasta do jogo, ao lado do .exe.
rem  2. Troque a linha SAVES_PC pelo MESMO caminho do Saves-Antes.bat.
rem  3. Mochila: detalhes do jogo > Execucao... > Script depois.
rem ============================================================
setlocal
set "SAVES_HD=%~dp0Saves_Portateis"

set "SAVES_PC=%LOCALAPPDATA%\TROQUE-AQUI"

rem Save em Documentos? Apague a linha acima e tire o "rem" da linha abaixo:
rem for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "[Environment]::GetFolderPath('MyDocuments')"`) do set "SAVES_PC=%%D\My Games\Jogo"

rem Trava: sem caminho configurado, nao faz nada.
if not "%SAVES_PC:TROQUE-AQUI=%"=="%SAVES_PC%" goto :fim

if not exist "%SAVES_PC%" goto :fim
if not exist "%SAVES_HD%" mkdir "%SAVES_HD%"

xcopy "%SAVES_PC%\*" "%SAVES_HD%\" /E /I /Y /H /R /Q >nul
if errorlevel 4 msg "%USERNAME%" "Nao consegui copiar os saves para o HD. Eles continuam em %SAVES_PC%"

:fim
endlocal
