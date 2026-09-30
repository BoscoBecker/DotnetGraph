@echo off

setlocal



set "ICON_DIR=%~dp0..\src\DotnetGraph.Extension\Resources\Icons"

set "PROJ=%~dp0IconGen\DotnetGraph.IconGen.csproj"



if "%1"=="/force" goto :generate

if exist "%ICON_DIR%\MenuIconSource.png" copy /Y "%ICON_DIR%\MenuIconSource.png" "%ICON_DIR%\CommandGraph16.png" >nul

if exist "%ICON_DIR%\PackageIcon.png" if exist "%ICON_DIR%\CommandGraph16.png" if exist "%ICON_DIR%\CommandGraph32.png" exit /b 0

:generate

if not exist "%ICON_DIR%" mkdir "%ICON_DIR%"



dotnet run --project "%PROJ%" -c Release -- "%ICON_DIR%"

exit /b %ERRORLEVEL%

