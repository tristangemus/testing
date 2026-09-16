;
; ClipForge installer
;
; Per-user install into %LOCALAPPDATA%\Programs\ClipForge so no administrator rights are needed.
;

Unicode true
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define APP_NAME     "ClipForge"
!define APP_VERSION  "1.0.0"
!define APP_PUBLISHER "ClipForge"
!define APP_EXE      "ClipForge.exe"
!define APP_REGKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipForge"
!define APP_RUNKEY   "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "Software\${APP_NAME}" "InstallDir"
RequestExecutionLevel user
ShowInstDetails show
ShowUnInstDetails show

VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName"     "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} setup"
VIAddVersionKey "FileVersion"     "${APP_VERSION}"
VIAddVersionKey "ProductVersion"  "${APP_VERSION}"
VIAddVersionKey "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "Copyright (c) 2026"

!define MUI_ICON   "${ICONFILE}"
!define MUI_UNICON "${ICONFILE}"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "Install ${APP_NAME}"
!define MUI_WELCOMEPAGE_TEXT  "${APP_NAME} records your gameplay and keeps an instant-replay buffer, so you can save the last moments of a match after they happen.$\r$\n$\r$\nIt installs for the current user only, so no administrator rights are needed.$\r$\n$\r$\nOn first launch ${APP_NAME} offers to download ffmpeg (about 80 MB), which it uses to capture and encode."

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"
!define MUI_FINISHPAGE_LINK "Read the ClipForge guide"
!define MUI_FINISHPAGE_LINK_LOCATION "https://github.com/tristangemus/testing/tree/main/clipforge"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; Offers to close a running copy; the files cannot be replaced while it is loaded.
!macro CloseRunningApp
  FindWindow $0 "" "ClipForge"
  ${If} $0 != 0
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
      "${APP_NAME} is currently running and must be closed to continue." \
      IDOK closeit IDCANCEL abortit
    closeit:
      nsExec::Exec 'taskkill /IM "${APP_EXE}" /F'
      Sleep 1200
      Goto done
    abortit:
      Abort
    done:
  ${EndIf}
!macroend

Section "ClipForge" SecCore
  SectionIn RO
  !insertmacro CloseRunningApp

  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}/*.*"

  WriteRegStr HKCU "Software\${APP_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\${APP_NAME}" "Version"    "${APP_VERSION}"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Add/Remove Programs entry
  WriteRegStr   HKCU "${APP_REGKEY}" "DisplayName"     "${APP_NAME}"
  WriteRegStr   HKCU "${APP_REGKEY}" "DisplayVersion"  "${APP_VERSION}"
  WriteRegStr   HKCU "${APP_REGKEY}" "Publisher"       "${APP_PUBLISHER}"
  WriteRegStr   HKCU "${APP_REGKEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE}"
  WriteRegStr   HKCU "${APP_REGKEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr   HKCU "${APP_REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "${APP_REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APP_REGKEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${APP_REGKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section /o "Start ClipForge when I sign in" SecStartup
  WriteRegStr HKCU "${APP_RUNKEY}" "ClipForge" '"$INSTDIR\${APP_EXE}" --minimized'
SectionEnd

LangString DESC_SecCore    ${LANG_ENGLISH} "The ClipForge application. Required."
LangString DESC_SecDesktop ${LANG_ENGLISH} "Place a ClipForge shortcut on the desktop."
LangString DESC_SecStartup ${LANG_ENGLISH} "Launch ClipForge minimised to the tray at sign-in, so the replay buffer is always ready."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecCore}    $(DESC_SecCore)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} $(DESC_SecStartup)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  !insertmacro CloseRunningApp

  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir  "$SMPROGRAMS\${APP_NAME}"

  DeleteRegValue HKCU "${APP_RUNKEY}" "ClipForge"
  DeleteRegKey   HKCU "${APP_REGKEY}"
  DeleteRegKey   HKCU "Software\${APP_NAME}"

  RMDir /r "$INSTDIR"

  ; Recorded clips live in the user's Videos folder and are never touched. The app-data folder
  ; holds settings, logs, the replay ring buffer and the downloaded ffmpeg.
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Also remove ClipForge settings and the downloaded ffmpeg?$\r$\n$\r$\nYour saved clips are kept either way." \
    IDNO keepdata
    RMDir /r "$LOCALAPPDATA\ClipForge"
  keepdata:
SectionEnd
