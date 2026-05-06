!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"
!include "nsDialogs.nsh"

!ifndef APP_NAME
  !error "APP_NAME define is required"
!endif
!ifndef APP_ID
  !error "APP_ID define is required"
!endif
!ifndef APP_VERSION
  !error "APP_VERSION define is required"
!endif
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR define is required"
!endif
!ifndef OUTPUT_EXE
  !error "OUTPUT_EXE define is required"
!endif
!ifndef MAIN_EXE
  !error "MAIN_EXE define is required"
!endif
!ifndef APP_ICON
  !define APP_ICON ""
!endif
!ifndef APPDATA_DIR_NAME
  !define APPDATA_DIR_NAME "${APP_NAME}"
!endif
!ifndef APP_PUBLISHER
  !define APP_PUBLISHER "${APP_NAME}"
!endif
!ifndef PRODUCT_REG_KEY
  !define PRODUCT_REG_KEY "Software\${APP_ID}"
!endif
!ifndef INSTALL_DIR
  !define INSTALL_DIR "$LOCALAPPDATA\Programs\${APP_NAME}"
!endif

Unicode True
Name "${APP_NAME}"
Caption "${APP_NAME} ${APP_VERSION}"
UninstallCaption "${APP_NAME} ${APP_VERSION}"
OutFile "${OUTPUT_EXE}"
!if "${APP_ICON}" != ""
  Icon "${APP_ICON}"
  UninstallIcon "${APP_ICON}"
  !define MUI_ICON "${APP_ICON}"
  !define MUI_UNICON "${APP_ICON}"
!endif
InstallDir "${INSTALL_DIR}"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show

!define UNINSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"

Var DeleteAppDataCheckbox
Var DeleteAppDataCheckboxState
Var ExistingInstallDir
Var ExistingUninstaller

LangString DeleteAppDataText 1033 "Delete local app data (AppData\\Carton)"
LangString DeleteAppDataText 2052 "删除本地应用数据 (AppData\\Carton)"
LangString LaunchAfterInstallText 1033 "Launch ${APP_NAME} after setup"
LangString LaunchAfterInstallText 2052 "安装完成后启动 ${APP_NAME}"
LangString WelcomePageTitle 1033 "Welcome to ${APP_NAME} ${APP_VERSION} Setup"
LangString WelcomePageTitle 2052 "欢迎使用 ${APP_NAME} ${APP_VERSION} 安装向导"
LangString WelcomePageText 1033 "Setup will install ${APP_NAME} ${APP_VERSION} on your computer.$\r$\n$\r$\nClick Next to continue."
LangString WelcomePageText 2052 "安装程序将把 ${APP_NAME} ${APP_VERSION} 安装到你的电脑。$\r$\n$\r$\n点击下一步继续。"
LangString UpgradePageTitle 1033 "Upgrade ${APP_NAME}"
LangString UpgradePageTitle 2052 "升级 ${APP_NAME}"
LangString UpgradePageMessage 1033 "An existing installation was found. Setup will upgrade ${APP_NAME} in the existing location."
LangString UpgradePageMessage 2052 "检测到已安装的 ${APP_NAME}。安装程序将使用原安装位置进行升级。"
LangString UpgradePagePathLabel 1033 "Install location:"
LangString UpgradePagePathLabel 2052 "安装位置："
LangString InstallDirNotWritableText 1033 "The selected install directory is not writable by the current user.$\r$\n$\r$\nThis installer runs without administrator privileges. Please choose a user-writable directory, such as your local AppData Programs folder."
LangString InstallDirNotWritableText 2052 "当前用户无法写入所选安装目录。$\r$\n$\r$\n此安装器不会请求管理员权限。请选择当前用户可写的目录，例如本地 AppData Programs 目录。"
LangString RunningDuringInstallText 1033 "${APP_NAME} is currently running.$\r$\n$\r$\nYes: close it automatically and continue.$\r$\nNo: retry after closing it manually.$\r$\nCancel: abort setup."
LangString RunningDuringInstallText 2052 "${APP_NAME} 正在运行。$\r$\n$\r$\n是：自动关闭并继续。$\r$\n否：手动关闭后重试。$\r$\n取消：终止安装。"
LangString RunningDuringUninstallText 1033 "${APP_NAME} is currently running.$\r$\n$\r$\nYes: close it automatically and continue uninstall.$\r$\nNo: retry after closing it manually.$\r$\nCancel: abort uninstall."
LangString RunningDuringUninstallText 2052 "${APP_NAME} 正在运行。$\r$\n$\r$\n是：自动关闭并继续卸载。$\r$\n否：手动关闭后重试。$\r$\n取消：终止卸载。"

