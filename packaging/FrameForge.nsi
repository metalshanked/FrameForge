Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"
!include "x64.nsh"
Name "FrameForge"
OutFile "..\dist\FrameForge-Setup-0.2.5.exe"
InstallDir "$LOCALAPPDATA\Programs\FrameForge"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ManifestDPIAware true
VIProductVersion "0.2.5.0"
VIAddVersionKey /LANG=1033 "ProductName" "FrameForge"
VIAddVersionKey /LANG=1033 "FileDescription" "FrameForge Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "0.2.5"
VIAddVersionKey /LANG=1033 "LegalCopyright" "FrameForge contributors"
!define MUI_ICON "..\assets\FrameForge.ico"
!define MUI_UNICON "..\assets\FrameForge.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install FrameForge for your Windows account.$\r$\n$\r$\nCapture screenshots, annotate, and keep capture shortcuts available in the system tray.$\r$\n$\r$\nRecording uses a separately installed FFmpeg. Your capture library is preserved during upgrades and uninstall."
!define MUI_FINISHPAGE_RUN "$INSTDIR\FrameForge.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open FrameForge"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var TestMode
Var AppKey
Var StartupKey
Var UninstallKey
Var ShortcutFolder
Var DesktopLink

!macro ConfigureIntegration
    SetShellVarContext current
    SetRegView 64
    StrCpy $AppKey "Software\FrameForge"
    StrCpy $StartupKey "Software\Microsoft\Windows\CurrentVersion\Run"
    StrCpy $UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameForge"
    StrCpy $ShortcutFolder "$SMPROGRAMS\FrameForge"
    StrCpy $DesktopLink "$DESKTOP\FrameForge.lnk"
    ${If} $TestMode == "1"
        StrCpy $AppKey "Software\FrameForge.PackageTest"
        StrCpy $StartupKey "Software\FrameForge.PackageTest\Run"
        StrCpy $UninstallKey "Software\FrameForge.PackageTest\Uninstall"
        StrCpy $ShortcutFolder "$INSTDIR\.test-shell\StartMenu"
        StrCpy $DesktopLink "$INSTDIR\.test-shell\Desktop\FrameForge.lnk"
    ${EndIf}
!macroend

Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP "FrameForge requires 64-bit Windows."
        Abort
    ${EndIf}
    StrCpy $TestMode "0"
    ${GetParameters} $0
    ClearErrors
    ${GetOptions} $0 "/TESTMODE" $1
    ${IfNot} ${Errors}
        StrCpy $TestMode "1"
    ${EndIf}
    !insertmacro ConfigureIntegration
    ${If} $TestMode != "1"
        ReadRegStr $0 HKCU "$AppKey" "InstallLocation"
        ${If} $0 != ""
            StrCpy $INSTDIR $0
        ${EndIf}
    ${EndIf}
FunctionEnd

Function EnsureAppClosed
    retry:
    System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\FrameForge.Editor") p .r0'
    ${If} $0 != 0
        System::Call 'kernel32::CloseHandle(p r0)'
        IfSilent abort
        MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "FrameForge is running. Right-click its tray icon, choose Exit FrameForge, then click Retry." IDRETRY retry
        abort:
        SetErrorLevel 2
        Abort
    ${EndIf}
FunctionEnd

Section "FrameForge (required)" Core
    SectionIn RO
    Call EnsureAppClosed
    !insertmacro ConfigureIntegration
    SetOutPath "$INSTDIR"
    File "..\dist\portable\FrameForge.exe"
    File /oname=GettingStarted.txt "..\docs\GettingStarted.txt"
    File "..\LICENSE"
    File "..\THIRD-PARTY-NOTICES.md"
    SetOutPath "$INSTDIR\licenses"
    File "..\licenses\*.txt"
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteINIStr "$INSTDIR\install.ini" "FrameForge" "TestMode" "$TestMode"
    WriteRegStr HKCU "$AppKey" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "$UninstallKey" "DisplayName" "FrameForge"
    WriteRegStr HKCU "$UninstallKey" "DisplayVersion" "0.2.5"
    WriteRegStr HKCU "$UninstallKey" "Publisher" "FrameForge contributors"
    WriteRegStr HKCU "$UninstallKey" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "$UninstallKey" "DisplayIcon" "$INSTDIR\FrameForge.exe,0"
    WriteRegStr HKCU "$UninstallKey" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "$UninstallKey" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "$UninstallKey" "NoModify" 1
    WriteRegDWORD HKCU "$UninstallKey" "NoRepair" 1
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    WriteRegDWORD HKCU "$UninstallKey" "EstimatedSize" $0
    CreateDirectory "$ShortcutFolder"
    CreateShortcut "$ShortcutFolder\FrameForge.lnk" "$INSTDIR\FrameForge.exe" "" "$INSTDIR\FrameForge.exe" 0
    CreateShortcut "$ShortcutFolder\Uninstall FrameForge.lnk" "$INSTDIR\Uninstall.exe"
    ; Preserve a previous opt-in and update its executable path on upgrade.
    ReadRegStr $0 HKCU "$StartupKey" "FrameForge"
    ${If} $0 != ""
        WriteRegStr HKCU "$StartupKey" "FrameForge" '$\"$INSTDIR\FrameForge.exe$\" --background'
    ${EndIf}
