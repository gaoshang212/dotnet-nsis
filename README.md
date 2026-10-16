# dotnet-nsis

用 C# 编写的 Windows 安装包 CLI。读取 YAML，把已发布的应用目录打成 NSIS `.exe` 安装包。

复用固定版本的 electron-builder NSIS 模板。运行时无需 Node.js，也无需手动安装 NSIS 或 7-Zip。首次构建下载工具，后续复用本机缓存。

## 快速开始

开发需要 .NET 10 SDK，打包主机使用 Windows x64。安装包可以面向 x64、x86 或 ARM64，每次构建一种架构。

```powershell
dotnet build
dotnet run --project src/DotnetNsis -- init
# 编辑 nsis.yml，填入应用发布目录和启动程序。
dotnet run --project src/DotnetNsis -- validate
dotnet run --project src/DotnetNsis -- build
```

生成不依赖本机 .NET 运行时的 CLI：

```powershell
dotnet publish src/DotnetNsis -c Release -r win-x64 --self-contained true -o artifacts/cli
./artifacts/cli/dotnet-nsis.exe build --config ./nsis.yml
```

分发时复制整个 `artifacts/cli` 目录，包括 `Resources`。CLI 不会编译被打包的应用，也不会安装其 .NET 运行时；应用本身需要自包含发布，或由目标电脑预先准备运行时。

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
| `version` | 必填，三段数字，每段 0～65535，例如 `1.2.3`。 |
| `publisher` | 可选，默认空。 |
| `appDirectory` | 必填，包含完整应用文件的目录。 |
| `executable` | 必填，相对应用目录的 `.exe` 路径，支持子目录。 |
| `outputDirectory` | 默认 `./dist`，不得等于或位于应用目录内。 |
| `arch` | `x64`、`x86` 或 `arm64`，默认 `x64`；应与应用发布架构一致。 |
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

安装界面固定简体中文。向导模式保留上游行为：选择的目录不包含应用目录名时，会自动追加应用目录名。默认当前用户路径为用户的 Programs 目录，整机路径为相应的 Program Files 目录。

输出文件为 `{安全应用名}-{版本}-{架构}-Setup.exe`。同名文件只在新构建成功后替换；构建失败保留原安装包。

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

完整示例见 [installer.nsh](examples/build/installer.nsh)，包括欢迎页和附加资源复制。

## 示例

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

上游固定提交：[`8eff005219db1d17363add3ef4902e5b14798241`](https://github.com/electron-userland/electron-builder/tree/8eff005219db1d17363add3ef4902e5b14798241/packages/app-builder-lib/templates/nsis)。原始模板随 CLI 分发，适配只作用于每次构建的临时副本。改动说明见 [第三方资源说明](THIRD-PARTY-NOTICES.md)。

开发时可运行 `python scripts/vendor.py` 重新获取该固定提交。Python 只用于维护模板，CLI 运行不依赖 Python。
