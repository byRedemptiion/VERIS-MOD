@echo off
setlocal
chcp 65001 >nul
title Installation de VERIS MOD
cd /d "%~dp0"

echo.
echo  ==========================================
echo    INSTALLATION DE VERIS MOD  (byRedemptiion)
echo  ==========================================
echo.
echo  Etape 1/2 : preparation de l'application
echo  (premiere fois : 2 a 5 minutes, ne ferme pas cette fenetre)
echo.

call "%~dp0build.cmd" /silent
if errorlevel 1 exit /b 1

echo  Etape 2/2 : installation sur ce PC
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" -Source "%~dp0publish" -Root "%~dp0."
if errorlevel 1 (
  echo.
  echo  L'installation a echoue. Envoie-moi une capture de cette fenetre.
  echo.
  pause
  exit /b 1
)
exit /b 0
