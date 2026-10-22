!define NSQUICKJS_PLUGIN_DIR "${BUILD_RESOURCES_DIR}\nsQuickJs\Plugins"
!include "${BUILD_RESOURCES_DIR}\nsQuickJs\Include\nsQuickJs.nsh"
!include StrFunc.nsh
${StrRep}

; 插件状态和结果使用独立变量，避免覆盖循环位置。
Var DemoJsStatus
Var DemoJsResult
; 跨调用传递目录文本和分块偏移。
Var DemoText
Var DemoOffset
Var DemoChunk
; 保存释放到当前安装目录的临时脚本路径。
Var DemoScriptFile

; 插件错误中止安装，并在离开前释放上下文。
!macro DemoRequireJs
  ${If} $DemoJsStatus != "0"
    DetailPrint "nsQuickJs：$DemoJsStatus / $DemoJsResult"
    ${If} $DemoScriptFile != ""
      Delete "$DemoScriptFile"
    ${EndIf}
    nsQuickJs::Destroy /NOUNLOAD
    Pop $DemoJsStatus
    Pop $DemoJsResult
    MessageBox MB_OK|MB_ICONSTOP "无法读写数据目录 JSON 配置。" /SD IDOK
    SetErrorLevel 2
    Abort "无法读写数据目录 JSON 配置。"
  ${EndIf}
!macroend

; 对最多 128 字符的输入块做 JSON 转义，路径始终作为数据传递。
Function DemoAppendText
  Push $0
  Push $1
  Push $2
  !insertmacro NsQuickJs_Call "" "resetInput" "[]" $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
  StrCpy $DemoOffset 0
  ${Do}
    StrCpy $DemoChunk $DemoText 128 $DemoOffset
    ${If} $DemoChunk == ""
      ${ExitDo}
    ${EndIf}
    ; 分块边界不能拆开 UTF-16 代理对，否则插件会拒绝半个字符。
    StrLen $2 $DemoChunk
    StrCpy $0 $DemoChunk 1 -1
    System::Call '*(&w1 r0) p.r1'
    System::Call '*$1(&i2 .r0)'
    System::Free $1
    ${If} $0 >= 0xD800
    ${AndIf} $0 <= 0xDBFF
      IntOp $2 $2 - 1
      StrCpy $DemoChunk $DemoChunk $2
    ${EndIf}
    ${If} $2 == 0
      StrCpy $DemoJsStatus "E_ENCODING"
      StrCpy $DemoJsResult "目录包含不完整的 Unicode 字符。"
      !insertmacro DemoRequireJs
    ${EndIf}
    ${StrRep} $DemoChunk $DemoChunk '\' '\\'
    ${StrRep} $DemoChunk $DemoChunk '$\"' '\$\"'
    ${StrRep} $DemoChunk $DemoChunk '$\r' '\r'
    ${StrRep} $DemoChunk $DemoChunk '$\n' '\n'
    ${StrRep} $DemoChunk $DemoChunk '$\t' '\t'
    !insertmacro NsQuickJs_Call "" "appendText" '["$DemoChunk"]' $DemoJsStatus $DemoJsResult
    !insertmacro DemoRequireJs
    IntOp $DemoOffset $DemoOffset + $2
  ${Loop}
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

; 每次读写按当前安装目录创建上下文，避免用户改目录后仍写入旧位置。
Function DemoInitJs
  StrCpy $DemoScriptFile ""
  ${StrRep} $DemoText $INSTDIR '\' '/'
  ${StrRep} $DemoText $DemoText '$\"' '\$\"'
  StrLen $DemoOffset $DemoText
  ${If} $DemoOffset > 900
    StrCpy $DemoJsStatus "E_PATH"
    StrCpy $DemoJsResult "安装目录过长。"
    !insertmacro DemoRequireJs
  ${EndIf}
  !insertmacro NsQuickJs_Init '{"rootDir":"$DemoText","allowFileAccess":true}' $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
  ; 脚本加载也受 rootDir 约束，使用唯一临时文件，加载后立即删除。
  ClearErrors
  GetTempFileName $DemoScriptFile "$INSTDIR"
  ${IfNot} ${Errors}
    File /oname=$DemoScriptFile "${BUILD_RESOURCES_DIR}\data-directory.js"
  ${EndIf}
  ${If} ${Errors}
    StrCpy $DemoJsStatus "E_IO"
    StrCpy $DemoJsResult "无法准备数据目录脚本。"
    !insertmacro DemoRequireJs
  ${EndIf}
  !insertmacro NsQuickJs_EvalFile "$DemoScriptFile" $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
  Delete "$DemoScriptFile"
  StrCpy $DemoScriptFile ""
FunctionEnd

; JS 读取旧配置，NSIS 只接收目录供页面显示；缺失配置由 JS 处理。
Function DemoReadDataConfig
  Call DemoInitJs
  !insertmacro NsQuickJs_Call "" "readConfig" "[]" $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
  StrCpy $DemoDataDirectory ""
  StrCpy $DemoOffset 0
  ${Do}
    !insertmacro NsQuickJs_Call "" "directoryChunk" '[$DemoOffset]' $DemoJsStatus $DemoJsResult
    !insertmacro DemoRequireJs
    ; 合法路径已去除反斜杠，只需去掉结果两端的 JSON 引号。
    StrCpy $DemoChunk $DemoJsResult -1 1
    ${If} $DemoChunk == ""
      ${ExitDo}
    ${EndIf}
    StrCpy $DemoDataDirectory "$DemoDataDirectory$DemoChunk"
    IntOp $DemoOffset $DemoOffset + 128
  ${Loop}
  ${StrRep} $DemoDataDirectory $DemoDataDirectory '/' '\'
  !insertmacro NsQuickJs_Destroy $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
FunctionEnd

; 将页面选择传给 JS，由 JS 完成校验及 JSON 文件写入。
Function DemoWriteDataConfig
  Call DemoInitJs
  StrCpy $DemoText $DemoDataDirectory
  Call DemoAppendText
  !insertmacro NsQuickJs_Call "" "writeConfig" "[]" $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
  !insertmacro NsQuickJs_Destroy $DemoJsStatus $DemoJsResult
  !insertmacro DemoRequireJs
FunctionEnd
