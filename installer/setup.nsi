Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "StrFunc.nsh"
${UnStrStr}

!define APP_NAME      "ViiCord"
!ifndef APP_VERSION
  !define APP_VERSION "1.5.1"
!endif
!ifndef APP_VERSION4
  !define APP_VERSION4 "${APP_VERSION}.0"
!endif
!define APP_EXE       "ViiCord.exe"
!define APP_PUBLISHER "byRedemptiion"
!define UNINST_KEY    "Software\Microsoft\Windows\CurrentVersion\Uninstall\ViiCord"
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif

Name "${APP_NAME}"
OutFile "ViiCord_Setup_${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\ViiCord"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APP_NAME} ${APP_VERSION} - ${APP_PUBLISHER}"

VIProductVersion "${APP_VERSION4}"
VIAddVersionKey /LANG=1036 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1036 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=1036 "FileDescription" "Installateur de ${APP_NAME}"
VIAddVersionKey /LANG=1036 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1036 "LegalCopyright" "${APP_PUBLISHER}"

!define MUI_ICON "..\src\Assets\ViiCord.ico"
!define MUI_UNICON "..\src\Assets\ViiCord.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Bienvenue dans l'installation de ${APP_NAME}"
!define MUI_WELCOMEPAGE_TEXT "${APP_NAME} personnalise les sons de Discord : un son, un volume, et un retour au son d'origine en un clic.$\r$\n$\r$\nL'installation est rapide et ne demande aucun droit administrateur. Un raccourci sera ajouté sur le Bureau et dans le menu Démarrer.$\r$\n$\r$\nClique sur Suivant pour continuer."
!define MUI_FINISHPAGE_TITLE "${APP_NAME} est installé !"
!define MUI_FINISHPAGE_TEXT "Au premier lancement, accepte « Activer ${APP_NAME} » : Discord sera fermé puis relancé automatiquement.$\r$\n$\r$\nTu peux changer l'ambiance de couleur avec les pastilles en bas à droite de la fenêtre."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer ${APP_NAME} maintenant"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"

Section "Install"
  SetShellVarContext current

  ; Ferme ViiCord s'il tourne (mise à jour)
  nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
  Pop $0
  Sleep 1500

  SetOutPath "$INSTDIR"
  File "${PUBLISH_DIR}\${APP_EXE}"
  SetOutPath "$INSTDIR\sounds"
  File "${PUBLISH_DIR}\sounds\*.wav"
  SetOutPath "$INSTDIR"

  ; Les dossiers de réglages (%APPDATA%\ViiCord) sont créés par l'application elle-même : elle y migre aussi
  ; l'ancien dossier « Audely » sans rien perdre (voir AppPaths.DataDir). Rien à créer ici.

  ; Legacy VERIS MOD / Audely compatibility : nettoyage de l'ancienne installation (ancien nom).
  nsExec::Exec 'taskkill /F /IM VerisMod.exe'
  Pop $0
  Delete "$DESKTOP\VERIS MOD.lnk"
  Delete "$SMPROGRAMS\VERIS MOD\VERIS MOD.lnk"
  Delete "$SMPROGRAMS\VERIS MOD\Désinstaller VERIS MOD.lnk"
  RMDir "$SMPROGRAMS\VERIS MOD"
  Delete "$LOCALAPPDATA\Programs\VERIS MOD\VerisMod.exe"
  Delete "$LOCALAPPDATA\Programs\VERIS MOD\sounds\*.wav"
  RMDir "$LOCALAPPDATA\Programs\VERIS MOD\sounds"
  Delete "$LOCALAPPDATA\Programs\VERIS MOD\Uninstall.exe"
  RMDir "$LOCALAPPDATA\Programs\VERIS MOD"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VERIS MOD"

  ; Raccourcis
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Désinstaller ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  ; Désinstalleur + entrée « Applications installées »
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ; Mise à jour depuis l'application (setup lancé avec /S) : on relance ViiCord tout seul.
  ${If} ${Silent}
    Exec '"$INSTDIR\${APP_EXE}"'
  ${EndIf}
SectionEnd

Section "Uninstall"
  SetShellVarContext current

  ; Dossier des réglages : ViiCord (ou l'ancien dossier Audely si la migration n'a pas encore eu lieu).
  StrCpy $R8 "$APPDATA\ViiCord"
  ${IfNot} ${FileExists} "$R8\install-state.json"
    StrCpy $R8 "$APPDATA\Audely"
  ${EndIf}

  ; Si ViiCord est encore actif dans Discord, on prévient (le hook doit être retiré par l'appli).
  StrCpy $R9 "0"
  ${If} ${FileExists} "$R8\install-state.json"
    FileOpen $0 "$R8\install-state.json" r
    ${Unless} ${Errors}
      loop:
        FileRead $0 $1
        ${If} ${Errors}
          Goto done
        ${EndIf}
        ${UnStrStr} $2 $1 '"installed": true'
        ${If} $2 != ""
          StrCpy $R9 "1"
          Goto done
        ${EndIf}
        ${UnStrStr} $2 $1 '"installed":true'
        ${If} $2 != ""
          StrCpy $R9 "1"
          Goto done
        ${EndIf}
        Goto loop
      done:
      FileClose $0
    ${EndUnless}
  ${EndIf}

  ${If} $R9 == "1"
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "ViiCord est encore actif dans Discord.$\r$\n$\r$\nPour que Discord retrouve ses fichiers d'origine, ouvre ViiCord et clique sur « Désinstaller » (menu de gauche) AVANT de continuer.$\r$\n$\r$\nDésinstaller le programme quand même ? (tes réglages et sauvegardes sont conservés)" /SD IDYES IDYES +2
    Abort
  ${EndIf}

  nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
  Pop $0
  Sleep 800

  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Désinstaller ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\sounds\*.wav"
  RMDir "$INSTDIR\sounds"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${UNINST_KEY}"

  ; Réglages et sons perso : on demande (jamais supprimés si le hook est encore actif).
  ${If} $R9 != "1"
  ${AndIf} ${FileExists} "$R8\*.*"
    MessageBox MB_YESNO|MB_ICONQUESTION "Supprimer aussi tes réglages et sons perso (dossier ViiCord) ?$\r$\n$\r$\nChoisis Non si tu comptes réinstaller ViiCord plus tard." /SD IDNO IDNO keepdata
    ; Legacy Audely compatibility : « Audely » est un simple lien vers « ViiCord » après la migration.
    RMDir "$APPDATA\Audely"
    RMDir /r "$APPDATA\ViiCord"
    ${If} ${FileExists} "$APPDATA\Audely\*.*"
      RMDir /r "$APPDATA\Audely"
    ${EndIf}
    keepdata:
  ${EndIf}
SectionEnd
