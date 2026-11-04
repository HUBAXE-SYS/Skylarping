; Skylarping installer. Build Skylarping in Release first (Skylarping\bin\Release).
; Optional deps are picked up from ..\deps like build.bat does: curl.exe and curl-ca-bundle.crt
; (HTTPS on Windows XP) go next to the exe; tools in deps\tools (and WebTools' curl/yt-dlp) are
; already in bin\Release via the project. License texts for them come from ..\THIRD_PARTY_LICENSES.

!define SOURCE_DIR "..\Skylarping\bin\Release"
!define ROOT_DIR ".."
!define PRODUCT_NAME "Skylarping"
!define PRODUCT_PUBLISHER "The Skylarping Team"
!define PRODUCT_WEB_SITE "https://github.com/randomNinja64/Skylarping"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\Skylarping.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

Unicode true
RequestExecutionLevel admin
!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "installer.ico"
!define MUI_UNICON "installer.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_HEADERIMAGE_BITMAP_STRETCH "AspectFitHeight"
!define MUI_WELCOMEFINISHPAGE_BITMAP "column.bmp"

Function HideHeaderText
    GetDlgItem $0 $HWNDPARENT 1037
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1038
    ShowWindow $0 ${SW_HIDE}
FunctionEnd

Function KillSkylarpingProcesses
    nsExec::ExecToLog 'taskkill /F /IM Skylarping.exe'
FunctionEnd

!define MUI_PAGE_CUSTOMFUNCTION_SHOW HideHeaderText
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_SHOW HideHeaderText
!insertmacro MUI_PAGE_LICENSE "license.txt"
!define MUI_PAGE_CUSTOMFUNCTION_SHOW HideHeaderText
!insertmacro MUI_PAGE_DIRECTORY
!define MUI_PAGE_CUSTOMFUNCTION_SHOW HideHeaderText
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Skylarping.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
    ; The app is AnyCPU (.NET Framework 4.0, Windows XP and later)
    System::Call "kernel32::GetSystemWow64Directory(t .r0, i ${NSIS_MAX_STRLEN})"
    ${If} $0 != ""
        StrCpy $INSTDIR "$PROGRAMFILES64\Skylarping"
    ${Else}
        StrCpy $INSTDIR "$PROGRAMFILES\Skylarping"
    ${EndIf}
FunctionEnd

Name "${PRODUCT_NAME}"
OutFile ".\Skylarping Installer.exe"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
ShowInstDetails show
ShowUnInstDetails show

Section "Install"
    Call KillSkylarpingProcesses
    ; Only wipe the folder if it holds a previous Skylarping install,
    ; never an arbitrary folder the user picked.
    ${If} ${FileExists} "$INSTDIR\Skylarping.exe"
        SetFileAttributes "$INSTDIR" NORMAL
        RMDir /r "$INSTDIR"
    ${EndIf}
    SetOutPath "$INSTDIR"
    SetOverwrite on
    File /r /x *.pdb /x *.xml /x *.vshost.* "${SOURCE_DIR}\*"
    File "${ROOT_DIR}\LICENSE.md"
    ; Optional deps (same as build.bat; skipped with a warning if missing)
    File /nonfatal "${ROOT_DIR}\deps\curl.exe"
    File /nonfatal "${ROOT_DIR}\deps\curl-ca-bundle.crt"
    ; Optional third-party license texts for those deps
    SetOutPath "$INSTDIR\THIRD_PARTY_LICENSES"
    File /nonfatal /x .gitkeep "${ROOT_DIR}\THIRD_PARTY_LICENSES\*"
    SetOutPath "$INSTDIR"

    ; Carry over data from before the rebrand (the app does this too on first run)
    ${If} ${FileExists} "$APPDATA\Skymu\*.*"
    ${AndIfNot} ${FileExists} "$APPDATA\Skylarping\*.*"
        Rename "$APPDATA\Skymu" "$APPDATA\Skylarping"
    ${EndIf}

    WriteIniStr "$INSTDIR\desktop.ini" ".ShellClassInfo" "IconFile" "$INSTDIR\Skylarping.exe"
    WriteIniStr "$INSTDIR\desktop.ini" ".ShellClassInfo" "IconIndex" "-911"
    CreateDirectory "$APPDATA\Skylarping"
    WriteIniStr "$APPDATA\Skylarping\desktop.ini" ".ShellClassInfo" "IconFile" "$INSTDIR\Skylarping.exe"
    WriteIniStr "$APPDATA\Skylarping\desktop.ini" ".ShellClassInfo" "IconIndex" "-911"
    SetFileAttributes "$APPDATA\Skylarping\desktop.ini" HIDDEN|SYSTEM
    SetFileAttributes "$APPDATA\Skylarping" READONLY
    SetFileAttributes "$INSTDIR\desktop.ini" HIDDEN|SYSTEM
    SetFileAttributes "$INSTDIR" READONLY
    CreateDirectory "$SMPROGRAMS\Skylarping"
    CreateShortCut "$SMPROGRAMS\Skylarping\Skylarping.lnk" "$INSTDIR\Skylarping.exe"
    CreateShortCut "$DESKTOP\Skylarping.lnk" "$INSTDIR\Skylarping.exe"
    WriteIniStr "$INSTDIR\${PRODUCT_NAME}.url" "InternetShortcut" "URL" "${PRODUCT_WEB_SITE}"
    CreateShortCut "$SMPROGRAMS\Skylarping\Website.lnk" "$INSTDIR\${PRODUCT_NAME}.url"
    CreateShortCut "$SMPROGRAMS\Skylarping\Uninstall.lnk" "$INSTDIR\uninst.exe"
    WriteUninstaller "$INSTDIR\uninst.exe"
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\Skylarping.exe"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "$(^Name)"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\Skylarping.exe"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
SectionEnd

Function un.onUninstSuccess
    HideWindow
    MessageBox MB_ICONINFORMATION|MB_OK "$(^Name) was successfully removed from your computer."
FunctionEnd

Function un.onInit
    MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 "Are you sure you want to completely remove $(^Name) and all of its components?" IDYES +2
    Abort
FunctionEnd

Section Uninstall
    nsExec::ExecToLog 'taskkill /F /IM Skylarping.exe'
    Delete "$INSTDIR\uninst.exe"
    Delete "$INSTDIR\${PRODUCT_NAME}.url"
    Delete "$SMPROGRAMS\Skylarping\Uninstall.lnk"
    Delete "$SMPROGRAMS\Skylarping\Website.lnk"
    Delete "$DESKTOP\Skylarping.lnk"
    Delete "$SMPROGRAMS\Skylarping\Skylarping.lnk"
    RMDir "$SMPROGRAMS\Skylarping"
    SetFileAttributes "$INSTDIR" NORMAL
    RMDir /r "$INSTDIR"
    ; Remove the "start with Windows" entry (Settings > Skylarping)
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Skylarping"
    DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
    SetAutoClose true
SectionEnd
