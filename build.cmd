@echo off

setlocal

cd /d "%~dp0"



set "VSIX="



call "%~dp0scripts\ensure-icons.cmd" /force
if errorlevel 1 exit /b 1

echo [DotnetGraph] Restoring packages...

dotnet restore DotnetGraph.sln

if errorlevel 1 exit /b 1



echo [DotnetGraph] Building Release (produces VSIX)...

dotnet build src\DotnetGraph.Extension\DotnetGraph.Extension.csproj -c Release --no-restore

if errorlevel 1 exit /b 1



set "VSIX=src\DotnetGraph.Extension\bin\Release\net472\DotnetGraph.Extension.vsix"

if exist "%VSIX%" goto :found



for /f "delims=" %%F in ('dir /s /b "src\DotnetGraph.Extension\bin\Release\*.vsix" 2^>nul') do (

  set "VSIX=%%F"

  goto :found

)



echo.

echo WARNING: build succeeded, but no .vsix was found.

echo Ensure the "Visual Studio extension development" workload is installed.

exit /b 1



:found

echo.

echo VSIX output:

echo   %CD%\%VSIX%

echo.

echo Install: double-click the .vsix or Extensions ^> Manage Extensions ^> Install from a file

endlocal
