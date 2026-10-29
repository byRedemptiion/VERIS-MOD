$ErrorActionPreference = 'Continue'
Set-Location $env:TEMP

$dest = Join-Path $env:LOCALAPPDATA 'Programs\VERIS MOD'
$data = Join-Path $env:APPDATA 'Audely'
$key  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VERIS MOD'

Write-Host ''
Write-Host '  ==========================================' -ForegroundColor Cyan
Write-Host '    DESINSTALLATION DE VERIS MOD' -ForegroundColor Cyan
Write-Host '  ==========================================' -ForegroundColor Cyan
Write-Host ''

# Le hook installe dans Discord doit etre retire par l'application elle-meme (restauration verifiee).
$hookActive = $false
$statePath = Join-Path $data 'install-state.json'
if (Test-Path $statePath) {
  try { $hookActive = ((Get-Content -LiteralPath $statePath -Raw) -match '(?i)"installed"\s*:\s*true') } catch { }
}

if ($hookActive) {
  Write-Host '  ATTENTION : VERIS MOD est encore active dans Discord.' -ForegroundColor Yellow
  Write-Host '  Avant de desinstaller, ouvre VERIS MOD et clique sur "Desinstaller"'
  Write-Host '  (menu de gauche) : cela restaure les fichiers originaux de Discord.'
  Write-Host ''
  $r = Read-Host '  Continuer quand meme ? Tes reglages et sauvegardes seront conserves (o/N)'
  if ($r -notmatch '^[oOyY]') {
    Write-Host '  Desinstallation annulee.'
    Start-Sleep -Seconds 2
    exit 0
  }
}

Get-Process -Name 'VerisMod' -ErrorAction SilentlyContinue | ForEach-Object {
  try { [void]$_.CloseMainWindow(); if (-not $_.WaitForExit(4000)) { $_.Kill() } } catch { }
}
Start-Sleep -Seconds 1

Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'VERIS MOD.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) 'VERIS MOD.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '  Programme et raccourcis supprimes.' -ForegroundColor Green

if (-not $hookActive -and (Test-Path $data)) {
  Write-Host ''
  $r = Read-Host '  Supprimer aussi tes reglages et sons perso (dossier Audely) ? (o/N)'
  if ($r -match '^[oOyY]') {
    Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '  Reglages supprimes.' -ForegroundColor Green
  } else {
    Write-Host '  Reglages conserves (utile si tu reinstalles plus tard).'
  }
}

Write-Host ''
Write-Host '  Termine. A bientot !'
Start-Sleep -Seconds 3
