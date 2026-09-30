@echo off

setlocal

cd /d "%~dp0"



set "VSIX="



call "%~dp0scripts\ensure-icons.cmd" /force
if errorlevel 1 exit /b 1

echo [DotnetGraph] Restaurando pacotes...

dotnet restore DotnetGraph.sln

if errorlevel 1 exit /b 1



echo [DotnetGraph] Compilando Release (gera VSIX)...

dotnet build src\DotnetGraph.Extension\DotnetGraph.Extension.csproj -c Release --no-restore

if errorlevel 1 exit /b 1



set "VSIX=src\DotnetGraph.Extension\bin\Release\net472\DotnetGraph.Extension.vsix"

if exist "%VSIX%" goto :found



for /f "delims=" %%F in ('dir /s /b "src\DotnetGraph.Extension\bin\Release\*.vsix" 2^>nul') do (

  set "VSIX=%%F"

  goto :found

)



echo.

echo AVISO: build OK, mas nenhum .vsix foi encontrado.

echo Confira se a carga "Desenvolvimento de extensao do Visual Studio" esta instalada.

exit /b 1



:found

echo.

echo VSIX gerado em:

echo   %CD%\%VSIX%

echo.

echo Instale: duplo clique no .vsix ou Extensions ^> Manage Extensions ^> Install from a file

endlocal

