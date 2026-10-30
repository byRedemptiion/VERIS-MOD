@echo off
setlocal
chcp 65001 >nul
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "ROOT=%~dp0"

if not exist "%ROOT%src\ViiCord.csproj" (
  echo.
  echo  Ce fichier doit etre dans le dossier ViiCord, a cote du dossier "src".
  echo  Dossier actuel : %ROOT%
  echo.
  pause
  exit /b 1
)

set "OUT=%ROOT%publish"
set "LOG=%ROOT%build-log.txt"
set "DOTNET=dotnet"

rem --- SDK .NET 8 : utilise celui du PC, sinon un SDK local (sans droits admin)
set "SDKDIR=%LOCALAPPDATA%\ViiCordBuild\dotnet"
if exist "%ROOT%.dotnet\dotnet.exe" set "SDKDIR=%ROOT%.dotnet"

dotnet --list-sdks 2>nul | findstr /b /c:"8." >nul
if errorlevel 1 (
  if not exist "%SDKDIR%\dotnet.exe" (
    echo.
    echo  SDK .NET 8 absent : telechargement automatique ^(environ 200 Mo, une seule fois^)...
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile '%TEMP%\dotnet-install.ps1'"
    if not exist "%TEMP%\dotnet-install.ps1" goto nosdk
    powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\dotnet-install.ps1" -Channel 8.0 -InstallDir "%SDKDIR%"
    if not exist "%SDKDIR%\dotnet.exe" goto nosdk
  )
  set "DOTNET=%SDKDIR%\dotnet.exe"
  set "DOTNET_ROOT=%SDKDIR%"
)

cd /d "%ROOT%src"
if exist "%OUT%" rmdir /s /q "%OUT%"

echo.
echo  === Compilation de ViiCord 1.5 ===
echo  SDK .NET :
"%DOTNET%" --version
echo  Compilation en cours ^(1 a 3 minutes, ne ferme pas la fenetre^)...
echo.

"%DOTNET%" publish ViiCord.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%" > "%LOG%" 2>&1
set "RC=%errorlevel%"

if not "%RC%"=="0" goto fail
if not exist "%OUT%\ViiCord.exe" goto fail

echo  OK. Application prete :
echo    %OUT%\ViiCord.exe
echo.
if /i "%~1"=="/silent" exit /b 0
explorer "%OUT%"
pause
exit /b 0

:nosdk
echo.
echo  Impossible de telecharger le SDK automatiquement.
echo  Installe-le a la main : https://dotnet.microsoft.com/download/dotnet/8.0
echo  ^(SDK 8.0, Windows x64^), puis relance ce fichier.
echo.
pause
exit /b 1

:fail
echo.
echo  ECHEC de la compilation ^(code %RC%^).
echo  Lignes d'erreur :
findstr /i /c:"error" "%LOG%"
echo.
echo  Journal complet : %LOG%
echo  Envoie-moi ce fichier build-log.txt ^(ou une capture de cette fenetre^).
echo.
pause
exit /b 1
