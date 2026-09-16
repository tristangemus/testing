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
!define DOTNET_URL   "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
!define DOTNET_DIR   "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App"

; REQUIRE_RUNTIME=1 builds the compact installer, whose payload is framework-dependent and
; therefore needs the .NET Desktop Runtime present on the machine.
!ifndef REQUIRE_RUNTIME
  !define REQUIRE_RUNTIME 0
!endif

!if ${REQUIRE_RUNTIME} == 1
  Var DotNetFound
!endif

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
!if ${REQUIRE_RUNTIME} == 1
  !define MUI_WELCOMEPAGE_TEXT "${APP_NAME} records your gameplay and keeps an instant-replay buffer, so you can save the last moments of a match after they happen.$\r$\n$\r$\nIt installs for the current user only, so no administrator rights are needed.$\r$\n$\r$\nThis compact build uses the .NET 8 Desktop Runtime and will offer to install it if your PC does not have it yet. On first launch ${APP_NAME} also offers to download ffmpeg (about 80 MB), which it uses to capture and encode."
!else
  !define MUI_WELCOMEPAGE_TEXT "${APP_NAME} records your gameplay and keeps an instant-replay buffer, so you can save the last moments of a match after they happen.$\r$\n$\r$\nIt installs for the current user only, so no administrator rights are needed.$\r$\n$\r$\nEverything it needs to run is included. On first launch ${APP_NAME} offers to download ffmpeg (about 80 MB), which it uses to capture and encode."
!endif

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

; Sets $DotNetFound to 1 if a Windows Desktop runtime of this major version is present.
!macro LookForRuntime pattern
  ${If} $DotNetFound == "0"
    FindFirst $0 $1 "${DOTNET_DIR}\${pattern}"
    ${If} $0 != ""
      ${IfNot} $1 == ""
        StrCpy $DotNetFound "1"
      ${EndIf}
      FindClose $0
    ${EndIf}
  ${EndIf}
!macroend

!if ${REQUIRE_RUNTIME} == 1
Function EnsureDotNet
  StrCpy $DotNetFound "0"
  ; The app rolls forward to a later major, so any of these will run it.
  !insertmacro LookForRuntime "8.*"
  !insertmacro LookForRuntime "9.*"
  !insertmacro LookForRuntime "10.*"
  ${If} $DotNetFound == "1"
    DetailPrint ".NET Desktop Runtime found."
    Return
  ${EndIf}

  MessageBox MB_YESNO|MB_ICONQUESTION \
    "${APP_NAME} needs the .NET 8 Desktop Runtime, which is not installed on this PC.$\r$\n$\r$\nDownload and install it from Microsoft now? (about 55 MB)" \
    IDYES dodownload
    MessageBox MB_OK|MB_ICONINFORMATION \
      "${APP_NAME} will be installed, but will not start until the runtime is present.$\r$\n$\r$\nGet it from:$\r$\nhttps://dotnet.microsoft.com/download/dotnet/8.0"
    Return

  dodownload:
  StrCpy $2 "$TEMP\windowsdesktop-runtime-x64.exe"
  DetailPrint "Downloading the .NET Desktop Runtime..."
  ; curl.exe ships with Windows 10 1803 and later, and unlike NSISdl it speaks HTTPS.
  nsExec::ExecToLog '"$SYSDIR\curl.exe" -sSL --fail -o "$2" "${DOTNET_URL}"'
  Pop $3
  ${If} $3 != 0
    Delete "$2"
    MessageBox MB_OK|MB_ICONEXCLAMATION \
      "The runtime download failed (code $3).$\r$\n$\r$\nInstall it manually from:$\r$\nhttps://dotnet.microsoft.com/download/dotnet/8.0"
    Return
  ${EndIf}

  DetailPrint "Installing the .NET Desktop Runtime..."
  nsExec::ExecToLog '"$2" /install /quiet /norestart'
  Pop $3
  Delete "$2"

  ; 0 = installed, 3010 = installed but wants a reboot, 1638 = a newer build is already there.
  ${If} $3 != 0
  ${AndIf} $3 != 3010
  ${AndIf} $3 != 1638
    MessageBox MB_OK|MB_ICONEXCLAMATION \
      "The runtime installer returned code $3. ${APP_NAME} may not start until the .NET 8 Desktop Runtime is installed."
  ${EndIf}
FunctionEnd
!endif

Section "ClipForge" SecCore
  SectionIn RO
  !insertmacro CloseRunningApp

  !if ${REQUIRE_RUNTIME} == 1
    Call EnsureDotNet
  !endif

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
