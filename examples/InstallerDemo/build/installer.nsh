!include LogicLib.nsh
!include WordFunc.nsh
!include FileFunc.nsh

!define INSTALLER_DEMO_ARTIFACTS "${BUILD_RESOURCES_DIR}\..\..\..\artifacts\installer-demo"
!define INSTALLER_DEMO_SETTINGS "Neuracle.InstallerDemo.View\Conf\System\System\SystemSetting.json"

; 展示辅助程序的诊断和恢复路径，便于定位失败原因。
!macro customHeader
  ShowInstDetails show
  ShowUninstDetails show
!macroend

; 安装及卸载均用自包含辅助程序检查五个进程，不终止运行中的软件。
!macro customCheckAppRunning
  InitPluginsDir
  File /oname=$PLUGINSDIR\InstallerDemoSupport.exe "${INSTALLER_DEMO_ARTIFACTS}\support\InstallerDemo.exe"
  nsExec::ExecToLog '"$PLUGINSDIR\InstallerDemoSupport.exe" check'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "请关闭 InstallerDemo 主程序、主控、模拟器、显示器校准和 VEP 刺激程序后重试。详情见安装日志。" /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
!macroend

!ifndef BUILD_UNINSTALLER
  ; 安装状态与旧路径用于锁定升级位置。
  Var InstallerDemoMode
  Var InstallerDemoOldLocation
  Var InstallerDemoData
  ; 返回上一页修改安装目录时，只同步尚未手动修改的数据默认值。
  Var InstallerDemoDefaultData
  Var InstallerDemoRecovery

  ; 初始化阶段判断版本，使用独立 Demo GUID，不接管真实 SetupFactory 安装。
  !macro customInit
    StrCpy $InstallerDemoMode "FRESH"
    ReadRegStr $1 SHELL_CONTEXT "${UNINSTALL_REGISTRY_KEY}" DisplayVersion
    ReadRegStr $InstallerDemoOldLocation SHELL_CONTEXT "${INSTALL_REGISTRY_KEY}" InstallLocation
    ${If} $1 != ""
      ${VersionCompare} $1 "${VERSION}" $0
      ${If} $0 == 1
        MessageBox MB_OK|MB_ICONSTOP "已安装版本 $1 高于本包 ${VERSION}，拒绝降级。" /SD IDOK
        SetErrorLevel 2
        Quit
      ${EndIf}
      ${If} $InstallerDemoOldLocation == ""
        MessageBox MB_OK|MB_ICONSTOP "旧安装注册信息缺少安装路径，请先修复注册信息或卸载旧版。" /SD IDOK
        SetErrorLevel 2
        Quit
      ${EndIf}
      StrCpy $INSTDIR $InstallerDemoOldLocation
      StrCpy $InstallerDemoMode "UPGRADE"
      ${If} $0 == 0
        StrCpy $InstallerDemoMode "REPAIR"
      ${EndIf}
    ${EndIf}
    !insertmacro customCheckAppRunning
  !macroend

  ; 自定义安装目录页精确使用所选位置，升级/修复跳过两个目录页。
  !macro customWelcomePage
    !insertmacro MUI_PAGE_WELCOME
    !define MUI_PAGE_CUSTOMFUNCTION_PRE InstallerDemoInstallDirectoryPre
    !insertmacro MUI_PAGE_DIRECTORY
    Function InstallerDemoInstallDirectoryPre
      ${If} $InstallerDemoMode != "FRESH"
        Abort
      ${EndIf}
    FunctionEnd
  !macroend

  ; 新装的数据目录默认在安装根目录，受文件清单卸载保护。
  !macro customPageAfterChangeDir
    !define MUI_PAGE_CUSTOMFUNCTION_PRE InstallerDemoDataPre
    !define MUI_PAGE_HEADER_TEXT "数据存储文件夹"
    !define MUI_PAGE_HEADER_SUBTEXT "配置、Data 和 Log 可写；程序目录保留默认权限。"
    !define MUI_DIRECTORYPAGE_TEXT_TOP "请选择本地数据目录。升级或修复保留原有 DataPath。"
    !define MUI_DIRECTORYPAGE_VARIABLE $InstallerDemoData
    !insertmacro MUI_PAGE_DIRECTORY
    Function InstallerDemoDataPre
      ${If} $InstallerDemoMode != "FRESH"
        Abort
      ${EndIf}
      ${If} $InstallerDemoData == ""
      ${OrIf} $InstallerDemoData == $InstallerDemoDefaultData
        StrCpy $InstallerDemoData "$INSTDIR\Data"
      ${EndIf}
      StrCpy $InstallerDemoDefaultData "$INSTDIR\Data"
    FunctionEnd
  !macroend

  ; 在旧版卸载前再次检查和持久备份，失败不进入卸载流程。
  !macro customBeforeInstall
    ${If} $InstallerDemoMode != "FRESH"
      StrCpy $INSTDIR $InstallerDemoOldLocation
    ${EndIf}
    ${If} $InstallerDemoData == ""
      StrCpy $InstallerDemoData "$INSTDIR\Data"
    ${EndIf}
    GetTempFileName $InstallerDemoRecovery "$TEMP"
    Delete "$InstallerDemoRecovery"
    StrCpy $InstallerDemoRecovery "$InstallerDemoRecovery-installer-recovery"
    nsExec::ExecToLog '"$PLUGINSDIR\InstallerDemoSupport.exe" prepare "$INSTDIR\." "$InstallerDemoRecovery\." "$InstallerDemoMode" "$InstallerDemoData\."'
    Pop $0
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "安装前检查或配置备份失败，已停止。恢复记录：$InstallerDemoRecovery" /SD IDOK
      SetErrorLevel 2
      Quit
    ${EndIf}
    DetailPrint "安装模式：$InstallerDemoMode；恢复记录：$InstallerDemoRecovery"
  !macroend

  ; 所有关键操作在完成页前执行，静默安装也执行并返回错误码。
  !macro customInstall
    nsExec::ExecToLog '"$PLUGINSDIR\InstallerDemoSupport.exe" complete "$INSTDIR\." "$InstallerDemoRecovery\." "$InstallerDemoMode" "$InstallerDemoData\."'
    Pop $0
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "配置恢复或目录授权失败。安装未完成，请保留恢复记录：$InstallerDemoRecovery" /SD IDOK
      SetErrorLevel 2
      Quit
    ${EndIf}
    ; 将持久恢复目录记录在安装目录中，便于查看配置备份。
    WriteINIStr "$INSTDIR\installer-demo.ini" "Install" "Mode" "$InstallerDemoMode"
    WriteINIStr "$INSTDIR\installer-demo.ini" "Install" "Recovery" "$InstallerDemoRecovery"
  !macroend
!endif

; 仅删除构建清单中的程序文件，保留配置、数据、日志和用户创建的其他文件。
!macro customRemoveFiles
  SetOutPath $TEMP
  !include "${INSTALLER_DEMO_ARTIFACTS}\remove-files.nsh"
  Delete "$INSTDIR\installer-demo.ini"
  Delete "$INSTDIR\${UNINSTALL_FILENAME}"
  RMDir "$INSTDIR"
!macroend
