Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "StrFunc.nsh"
${UnStrStr}

!define APP_NAME      "VERIS MOD"
!ifndef APP_VERSION
  !define APP_VERSION "1.5.1"
!endif
!ifndef APP_VERSION4
  !define APP_VERSION4 "${APP_VERSION}.0"
!endif
!define APP_EXE       "VerisMod.exe"
!define APP_PUBLISHER "byRedemptiion"
!define UNINST_KEY    "Software\Microsoft\Windows\CurrentVersion\Uninstall\VERIS MOD"
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif

Name "${APP_NAME}"
OutFile "VERIS_MOD_Setup_${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\VERIS MOD"
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

!define MUI_ICON "..\src\Assets\Audely.ico"
!define MUI_UNICON "..\src\Assets\Audely.ico"
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

  ; Ferme VERIS MOD s'il tourne (mise à jour)
  nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
  Pop $0
  Sleep 1500

  SetOutPath "$INSTDIR"
  File "${PUBLISH_DIR}\${APP_EXE}"
  SetOutPath "$INSTDIR\sounds"
  File "${PUBLISH_DIR}\sounds\*.wav"
  SetOutPath "$INSTDIR"

  ; Dossiers de réglages (conservés s'ils existent déjà)
  CreateDirectory "$APPDATA\Audely"
  CreateDirectory "$APPDATA\Audely\library"
  CreateDirectory "$APPDATA\Audely\sounds"
  CreateDirectory "$APPDATA\Audely\backups"

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

  ; Mise à jour depuis l'application (setup lancé avec /S) : on relance VERIS MOD tout seul.
  ${If} ${Silent}
    Exec '"$INSTDIR\${APP_EXE}"'
  ${EndIf}
SectionEnd

Section "Uninstall"
  SetShellVarContext current

  ; Si VERIS MOD est encore actif dans Discord, on prévient (le hook doit être retiré par l'appli).
  StrCpy $R9 "0"
  ${If} ${FileExists} "$APPDATA\Audely\install-state.json"
    FileOpen $0 "$APPDATA\Audely\install-state.json" r
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
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "VERIS MOD est encore actif dans Discord.$\r$\n$\r$\nPour que Discord retrouve ses fichiers d'origine, ouvre VERIS MOD et clique sur « Désinstaller » (menu de gauche) AVANT de continuer.$\r$\n$\r$\nDésinstaller le programme quand même ? (tes réglages et sauvegardes sont conservés)" /SD IDYES IDYES +2
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
  ${AndIf} ${FileExists} "$APPDATA\Audely\*.*"
    MessageBox MB_YESNO|MB_ICONQUESTION "Supprimer aussi tes réglages et sons perso (dossier Audely) ?$\r$\n$\r$\nChoisis Non si tu comptes réinstaller VERIS MOD plus tard." /SD IDNO IDNO keepdata
    RMDir /r "$APPDATA\Audely"
    keepdata:
  ${EndIf}
SectionEnd
