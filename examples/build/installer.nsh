; 隐藏执行 JSON 读写脚本，路径作为参数传递，不拼入 PowerShell 表达式。
!macro DemoDataConfig Mode
  nsExec::Exec '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\data-directory.ps1" -Mode ${Mode} -ConfigPath "$INSTDIR\data-directory.json" -ValueFile "$PLUGINSDIR\data-directory.txt"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "无法读写数据目录 JSON 配置。" /SD IDOK
    SetErrorLevel 2
    Abort "无法读写数据目录 JSON 配置。"
  ${EndIf}
!macroend

!ifndef BUILD_UNINSTALLER
  ; 数据目录独立于程序安装目录，返回上一页时保留用户选择。
  Var DemoDataDirectory
  ; 记录上次默认目录，安装目录改变时同步尚未手动修改的数据路径。
  Var DemoDefaultDataDirectory

  ; 升级前读取旧配置，避免旧版卸载清理配置后丢失数据路径。
  !macro customInit
    InitPluginsDir
    File /oname=$PLUGINSDIR\data-directory.ps1 "${BUILD_RESOURCES_DIR}\data-directory.ps1"
    ${If} ${FileExists} "$INSTDIR\data-directory.json"
      !insertmacro DemoDataConfig Read
      ClearErrors
      FileOpen $0 "$PLUGINSDIR\data-directory.txt" r
      FileReadUTF16LE $0 $DemoDataDirectory
      FileClose $0
      ${If} ${Errors}
        MessageBox MB_OK|MB_ICONSTOP "无法读取已有数据目录。" /SD IDOK
        SetErrorLevel 2
        Abort "无法读取已有数据目录。"
      ${EndIf}
    ${EndIf}
  !macroend

  ; 在安装目录页之后插入数据目录页，使用原生输入框和浏览按钮。
  !macro customPageAfterChangeDir
    ; 上游为安装进度页预留了回调，插入新页面时先保存，随后恢复。
    !ifdef MUI_PAGE_CUSTOMFUNCTION_PRE
      !define DEMO_NEXT_PAGE_PRE "${MUI_PAGE_CUSTOMFUNCTION_PRE}"
      !undef MUI_PAGE_CUSTOMFUNCTION_PRE
    !endif
    !define MUI_PAGE_CUSTOMFUNCTION_PRE DemoDataDirectoryPre
    !define MUI_PAGE_HEADER_TEXT "数据存储文件夹"
    !define MUI_PAGE_HEADER_SUBTEXT "您想将数据存储到何处？"
    !define MUI_DIRECTORYPAGE_TEXT_TOP "数据将存储到下列文件夹中。要选择不同的位置，请输入新的路径，或单击“浏览”选择其他文件夹。"
    !define MUI_DIRECTORYPAGE_TEXT_DESTINATION "将数据存储到："
    !define MUI_DIRECTORYPAGE_VARIABLE $DemoDataDirectory
    !insertmacro MUI_PAGE_DIRECTORY

    ; 在计算默认数据路径前，先执行上游的安装目录规范化。
    Function DemoDataDirectoryPre
      !ifdef DEMO_NEXT_PAGE_PRE
        Call ${DEMO_NEXT_PAGE_PRE}
      !endif
      ${If} $DemoDataDirectory == ""
      ${OrIf} $DemoDataDirectory == $DemoDefaultDataDirectory
        StrCpy $DemoDataDirectory "$INSTDIR\Data"
      ${EndIf}
      StrCpy $DemoDefaultDataDirectory "$INSTDIR\Data"
    FunctionEnd

    !ifdef DEMO_NEXT_PAGE_PRE
      !define MUI_PAGE_CUSTOMFUNCTION_PRE "${DEMO_NEXT_PAGE_PRE}"
      !undef DEMO_NEXT_PAGE_PRE
    !endif
  !macroend
!endif

; 在向导模式中增加欢迎页；一键模式不会调用此宏。
!macro customWelcomePage
  !insertmacro MUI_PAGE_WELCOME
!macroend

; 应用文件解压后，复制脚本目录中的附加资源。
!macro customInstall
  ; 静默和一键安装不显示数据页，未保存过路径时使用默认值。
  ${If} $DemoDataDirectory == ""
    StrCpy $DemoDataDirectory "$INSTDIR\Data"
  ${EndIf}
  ClearErrors
  CreateDirectory "$DemoDataDirectory"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "无法创建数据目录：$DemoDataDirectory" /SD IDOK
    Abort "无法创建数据目录。"
  ${EndIf}
  ; 使用 Unicode 中间文件传递路径，保留中文及特殊字符。
  FileOpen $0 "$PLUGINSDIR\data-directory.txt" w
  FileWriteUTF16LE /BOM $0 "$DemoDataDirectory"
  FileClose $0
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "无法保存数据目录配置。" /SD IDOK
    Abort "无法保存数据目录配置。"
  ${EndIf}
  !insertmacro DemoDataConfig Write
  SetOutPath "$INSTDIR"
  File /oname=安装说明.txt "${BUILD_RESOURCES_DIR}\安装说明.txt"
  DetailPrint "自定义安装步骤已完成。"
!macroend

; 默认文件清理前执行；需要时可在此清理自定义创建的注册表项。
!macro customUnInstall
  DetailPrint "正在执行自定义卸载步骤。"
!macroend