!define MUI_WELCOMEPAGE_TITLE "$(WelcomePageTitle)"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomePageText)"
!insertmacro MUI_PAGE_WELCOME
Page custom UpgradePageShow UpgradePageLeave
!define MUI_PAGE_CUSTOMFUNCTION_PRE DirectoryPre
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE InstallDirLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchAfterInstallText)"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchAfterInstall
!define MUI_FINISHPAGE_RUN_CHECKED
!insertmacro MUI_PAGE_FINISH

!define MUI_PAGE_CUSTOMFUNCTION_SHOW un.ConfirmShow
Function un.ConfirmShow
  ; Add a checkbox to the stock uninstall confirmation page.
  FindWindow $0 "#32770" "" $HWNDPARENT
  System::Call "user32::GetDpiForWindow(p r0) i .r1"
  IntOp $2 0 * $1
  IntOp $3 100 * $1
  IntOp $4 420 * $1
  IntOp $5 25 * $1
  IntOp $2 $2 / 96
  IntOp $3 $3 / 96
  IntOp $4 $4 / 96
  IntOp $5 $5 / 96
  System::Call 'user32::CreateWindowEx(i ${__NSD_CheckBox_EXSTYLE}, w "${__NSD_CheckBox_CLASS}", w "$(DeleteAppDataText)", i ${__NSD_CheckBox_STYLE}, i r2, i r3, i r4, i r5, p r0, i0, i0, i0) i .s'
  Pop $DeleteAppDataCheckbox
  SendMessage $HWNDPARENT ${WM_GETFONT} 0 0 $6
  SendMessage $DeleteAppDataCheckbox ${WM_SETFONT} $6 1
FunctionEnd

!define MUI_PAGE_CUSTOMFUNCTION_LEAVE un.ConfirmLeave
Function un.ConfirmLeave
  SendMessage $DeleteAppDataCheckbox ${BM_GETCHECK} 0 0 $DeleteAppDataCheckboxState
FunctionEnd

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  Call EnsureAppNotRunningForInstall
  Call RunExistingUninstaller

  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${PRODUCT_REG_KEY}" "" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${MAIN_EXE}"
SectionEnd

Section "Uninstall"
  Call un.EnsureAppNotRunningForUninstall

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINSTALL_REG_KEY}"
  DeleteRegKey HKCU "${PRODUCT_REG_KEY}"

  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"

  ${If} $DeleteAppDataCheckboxState = 1
    RMDir /r "$APPDATA\${APPDATA_DIR_NAME}"
    RMDir /r "$LOCALAPPDATA\${APPDATA_DIR_NAME}"
  ${EndIf}
SectionEnd

Function LaunchAfterInstall
  Exec '"$INSTDIR\${MAIN_EXE}"'
FunctionEnd

Function .onInit
  Call ResolveExistingInstall
FunctionEnd

Function ResolveExistingInstall
  StrCpy $ExistingInstallDir ""
  StrCpy $ExistingUninstaller ""

  ReadRegStr $0 HKCU "${UNINSTALL_REG_KEY}" "InstallLocation"
  ${If} $0 != ""
    StrCpy $ExistingInstallDir $0
    StrCpy $INSTDIR $0
  ${Else}
    ReadRegStr $0 HKCU "${PRODUCT_REG_KEY}" ""
    ${If} $0 != ""
      StrCpy $ExistingInstallDir $0
      StrCpy $INSTDIR $0
    ${EndIf}
  ${EndIf}

  ${If} $ExistingInstallDir != ""
    IfFileExists "$ExistingInstallDir\uninstall.exe" 0 +2
      StrCpy $ExistingUninstaller "$ExistingInstallDir\uninstall.exe"
  ${EndIf}
