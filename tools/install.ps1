param(
  [Parameter(Mandatory = $true)][string]$Source,
  [Parameter(Mandatory = $true)][string]$Root
)

$ErrorActionPreference = 'Stop'

try {
  $dest = Join-Path $env:LOCALAPPDATA 'Programs\ViiCord'
  $exe  = Join-Path $dest 'ViiCord.exe'

  if (-not (Test-Path (Join-Path $Source 'ViiCord.exe'))) {
    throw "ViiCord.exe introuvable dans $Source (la compilation a-t-elle reussi ?)"
  }

  # 1. Ferme ViiCord s'il tourne (mise a jour d'une version deja installee)
  Get-Process -Name 'ViiCord' -ErrorAction SilentlyContinue | ForEach-Object {
    try {
      [void]$_.CloseMainWindow()
      if (-not $_.WaitForExit(4000)) { $_.Kill() }
    } catch { }
  }

  # 2. Dossiers : programme + donnees (les reglages existants sont conserves)
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  # (les reglages dans %APPDATA%\ViiCord sont crees par l'application)

  # 3. Copie de l'application (exe + sons + mascotte)
  Get-ChildItem -LiteralPath $dest -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
  Copy-Item -Path (Join-Path $Source '*') -Destination $dest -Recurse -Force
  Copy-Item -LiteralPath (Join-Path $Root 'tools\uninstall.ps1') -Destination (Join-Path $dest 'uninstall.ps1') -Force
  Copy-Item -LiteralPath (Join-Path $Root 'tools\Desinstaller.cmd') -Destination (Join-Path $dest 'Desinstaller.cmd') -Force

  # 4. Raccourcis : Bureau + menu Demarrer
  $ws = New-Object -ComObject WScript.Shell
  function New-AppShortcut([string]$path) {
    $s = $ws.CreateShortcut($path)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $dest
    $s.IconLocation = "$exe,0"
    $s.Description = 'ViiCord - personnalisation des sons Discord'
    $s.Save()
  }
  $desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ViiCord.lnk'
  $startLnk   = Join-Path ([Environment]::GetFolderPath('Programs')) 'ViiCord.lnk'
  New-AppShortcut $desktopLnk
  New-AppShortcut $startLnk

  # 5. Entree "Applications installees" de Windows (desinstallation depuis les Parametres)
  $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ViiCord'
  New-Item -Path $key -Force | Out-Null
  $unCmd = '"' + (Join-Path $dest 'Desinstaller.cmd') + '"'
  New-ItemProperty -Path $key -Name 'DisplayName'     -Value 'ViiCord' -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $key -Name 'DisplayVersion'  -Value '1.5.0' -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $key -Name 'Publisher'       -Value 'byRedemptiion' -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $key -Name 'InstallLocation' -Value $dest -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $key -Name 'DisplayIcon'     -Value $exe -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $key -Name 'UninstallString' -Value $unCmd -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $key -Name 'NoModify'        -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty -Path $key -Name 'NoRepair'        -Value 1 -PropertyType DWord -Force | Out-Null

  Write-Host ''
  Write-Host '  ==========================================' -ForegroundColor Green
  Write-Host '    ViiCord est installe !' -ForegroundColor Green
  Write-Host '  ==========================================' -ForegroundColor Green
  Write-Host ''
  Write-Host "  Programme   : $dest"
  Write-Host "  Reglages    : $data"
  Write-Host '  Raccourcis  : Bureau + menu Demarrer ("ViiCord")'
  Write-Host ''
  Write-Host '  Lancement de ViiCord...'
  Start-Process -FilePath $exe -WorkingDirectory $dest
  Start-Sleep -Seconds 3
  exit 0
}
catch {
  Write-Host ''
  Write-Host ('  ERREUR : ' + $_.Exception.Message) -ForegroundColor Red
  Write-Host ''
  Read-Host '  Appuie sur Entree pour fermer'
  exit 1
}