SectionEnd

Section /o "Desktop shortcut" Desktop
    ${GetParent} "$DesktopLink" $0
    CreateDirectory "$0"
    CreateShortcut "$DesktopLink" "$INSTDIR\FrameForge.exe" "" "$INSTDIR\FrameForge.exe" 0
SectionEnd

Section /o "Start quietly when I sign in" Startup
    WriteRegStr HKCU "$StartupKey" "FrameForge" '$\"$INSTDIR\FrameForge.exe$\" --background'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
!insertmacro MUI_DESCRIPTION_TEXT ${Core} "App, Start-menu shortcuts, and uninstaller. Your existing library and settings are preserved."
!insertmacro MUI_DESCRIPTION_TEXT ${Desktop} "Add a FrameForge shortcut to your desktop."
!insertmacro MUI_DESCRIPTION_TEXT ${Startup} "Start in the system tray at sign-in so your capture shortcut is always ready. This is optional."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function un.onInit
    ReadINIStr $TestMode "$INSTDIR\install.ini" "FrameForge" "TestMode"
    !insertmacro ConfigureIntegration
    System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\FrameForge.Editor") p .r0'
    ${If} $0 != 0
        System::Call 'kernel32::CloseHandle(p r0)'
        IfSilent +2
        MessageBox MB_ICONEXCLAMATION "Exit FrameForge from its tray menu, then run uninstall again."
        SetErrorLevel 2
        Abort
    ${EndIf}
FunctionEnd

Section "Uninstall"
    ; Only remove integration belonging to this installation.
    ReadRegStr $0 HKCU "$UninstallKey" "InstallLocation"
    ${If} $0 == $INSTDIR
        DeleteRegKey HKCU "$UninstallKey"
        DeleteRegValue HKCU "$AppKey" "InstallLocation"
        DeleteRegKey /ifempty HKCU "$AppKey"
        Delete "$ShortcutFolder\FrameForge.lnk"
        Delete "$ShortcutFolder\Uninstall FrameForge.lnk"
        RMDir "$ShortcutFolder"
        Delete "$DesktopLink"
    ${EndIf}
    ReadRegStr $0 HKCU "$StartupKey" "FrameForge"
    ${If} $0 == '$\"$INSTDIR\FrameForge.exe$\" --background'
        DeleteRegValue HKCU "$StartupKey" "FrameForge"
    ${EndIf}
    Delete "$INSTDIR\FrameForge.exe"
    Delete "$INSTDIR\GettingStarted.txt"
    Delete "$INSTDIR\LICENSE"
    Delete "$INSTDIR\THIRD-PARTY-NOTICES.md"
    Delete "$INSTDIR\licenses\CsWinRT.txt"
    Delete "$INSTDIR\licenses\dotnet-desktop.txt"
    Delete "$INSTDIR\licenses\dotnet-runtime.txt"
    Delete "$INSTDIR\licenses\dotnet-third-party.txt"
    Delete "$INSTDIR\licenses\NAudio.txt"
    Delete "$INSTDIR\licenses\NSIS.txt"
    RMDir "$INSTDIR\licenses"
    Delete "$INSTDIR\install.ini"
    Delete "$INSTDIR\Uninstall.exe"
    ${If} $TestMode == "1"
        DeleteRegKey /ifempty HKCU "$StartupKey"
        DeleteRegKey /ifempty HKCU "$AppKey"
        RMDir "$INSTDIR\.test-shell\Desktop"
        RMDir "$INSTDIR\.test-shell\StartMenu"
        RMDir "$INSTDIR\.test-shell"
    ${EndIf}
    RMDir "$INSTDIR"
    ; %LOCALAPPDATA%\FrameForge is intentionally untouched.
SectionEnd
