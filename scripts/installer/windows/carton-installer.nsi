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
!ifndef APPDATA_DIR_NAME
  !define APPDATA_DIR_NAME "${APP_NAME}"
!endif
!ifndef INSTALL_DIR
  !define INSTALL_DIR "$LOCALAPPDATA\Programs\${APP_NAME}"
!endif

Unicode True
Name "${APP_NAME}"
OutFile "${OUTPUT_EXE}"
InstallDir "${INSTALL_DIR}"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show

!define UNINSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define PRODUCT_REG_KEY "Software\${APP_ID}"

Var DeleteAppDataCheckbox
Var DeleteAppDataCheckboxState

LangString DeleteAppDataText 1033 "Delete local app data (AppData\\Carton)"
LangString DeleteAppDataText 2052 "删除本地应用数据 (AppData\\Carton)"
LangString LaunchAfterInstallText 1033 "Launch ${APP_NAME} after setup"
LangString LaunchAfterInstallText 2052 "安装完成后启动 ${APP_NAME}"
LangString RunningDuringInstallText 1033 "${APP_NAME} is currently running.$\r$\n$\r$\nYes: close it automatically and continue.$\r$\nNo: retry after closing it manually.$\r$\nCancel: abort setup."
LangString RunningDuringInstallText 2052 "${APP_NAME} 正在运行。$\r$\n$\r$\n是：自动关闭并继续。$\r$\n否：手动关闭后重试。$\r$\n取消：终止安装。"
LangString RunningDuringUninstallText 1033 "${APP_NAME} is currently running.$\r$\n$\r$\nYes: close it automatically and continue uninstall.$\r$\nNo: retry after closing it manually.$\r$\nCancel: abort uninstall."
LangString RunningDuringUninstallText 2052 "${APP_NAME} 正在运行。$\r$\n$\r$\n是：自动关闭并继续卸载。$\r$\n否：手动关闭后重试。$\r$\n取消：终止卸载。"

!insertmacro MUI_PAGE_WELCOME
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

  IfFileExists "$INSTDIR\uninstall.exe" 0 +2
    ExecWait '"$INSTDIR\uninstall.exe" /S _?=$INSTDIR'

  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${PRODUCT_REG_KEY}" "" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "Publisher" "${APP_NAME}"
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
