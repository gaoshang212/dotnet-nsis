# dotnet-nsis

[English](README.md) | **简体中文**

用 C# 编写的 Windows 安装包 CLI。读取 YAML，把已发布的应用目录打成 NSIS `.exe` 安装包。

项目采用 [MIT 许可证](LICENSE)。第三方组件保留各自许可，详见 [第三方资源说明](THIRD-PARTY-NOTICES.md)。

复用固定版本的 electron-builder NSIS 模板。运行时无需 Node.js，也无需手动安装 NSIS 或 7-Zip。首次构建下载工具，后续复用本机缓存。

## 从 NuGet 安装

版本发布到 NuGet.org 后，在安装了 .NET 8 SDK 的 Windows x64 上执行：

```powershell
dotnet tool install --global dotnet-nsis
dotnet-nsis --help
# 更新已安装的工具。
dotnet tool update --global dotnet-nsis
```

NuGet 包以 .NET 8 为运行基线，包含模板和许可证。
此包是 CLI 工具，不作为 `PackageReference` 类库使用。本地打包及发布配置见
[NuGet 分发说明](docs/build.md#nuget-distribution)。

## 快速开始

从源码开发使用 .NET 10 SDK，并安装 .NET 8 运行时来运行 CLI 和测试；仅使用 NuGet 工具需要 .NET 8 即可。打包主机使用 Windows x64。安装包可以面向 x64、x86 或 ARM64，每次构建一种架构。

```powershell
dotnet build
dotnet run --project src/DotnetNsis -- init
# 编辑 nsis.yml，填入应用发布目录和启动程序。
dotnet run --project src/DotnetNsis -- validate
dotnet run --project src/DotnetNsis -- build
```

CLI 通过 NuGet 分发。CLI 不会编译被打包的应用，也不会安装其 .NET 运行时；应用本身需要自包含发布，或由目标电脑预先准备运行时。

## 命令行

参数解析、帮助和版本输出由 `System.CommandLine 2.0.13` 提供。原有命令保持兼容，`--config` 可简写为 `-c`，也支持等号写法；省略时读取当前目录的 `nsis.yml`。

```powershell
dotnet-nsis --help
dotnet-nsis build --help
dotnet-nsis --version
dotnet-nsis init -c ./nsis.yml
dotnet-nsis validate --config=./nsis.yml
dotnet-nsis build -c "./配置目录/应用配置.yml"
```

无参数时显示帮助。未知命令、未知选项或选项缺值返回 `2`，不会执行文件写入或构建。配置选项也可放在子命令之前，如 `dotnet-nsis -c ./nsis.yml build`。

## 配置

```yaml
appId: com.example.myapp
productName: 我的应用
version: 1.0.0
publisher: 示例公司
appDirectory: ./publish
executable: bin/MyApp.exe
outputDirectory: ./dist
arch: x64
nsis:
  installerLanguages: [zh_CN, en_US]
  displayLanguageSelector: false
  # include: ./build/installer.nsh
  # installerIcon: ./build/installer.ico
  # uninstallerIcon: ./build/uninstaller.ico
  oneClick: false
  perMachine: false
  allowToChangeInstallationDirectory: true
  createDesktopShortcut: true
  createStartMenuShortcut: true
  runAfterFinish: false
```

| 字段 | 规则或默认值 |
| --- | --- |
| `appId` | 必填，稳定应用标识；升级时保持不变。允许字母、数字、点、下划线、连字符。 |
| `productName` | 必填，安装界面显示名。文件名、快捷方式名和默认目录名会替换 Windows 非法字符。 |
| `version` | 必填，三段或四段数字，每段 0～65535，例如 `1.2.3` 或 `1.2.3.4`。Windows 资源中的三段版本自动补 `.0`。 |
| `publisher` | 可选，默认空。 |
| `appDirectory` | 必填，包含完整应用文件的目录。 |
| `executable` | 必填，相对应用目录的 `.exe` 路径，支持子目录。 |
| `outputDirectory` | 默认 `./dist`，不得等于或位于应用目录内。 |
| `arch` | `x64`、`x86` 或 `arm64`，默认 `x64`；应与应用发布架构一致。 |
| `nsis.installerLanguages` | 默认 `[zh_CN]`；按顺序指定安装语言，例如 `[zh_CN, en_US]`。首项为系统语言不匹配时的回退语言。 |
| `nsis.displayLanguageSelector` | 默认 `false`；设为 `true` 且配置多种语言时，在非静默安装中显示语言选择框。 |
| `nsis.oneClick` | 默认 `false`：安装向导；设为 `true`：一键安装。 |
| `nsis.perMachine` | 默认 `false`：固定当前用户安装；设为 `true`：固定整机安装并请求管理员权限。 |
| `nsis.allowToChangeInstallationDirectory` | 默认 `true`；一键安装时忽略。 |
| `nsis.createDesktopShortcut` | 默认 `true`。 |
| `nsis.createStartMenuShortcut` | 默认 `true`。 |
| `nsis.runAfterFinish` | 默认 `false`；设为 `true` 后允许安装完成时启动应用。 |
| `nsis.include` | 可选，单个自定义 `.nsh` 文件路径；省略或设为 `null` 时禁用。 |
| `nsis.installerIcon` | 可选，安装器 `.ico` 文件；省略或设为 `null` 时使用 NSIS 默认安装图标。 |
| `nsis.uninstallerIcon` | 可选，卸载器及 Windows 卸载列表的 `.ico` 文件；省略或设为 `null` 时保留默认行为。 |

目录相对 **配置文件所在目录** 解析，不依赖命令执行位置。输入目录内的文件和空目录全部打包；不做排除规则处理，因此应使用干净的发布目录。符号链接、目录联接、未知配置字段和重复字段会报错。配置文本不接受控制字符及 `${` 预处理表达式，避免被 NSIS 当作脚本展开。

安装界面默认简体中文。向导模式保留上游行为：选择的目录不包含应用目录名时，会自动追加应用目录名。默认当前用户路径为用户的 Programs 目录，整机路径为相应的 Program Files 目录。

输出文件为 `{安全应用名}-{版本}-{架构}-Setup.exe`。同名文件只在新构建成功后替换；构建失败保留原安装包。

## 安装语言

```yaml
nsis:
  installerLanguages: [zh_CN, en_US, zh_TW]
  displayLanguageSelector: true
```

安装器和卸载器均包含所选语言资源，默认按系统语言匹配，未匹配时使用首项。语言选择框用于安装器；静默安装和构建时生成卸载器不弹框。未配置语言列表时仍只使用简体中文；`init` 生成的示例显式启用中英双语。

支持与固定版 electron-builder 内置列表一致的 27 种语言代码：

```text
en_US de_DE fr_FR es_ES zh_CN zh_TW ja_JP ko_KR it_IT nl_NL
da_DK sv_SE nb_NO fi_FI ru_RU pt_PT pt_BR pl_PL uk_UA cs_CZ
sk_SK hu_HU ar_SA he_IL tr_TR th_TH vi_VN
```

代码不区分大小写，接受 `zh-CN` 等连字符形式。短代码使用列表中首个对应区域，例如 `en` → `en_US`、`zh` → `zh_CN`、`pt` → `pt_PT`；需要其他区域时明确填写。空列表、`null`、未知代码和重复语言会报配置错误。

界面消息复用上游翻译，缺失项回退英语；自有 ARM64 提示提供简体中文、繁体中文和英语回退。应用名称和自定义 `.nsh` 内的文案由应用配置或脚本提供，不自动翻译。

## 应用 GUID

根据原始 `appId` 和命名空间 `50e065bc-3134-11e6-9bab-38c9862bdaf3` 生成 UUID v5，与 electron-builder 未指定 `nsis.guid` 时一致。`appId` 区分大小写，升级时保持不变；版本和显示名称不影响 GUID。

此前 SHA-256 算法生成的安装标识与新 GUID 不同，新包不会自动识别旧包为同一安装。已有旧版安装需先卸载再安装新包；当前未提供旧标识迁移。GUID 对齐不等于完整安装布局兼容，从 electron-builder 安装包切换仍需验证升级流程。

## 自定义图标

```yaml
nsis:
  installerIcon: ./build/installer.ico
  uninstallerIcon: ./build/uninstaller.ico
```

两个字段均相对 **YAML 文件所在目录** 解析，也接受绝对路径，可独立配置或使用同一个文件。只接受 `.ico`，不自动转换 PNG/SVG，也不自动查找默认文件。`validate` 检查文件存在性和 ICO 目录结构，具体图像数据由 NSIS 在构建时校验。

`installerIcon` 设置安装包 EXE 图标；`uninstallerIcon` 设置卸载程序 EXE 图标，同时将图标复制到安装目录的 `uninstallerIcon.ico` 并用于 Windows 卸载列表。不配置时，卸载程序使用 NSIS 默认图标，卸载列表使用启动 EXE 图标。两个选项均不修改应用 EXE 或快捷方式图标。

图标定义在自定义 `.nsh` 加载前生成。若原脚本已有 `MUI_ICON`、`MUI_UNICON` 或 `UNINSTALLER_ICON` 定义，请删除对应重复定义；需要由脚本覆盖时使用 `!define /redef`。

## 自定义 NSIS 脚本

通过 `nsis.include` 扩展默认模板，与 electron-builder 的 [include 宏接口](https://www.electron.build/nsis/) 一致。路径相对 YAML 文件所在目录解析，也支持绝对路径；文件必须存在。不自动查找 `build/installer.nsh`，需要显式配置。

```yaml
nsis:
  include: ./build/installer.nsh
```

在 `build/installer.nsh` 中定义需要的钩子：

```nsis
; 在应用文件解压后执行。
!macro customInstall
  DetailPrint "执行自定义安装步骤。"
!macroend

; 在默认卸载文件清理前执行。
!macro customUnInstall
  DetailPrint "执行自定义卸载步骤。"
!macroend
```

支持固定模板中已有的宏，例如 `customHeader`、`preInit`、`customInit`、`customUnInit`、`customInstall`、`customUnInstall`、`customRemoveFiles` 和 `customUnInstallSection`。向导模式还支持 `customWelcomePage`、`customPageAfterChangeDir`、`customFinishPage`、`customUnWelcomePage`、`customUninstallPage`。当前安装权限由 YAML 固定，未接入 `customInstallMode`；暂不提供替换整个 `.nsi` 的 `nsis.script`。

自定义脚本目录加入头文件搜索路径，可写 `!include "hooks\actions.nsh"`；脚本内的 Windows 路径使用反斜杠。`${BUILD_RESOURCES_DIR}` 指向该目录，附加文件使用 `File "${BUILD_RESOURCES_DIR}\extra.txt"`；编译工作目录仍是临时模板目录，普通相对 `File` 路径不会自动相对 `.nsh` 解析。脚本建议使用 UTF-8，嵌套脚本的资源也以顶层脚本目录为基准。

安装器和卸载器两阶段均加载脚本，`BUILD_UNINSTALLER` 只在生成卸载器阶段定义。`preInit` 也会在构建时的卸载器生成程序中运行；仅需安装时执行的动作放在 `customInit` 或 `customInstall`。`!system` 等预处理指令在编译时执行，`customRemoveFiles` 会替换默认文件清理。只使用可信脚本，并自行负责自定义动作的升级和卸载行为。脚本编译错误保留在失败目录的 `build.log` 中。

完整示例见 [installer.nsh](examples/build/installer.nsh)，包括欢迎页、数据目录选择页和附加资源复制。

示例在安装目录页之后显示“数据存储文件夹”，支持输入路径和浏览文件夹，默认使用最终安装目录下的 `Data`。安装时创建所选目录，由 [data-directory.js](examples/build/data-directory.js) 完成路径校验、JSON 序列化，并通过 nsQuickJs 的 `fs.writeText` 直接写入安装目录的 `data-directory.json`；升级前由 JS 检查配置、校验文件大小，通过 `fs.readText` 读取并解析旧 JSON。静默及一键安装不显示此页，优先使用已有配置，否则使用默认目录。此配置用于演示，`HelloApp` 不读取它。

`examples/build/nsQuickJs` 随示例提供支持 `fs` 的 `0.1.0` x86 Unicode 插件及许可证。安装器进程为 x86，因此三种应用架构均使用该 DLL。安装时无需 PowerShell 或 Node.js。复制示例时保留整个 `build` 目录；插件错误或损坏配置会中止安装。JSON 文件最多 16 KiB，目录最多 1023 个 UTF-16 单元。JSON 使用无 BOM 的 UTF-8，保留中文原文，并兼容旧配置的 BOM 和 Unicode 转义。NSIS 只分块传递页面目录值，不再读写 JSON 字节。

每次读写以当前安装目录作为 `rootDir`，显式启用 `allowFileAccess`。可信脚本释放为该目录内的唯一临时文件，加载后删除；调用完成销毁上下文，用户更改安装目录后按新路径初始化。文件访问受插件根目录限制；安装目录外的数据文件夹仍由 NSIS 创建。

```json
{
  "dataDirectory": "D:\\应用数据"
}
```

示例不修改数据目录权限。安装目录内的数据会随升级或卸载清理；需要长期保留数据时，请选择安装目录外且应用用户可写的位置。

## 示例

复现原 SetupFactory 脚本安装流程的独立示例见 [InstallerDemo](examples/InstallerDemo/README.md)，包含原布局中的子目录入口、四段版本修复/升级、五进程阻止、配置备份恢复、DataPath 选择，以及配置、Data、Log 的可写权限。功能差异与后续改进逐项列在该文档中。

CLI 额外提供 `customBeforeInstall` 宏：在旧版卸载前、应用文件解压前执行，向导、静默和一键安装均生效，可用于配置备份和安装前校验。它是本项目扩展，不属于上游 include 宏。失败时应设置非零退出码并 `Quit`，避免继续卸载旧版。`customInstall` 仍在解压后执行。子目录程序的快捷方式工作目录现与其 EXE 所在目录一致。

```powershell
dotnet publish examples/HelloApp -r win-x64 --self-contained true -o examples/publish
dotnet run --project src/DotnetNsis -- build --config examples/nsis.yml
```

生成 `artifacts/installers/NSIS 示例应用-1.0.0-x64-Setup.exe`。

静默安装支持 NSIS 的 `/S` 和 `/D=`。`/D=` 必须位于命令末尾，值不加引号；可执行文件路径按 shell 规则加引号。

```powershell
& './artifacts/installers/NSIS 示例应用-1.0.0-x64-Setup.exe' /S
```

在 Windows 已安装的应用列表中卸载，或运行安装目录中的 `Uninstall {安全应用名}.exe`。同一 `appId` 的新版本使用上游旧版本卸载和覆盖安装流程；首版验收范围是同架构、同安装权限升级。应用数据应放在安装目录外，安装目录内的内容会被卸载或升级清理。

签名和自动更新未启用。按当前设计，保留上游更新参数和应用数据删除逻辑，但 CLI 不生成更新配置、不启用默认数据删除，也不提供相应 YAML 开关。

## 缓存与错误排查

工具缓存位于 `%LOCALAPPDATA%/dotnet-nsis/cache`：

- NSIS 工具包 `1.2.1`，包含 NSIS `3.12` 和插件。
- 7-Zip 工具包 `1.0.1`，使用 Windows x64 可执行程序。
- 下载归档校验固定 SHA-256；缓存文件校验各自摘要，损坏后从已验证归档修复。
- 缓存完整后可以离线打包。多个进程通过依赖锁避免重复写入缓存。

构建失败时，输出目录中的 `.dotnet-nsis-{随机编号}` 会保留应用副本、生成脚本和 `build.log`；成功后清理。命令成功时标准输出打印安装包绝对路径，进度写入标准错误。

退出码：`0` 成功，`2` 配置错误，`1` 下载或构建错误，`130` 用户取消。压缩后应用达到 2 GB 时提前报错，避免生成超过 NSIS 单包限制的安装包。

## 测试

```powershell
dotnet test
# 下载真实工具并验证 3 架构 × 2 模式 × 2 安装权限的构建。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1
# 另外执行 x86/x64 的当前用户安装、升级、启动和卸载。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1 -Install
```

安装测试使用随机应用标识和临时安装目录，测试结束清理快捷方式、注册表项及测试数据。建议在测试用 Windows 环境运行。测试检查子目录程序、文件完整性、空目录、快捷方式目标、版本升级和卸载后保留用户数据。

普通 `dotnet test` 明确跳过依赖下载及真实安装测试。整机 UAC 交互、向导页面交互和 ARM64 实际安装需在对应环境验证。

## 模板来源

安装模板来自 [electron-builder 的 NSIS 模板目录](https://github.com/electron-userland/electron-builder/tree/8eff005219db1d17363add3ef4902e5b14798241/packages/app-builder-lib/templates/nsis)（`packages/app-builder-lib/templates/nsis`）。链接指向本项目使用的固定提交，便于追溯模板版本。原始模板随 CLI 分发，适配只作用于每次构建的临时副本。完整提交哈希与改动说明见 [第三方资源说明](THIRD-PARTY-NOTICES.md)。

开发时可运行 `python scripts/vendor.py` 重新获取该固定提交。Python 只用于维护模板，CLI 运行不依赖 Python。

## 参与开发与发布

参见 [贡献指南](CONTRIBUTING.md)、[安全问题反馈](SECURITY.md)、[更新日志](CHANGELOG.md)、[构建与发布流程](docs/build.md) 和 [验证范围](docs/validation.md)。默认首页为英文，本文保留完整中文使用说明。

GitHub Actions 在匹配的 `vX.Y.Z` 标签通过构建与真实安装测试后，
通过 Trusted Publishing（OIDC）以 `gaoshang212` 账号发布验证过的 `dotnet-nsis` 工具包到 NuGet.org。
需要先在 NuGet.org 配置信任策略，无需保存长期 API Key Secret。每次构建均生成 `.nupkg`，从仅含本地源的隔离目录安装，
验证命令和真实 NSIS 构建后上传产物；发布配置步骤见发布文档。

HelloApp、InstallerDemo 和本机来源说明均保留，安装流程示例统一使用 InstallerDemo 名称。示例源码在源码仓库中，NuGet 包包含 CLI、模板、许可证和文档；按示例构建时使用完整源码目录。文档中的本机路径用于记录来源，运行示例无需这些外部工程。
