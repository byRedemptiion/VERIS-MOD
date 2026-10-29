@echo off
rem Copie le script dans %TEMP% puis le lance : le dossier d'installation peut ainsi etre supprime.
cd /d "%TEMP%"
copy /y "%~dp0uninstall.ps1" "%TEMP%\veris_uninstall.ps1" >nul
if not exist "%TEMP%\veris_uninstall.ps1" (
  echo Script de desinstallation introuvable.
  pause
  exit /b 1
)
start "Desinstallation de VERIS MOD" powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\veris_uninstall.ps1"
exit
