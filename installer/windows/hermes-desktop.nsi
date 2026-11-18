Unicode True
ManifestDPIAware true
XPStyle on

!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!ifndef STAGE_DIR
  !define STAGE_DIR "..\..\dist\win-x64-stage"
!endif
!ifndef STAGE_PATTERN
  !define STAGE_PATTERN "${STAGE_DIR}/*"
!endif
!ifndef ICON_PATH
  !define ICON_PATH "..\..\src\HermesDesktop.App\Assets\hermes-desktop.ico"
!endif
!ifndef WELCOME_BMP
  !define WELCOME_BMP "welcome.bmp"
!endif
!ifndef HEADER_BMP
  !define HEADER_BMP "header.bmp"
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
ShowInstDetails nevershow
ShowUninstDetails nevershow

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 permails (MIT License)"
VIAddVersionKey "OriginalFilename" "HermesDesktop-Setup-${APP_VERSION}-win-x64.exe"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"

!define MUI_ICON "${ICON_PATH}"
!define MUI_UNICON "${ICON_PATH}"
!define MUI_ABORTWARNING
!define MUI_BGCOLOR "FFFFFF"

!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "${HEADER_BMP}"
!define MUI_HEADERIMAGE_UNBITMAP "${HEADER_BMP}"
!define MUI_HEADERIMAGE_BITMAP_STRETCH FitControl

!define MUI_WELCOMEFINISHPAGE_BITMAP "${WELCOME_BMP}"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${WELCOME_BMP}"
!define MUI_WELCOMEFINISHPAGE_BITMAP_STRETCH FitControl

!define MUI_WELCOMEPAGE_TITLE "$(WelcomeTitle)"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"

!define MUI_FINISHPAGE_TITLE "$(FinishTitle)"
!define MUI_FINISHPAGE_TEXT "$(FinishText)"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchAppText)"

!insertmacro MUI_PAGE_WELCOME
Page custom OptionsPageCreate OptionsPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

; Native Windows 11 typography instead of legacy Tahoma
SetFont /LANG=${LANG_SIMPCHINESE} "Microsoft YaHei UI" 9
SetFont /LANG=${LANG_ENGLISH} "Segoe UI" 9

; Localized UI Strings
LangString WelcomeTitle ${LANG_SIMPCHINESE} "安装 ${APP_NAME} ${APP_VERSION}"
LangString WelcomeTitle ${LANG_ENGLISH} "Install ${APP_NAME} ${APP_VERSION}"

LangString WelcomeText ${LANG_SIMPCHINESE} "直连自托管 Hermes 网关的轻量桌面客户端。$\r$\n$\r$\n点击「下一步」选择安装路径并开始安装，全程无需管理员权限。"
LangString WelcomeText ${LANG_ENGLISH} "A lightweight desktop client for self-hosted Hermes gateways.$\r$\n$\r$\nClick Next to choose your installation options and begin. No administrator privileges required."

LangString OptionsTitle ${LANG_SIMPCHINESE} "安装选项"
LangString OptionsTitle ${LANG_ENGLISH} "Installation Options"

LangString OptionsSubtitle ${LANG_SIMPCHINESE} "选择安装位置与快捷方式。"
LangString OptionsSubtitle ${LANG_ENGLISH} "Choose where to install and which shortcuts to create."

LangString InstallDirLabel ${LANG_SIMPCHINESE} "安装位置"
LangString InstallDirLabel ${LANG_ENGLISH} "Install location"

LangString BrowseBtnText ${LANG_SIMPCHINESE} "浏览..."
LangString BrowseBtnText ${LANG_ENGLISH} "Browse..."

LangString DesktopChkText ${LANG_SIMPCHINESE} "创建桌面快捷方式"
LangString DesktopChkText ${LANG_ENGLISH} "Create Desktop shortcut"

LangString StartMenuChkText ${LANG_SIMPCHINESE} "创建开始菜单快捷方式"
LangString StartMenuChkText ${LANG_ENGLISH} "Create Start Menu shortcuts"

LangString PerUserNote ${LANG_SIMPCHINESE} "将安装到当前用户目录，支持在 Windows「设置 → 应用」中一键卸载。"
LangString PerUserNote ${LANG_ENGLISH} "Installs for the current user only. You can uninstall anytime from Windows Settings -> Apps."

LangString InstallBtnText ${LANG_SIMPCHINESE} "安装 (&I)"
LangString InstallBtnText ${LANG_ENGLISH} "&Install"

LangString FinishTitle ${LANG_SIMPCHINESE} "${APP_NAME} 安装完成"
LangString FinishTitle ${LANG_ENGLISH} "${APP_NAME} is ready"

LangString FinishText ${LANG_SIMPCHINESE} "${APP_NAME} ${APP_VERSION} 已成功安装到你的电脑。$\r$\n$\r$\n点击「完成」关闭向导。"
LangString FinishText ${LANG_ENGLISH} "${APP_NAME} ${APP_VERSION} has been installed on your computer.$\r$\n$\r$\nClick Finish to close setup."

LangString LaunchAppText ${LANG_SIMPCHINESE} "立即启动 ${APP_NAME}"
LangString LaunchAppText ${LANG_ENGLISH} "Launch ${APP_NAME} now"