FunctionEnd

Function RunExistingUninstaller
  ${If} $ExistingUninstaller != ""
    ExecWait '"$ExistingUninstaller" /S _?=$ExistingInstallDir'
  ${Else}
    IfFileExists "$INSTDIR\uninstall.exe" 0 +2
      ExecWait '"$INSTDIR\uninstall.exe" /S _?=$INSTDIR'
  ${EndIf}
FunctionEnd

Function UpgradePageShow
  ${If} $ExistingInstallDir == ""
    Abort
  ${EndIf}

  !insertmacro MUI_HEADER_TEXT "$(UpgradePageTitle)" "$(UpgradePageMessage)"
  nsDialogs::Create 1018
  Pop $0

  ${NSD_CreateLabel} 0 0 100% 28u "$(UpgradePageMessage)"
  Pop $1

  ${NSD_CreateLabel} 0 46u 100% 12u "$(UpgradePagePathLabel)"
  Pop $1

  ${NSD_CreateText} 0 62u 100% 13u "$ExistingInstallDir"
  Pop $1
  EnableWindow $1 0

  nsDialogs::Show
FunctionEnd

Function UpgradePageLeave
FunctionEnd

Function DirectoryPre
  ${If} $ExistingInstallDir != ""
    Abort
  ${EndIf}
FunctionEnd

Function InstallDirLeave
  ClearErrors
  CreateDirectory "$INSTDIR"
  IfErrors install_dir_not_writable

  ClearErrors
  FileOpen $0 "$INSTDIR\.__carton_write_test.tmp" w
  IfErrors install_dir_not_writable
  FileWrite $0 "test"
  FileClose $0
  Delete "$INSTDIR\.__carton_write_test.tmp"
  Return

  install_dir_not_writable:
    MessageBox MB_ICONEXCLAMATION "$(InstallDirNotWritableText)"
    Abort
FunctionEnd

Function IsMainProcessRunning
  nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq ${MAIN_EXE}" /NH /FO CSV'
  Pop $0
  Pop $1

  Push "0"
  ${If} $0 == 0
    StrCpy $2 $1 1
    ${If} $2 == "$\""
      Pop $3
      Push "1"
    ${EndIf}
  ${EndIf}
FunctionEnd

Function EnsureAppNotRunningForInstall
  install_running_check:
    Call IsMainProcessRunning
    Pop $0
    ${If} $0 == "1"
      MessageBox MB_ICONEXCLAMATION|MB_YESNOCANCEL "$(RunningDuringInstallText)" /SD IDCANCEL IDYES install_force_close IDNO install_running_check
      Goto install_abort
      install_force_close:
        nsExec::ExecToLog 'taskkill /IM "${MAIN_EXE}" /T /F'
        Sleep 800
        Goto install_running_check
      install_abort:
        Abort
    ${EndIf}
FunctionEnd

Function un.IsMainProcessRunning
  nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq ${MAIN_EXE}" /NH /FO CSV'
  Pop $0
  Pop $1

  Push "0"
  ${If} $0 == 0
    StrCpy $2 $1 1
    ${If} $2 == "$\""
      Pop $3
      Push "1"
    ${EndIf}
  ${EndIf}
FunctionEnd

Function un.EnsureAppNotRunningForUninstall
  uninstall_running_check:
    Call un.IsMainProcessRunning
    Pop $0
    ${If} $0 == "1"
      MessageBox MB_ICONEXCLAMATION|MB_YESNOCANCEL "$(RunningDuringUninstallText)" /SD IDCANCEL IDYES uninstall_force_close IDNO uninstall_running_check
      Goto uninstall_abort
      uninstall_force_close:
        nsExec::ExecToLog 'taskkill /IM "${MAIN_EXE}" /T /F'
        Sleep 800
        Goto uninstall_running_check
      uninstall_abort:
        Abort
    ${EndIf}
FunctionEnd
