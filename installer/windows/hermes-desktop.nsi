Unicode True

!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!ifndef STAGE_DIR
  !define STAGE_DIR "..\..\dist\win-x64-stage"
!endif
!ifndef ICON_PATH
  !define ICON_PATH "..\..\src\HermesDesktop.App\Assets\hermes-desktop.ico"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "..\..\dist\HermesDesktop-Setup-${APP_VERSION}-win-x64.exe"
!endif

!define APP_NAME "Hermes Desktop"
!define APP_EXE "HermesDesktop.exe"
!define APP_PUBLISHER "Hermes Desktop"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HermesDesktop"
!define WEBVIEW2_GUID "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
!define WEBVIEW2_URL "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\HermesDesktop"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "OriginalFilename" "HermesDesktop-Setup-${APP_VERSION}-win-x64.exe"

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ICON "${ICON_PATH}"
!define MUI_UNICON "${ICON_PATH}"
!define MUI_ABORTWARNING

!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchAppText)"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

; Localized strings
LangString SecCoreName ${LANG_SIMPCHINESE} "Hermes Desktop 主程序（必需）"
LangString SecCoreName ${LANG_ENGLISH} "Hermes Desktop Core Application (Required)"

LangString SecDesktopName ${LANG_SIMPCHINESE} "创建桌面快捷方式"
LangString SecDesktopName ${LANG_ENGLISH} "Create Desktop Shortcut"

LangString SecStartMenuName ${LANG_SIMPCHINESE} "创建开始菜单快捷方式"
LangString SecStartMenuName ${LANG_ENGLISH} "Create Start Menu Shortcuts"

LangString LaunchAppText ${LANG_SIMPCHINESE} "立即启动 Hermes Desktop"
LangString LaunchAppText ${LANG_ENGLISH} "Launch Hermes Desktop now"

LangString RunningAppWarn ${LANG_SIMPCHINESE} "检测到 Hermes Desktop 正在运行，请先关闭应用后再点击「重试」继续。"
LangString RunningAppWarn ${LANG_ENGLISH} "Hermes Desktop is currently running. Please close it and click Retry to continue."

LangString WebView2MissingWarn ${LANG_SIMPCHINESE} "当前系统未检测到 Microsoft Edge WebView2 运行时（用于显示公式与富文本内容）。$\r$\n是否立即打开微软官方下载页面获取 WebView2 安装程序？"
LangString WebView2MissingWarn ${LANG_ENGLISH} "Microsoft Edge WebView2 Runtime was not detected (required for formula and rich-text rendering).$\r$\nOpen the official Microsoft download page now?"

LangString RemoveDataPrompt ${LANG_SIMPCHINESE} "是否同时删除本地用户数据（已保存的服务器连接、凭据和本地配置）？$\r$\n位置：$LOCALAPPDATA\HermesDesktop"
LangString RemoveDataPrompt ${LANG_ENGLISH} "Also remove local user data (saved server profiles, credentials, and settings)?$\r$\nLocation: $LOCALAPPDATA\HermesDesktop"

Function EnsureAppClosed
  loop_check:
    nsExec::ExecToStack 'cmd.exe /C tasklist /FI "IMAGENAME eq ${APP_EXE}" | find /I "${APP_EXE}"'
    Pop $0
    ${If} $0 == 0
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(RunningAppWarn)" IDRETRY loop_check
      Abort
    ${EndIf}
FunctionEnd

Function un.EnsureAppClosed
  loop_uncheck:
    nsExec::ExecToStack 'cmd.exe /C tasklist /FI "IMAGENAME eq ${APP_EXE}" | find /I "${APP_EXE}"'
    Pop $0
    ${If} $0 == 0
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(RunningAppWarn)" IDRETRY loop_uncheck
      Abort
    ${EndIf}
FunctionEnd

Function CheckWebView2
  Push $0
  SetRegView 32
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\EdgeUpdate\Clients\${WEBVIEW2_GUID}" "pv"
  ${If} $0 == ""
    SetRegView 64
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\EdgeUpdate\Clients\${WEBVIEW2_GUID}" "pv"
  ${EndIf}
  ${If} $0 == ""
    ReadRegStr $0 HKCU "Software\Microsoft\EdgeUpdate\Clients\${WEBVIEW2_GUID}" "pv"
  ${EndIf}
  ${If} $0 == ""
    MessageBox MB_YESNO|MB_ICONINFORMATION "$(WebView2MissingWarn)" IDNO done_wv2
    ExecShell "open" "${WEBVIEW2_URL}"
  ${EndIf}
  done_wv2:
  SetRegView 32
  Pop $0
FunctionEnd

Function .onInit
  Call EnsureAppClosed
FunctionEnd

Function un.onInit
  Call un.EnsureAppClosed
FunctionEnd

Section "$(SecCoreName)" SecCore
  SectionIn RO
  SetOutPath "$INSTDIR"
  SetOverwrite on

  File /r "${STAGE_DIR}/*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Register per-user uninstall entry in Windows Settings -> Apps
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  ; Estimated size in KB (~115 MB)
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" 115712

  Call CheckWebView2
SectionEnd

Section "$(SecDesktopName)" SecDesktop
  SetOutPath "$INSTDIR"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

Section "$(SecStartMenuName)" SecStartMenu
  SetOutPath "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"

  ; Remove installed files cleanly
  RMDir /r "$INSTDIR"

  ; Optionally remove user profile/credential data (default No in silent mode)
  IfSilent skip_data
  MessageBox MB_YESNO|MB_ICONQUESTION "$(RemoveDataPrompt)" /SD IDNO IDNO skip_data
    RMDir /r "$LOCALAPPDATA\HermesDesktop"
  skip_data:
SectionEnd