LangString RunningAppWarn ${LANG_SIMPCHINESE} "检测到 ${APP_NAME} 正在运行，请先关闭应用后再点击「重试」继续。"
LangString RunningAppWarn ${LANG_ENGLISH} "${APP_NAME} is currently running. Please close it and click Retry to continue."

LangString WebView2MissingWarn ${LANG_SIMPCHINESE} "当前系统未检测到 Microsoft Edge WebView2 运行时（用于部分内容预览）。$\r$\n是否立即打开微软官方下载页面获取安装程序？"
LangString WebView2MissingWarn ${LANG_ENGLISH} "Microsoft Edge WebView2 Runtime was not detected.$\r$\nOpen the official Microsoft download page now?"

LangString RemoveDataPrompt ${LANG_SIMPCHINESE} "是否同时删除本地用户数据（已保存的服务器连接、凭据和本地配置）？$\r$\n位置：$LOCALAPPDATA\HermesDesktop"
LangString RemoveDataPrompt ${LANG_ENGLISH} "Also remove local user data (saved server profiles, credentials, and settings)?$\r$\nLocation: $LOCALAPPDATA\HermesDesktop"

LangString InstallingStatus ${LANG_SIMPCHINESE} "正在安装 ${APP_NAME}，请稍候..."
LangString InstallingStatus ${LANG_ENGLISH} "Installing ${APP_NAME}, please wait..."

Var DialogOptions
Var DirInput
Var DirBrowseBtn
Var DesktopCheck
Var StartMenuCheck
Var CreateDesktopShortcut
Var CreateStartMenuShortcut

Function OptionsPageCreate
  !insertmacro MUI_HEADER_TEXT "$(OptionsTitle)" "$(OptionsSubtitle)"

  nsDialogs::Create 1018
  Pop $DialogOptions
  ${If} $DialogOptions == error
    Abort
  ${EndIf}

  ; Set button text to "Install" right on this unified options page
  GetDlgItem $0 $HWNDPARENT 1
  SendMessage $0 ${WM_SETTEXT} 0 "STR:$(InstallBtnText)"

  ${NSD_CreateLabel} 0 4u 100% 12u "$(InstallDirLabel)"
  Pop $0

  ${NSD_CreateText} 0 18u 228u 15u "$INSTDIR"
  Pop $DirInput

  ${NSD_CreateButton} 234u 17u 62u 17u "$(BrowseBtnText)"
  Pop $DirBrowseBtn
  ${NSD_OnClick} $DirBrowseBtn OnBrowseClick

  ${NSD_CreateCheckbox} 0 48u 100% 13u "$(DesktopChkText)"
  Pop $DesktopCheck
  ${If} $CreateDesktopShortcut == ${BST_CHECKED}
    ${NSD_Check} $DesktopCheck
  ${EndIf}

  ${NSD_CreateCheckbox} 0 66u 100% 13u "$(StartMenuChkText)"
  Pop $StartMenuCheck
  ${If} $CreateStartMenuShortcut == ${BST_CHECKED}
    ${NSD_Check} $StartMenuCheck
  ${EndIf}

  ${NSD_CreateLabel} 0 100u 100% 24u "$(PerUserNote)"
  Pop $0

  nsDialogs::Show
FunctionEnd

Function OnBrowseClick
  ${NSD_GetText} $DirInput $0
  nsDialogs::SelectFolderDialog "$(InstallDirLabel)" "$0"
  Pop $1
  ${If} $1 != error
    ${NSD_SetText} $DirInput "$1"
  ${EndIf}
FunctionEnd

Function OptionsPageLeave
  ${NSD_GetText} $DirInput $0
  ${If} $0 != ""
    StrCpy $INSTDIR "$0"
  ${EndIf}
  ${NSD_GetState} $DesktopCheck $CreateDesktopShortcut
  ${NSD_GetState} $StartMenuCheck $CreateStartMenuShortcut
FunctionEnd

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
  StrCpy $CreateDesktopShortcut ${BST_CHECKED}
  StrCpy $CreateStartMenuShortcut ${BST_CHECKED}
  Call EnsureAppClosed
FunctionEnd

Function un.onInit
  Call un.EnsureAppClosed
FunctionEnd

Section "Install" SecMain
  SetDetailsPrint textonly
  DetailPrint "$(InstallingStatus)"
  SetDetailsPrint none

  SetOutPath "$INSTDIR"
  SetOverwrite on

  File /r "${STAGE_PATTERN}"

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
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" 115712

  ${If} $CreateDesktopShortcut == ${BST_CHECKED}
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  ${EndIf}

  ${If} $CreateStartMenuShortcut == ${BST_CHECKED}
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0
  ${EndIf}

  Call CheckWebView2
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"

  RMDir /r "$INSTDIR"

  IfSilent skip_data
  MessageBox MB_YESNO|MB_ICONQUESTION "$(RemoveDataPrompt)" /SD IDNO IDNO skip_data
    RMDir /r "$LOCALAPPDATA\HermesDesktop"
  skip_data:
SectionEnd
