; 在向导模式中增加欢迎页；一键模式不会调用此宏。
!macro customWelcomePage
  !insertmacro MUI_PAGE_WELCOME
!macroend

; 应用文件解压后，复制脚本目录中的附加资源。
!macro customInstall
  SetOutPath "$INSTDIR"
  File /oname=安装说明.txt "${BUILD_RESOURCES_DIR}\安装说明.txt"
  DetailPrint "自定义安装步骤已完成。"
!macroend

; 默认文件清理前执行；需要时可在此清理自定义创建的注册表项。
!macro customUnInstall
  DetailPrint "正在执行自定义卸载步骤。"
!macroend
