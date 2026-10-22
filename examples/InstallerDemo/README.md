# InstallerDemo 安装流程 Demo

依据 `D:\workspace\csharp\NeuracleEMG\SetupFactoryScript` 目录中的原 SetupFactory 脚本编写，演示安装行为，不包含肌电检测业务。项目、模块和界面统一使用 InstallerDemo 名称。

Demo 使用独立 `appId: com.dotnetnsis.demo.installer`，不接管真实产品的注册表或安装目录。此标识与更名前的 Demo 不同，不自动覆盖或迁移旧示例；修复和升级测试仅在当前标识的版本之间进行。

## 构建与运行

在仓库根目录执行，需要 Windows x64 和 .NET 10 SDK：

```powershell
# 与原包一致：整机安装，需要 UAC；默认生成 1.0.0.0。
powershell -NoProfile -ExecutionPolicy Bypass -File examples/InstallerDemo/build.ps1

# 需要普通用户测试时使用此选项；同一轮升级测试保持相同安装权限。
powershell -NoProfile -ExecutionPolicy Bypass -File examples/InstallerDemo/build.ps1 -CurrentUser

# 生成升级包，保留此前的安装包用于验证禁止降级。
powershell -NoProfile -ExecutionPolicy Bypass -File examples/InstallerDemo/build.ps1 -Version 1.0.0.1 -CurrentUser
```

产物：`artifacts/installer-demo/installers/InstallerDemo 安装演示-1.0.0.0-x64-Setup.exe`。构建脚本生成自包含应用、卸载清单及当前构建用 YAML，正常发布和安装均不需要 Node.js 或 PowerShell。PowerShell 只用于开发机构建。目标机无需预装 .NET。

安装向导包含欢迎页、程序目录、数据目录、进度和完成页。修复/升级跳过两个目录页，固定旧安装路径并保留旧 DataPath。也支持 `/S /D=绝对目录`；`/D=` 放末尾且值不加引号。此静默支持是相对原包的新增能力。

桌面快捷方式启动主 Demo，控制台显示程序、工作目录、配置、数据及日志位置，并实际写入 Data 和 Log。各入口加 `--smoke` 执行后退出，加 `--wait` 保持运行以验证安装阻止。

## 目录结构及来源边界

```text
安装目录/
├─ Neuracle.InstallerDemo.View/
│  ├─ net8.0-windows/
│  │  ├─ Neuracle.InstallerDemo.View.exe
│  │  └─ fonts/                         # 预留位置，未塞入替代字体
│  ├─ Conf/System/System/SystemSetting.json
│  ├─ Data/
│  └─ Log/demo.log
├─ Neuracle.InstallerDemo.MasterPC.View/         # 演示进程检查用
│  ├─ net8.0-windows/Neuracle.InstallerDemo.MasterPC.View.exe
│  └─ Conf/、Data/、Log/
├─ Neuracle.InstallerDemo.Simulator.View/        # 演示进程检查用
│  ├─ net8.0-windows/Neuracle.InstallerDemo.Simulator.View.exe
│  └─ Conf/、Data/、Log/
├─ Neuracle.InstallerDemo.MonitorCalibrate.View/
│  ├─ net8.0-windows/Neuracle.InstallerDemo.MonitorCalibrate.View.exe
│  └─ Conf/、Data/、Log/
├─ Neuracle.InstallerDemo.Stimulator.VEPStim/
│  ├─ net8.0-windows/Neuracle.InstallerDemo.Stimulator.VEPStim.exe
│  └─ Conf/、Data/、Log/
├─ Data/                               # 新装默认数据目录，可另选
├─ installer-demo.ini                   # 安装模式和恢复目录位置
└─ Uninstall InstallerDemo 安装演示.exe
```

原 SetupFactory 脚本使用 `#ReleasePath#\*.*` 递归收集发布产物，本身不包含完整文件清单。主入口、字体和 SystemSetting 路径来自脚本明确引用；另外检查了本机 `NeuracleEMG/src/Release`，确认五个程序均位于各自模块的 `net8.0-windows` 子目录。Demo 将模块名改为 InstallerDemo，复现关键目录和行为，没有复制真实产品的所有 DLL、配置、资源及测试残留文件。示例二进制实际使用 .NET 10，自包含运行；目录名保留原布局的 `net8.0-windows`。

`NeuracleEMG/build/Build.cs` 在正式打包前移除 MasterPC 和 Simulator 目录，但 `.suf` 仍检查它们的进程。Demo 特意保留这两个测试入口，以便实际验证五个进程；它们不代表正式包包含这两个模块。原工程的 `log4net.config` 使用 `../Log`，本 Demo 对应模块级 `Log`。

五个模块的 `Conf` 均保留已核对的目录骨架：`Factory/Picture`、`Factory/Report`、`Factory/SystemConf`、`Factory/User/Study`、`Factory/User/User`、`System/Picture`、`System/Study`、`System/System`。除主模块的演示 SystemSetting 外这些目录为空，不冒充真实业务配置。

## 行为对照与改进

| 原包行为 | Demo 状态 | 实现或差异 |
| --- | --- | --- |
| 递归复制发布目录、子目录主入口 | 已实现示例布局 | 使用真实可运行 Demo EXE；并非真实发布物 |
| 管理员、Program Files、桌面快捷方式 | 已实现 | 默认整机；无开始菜单快捷方式；工作目录为主 EXE 所在目录 |
| 五进程存在时拒绝继续 | 已实现 | 安装初始化、实际替换前、卸载均检查；列出全部命中进程，不强杀；同名进程即阻止，匹配原脚本语义 |
| 四段数字版本；FRESH / REPAIR / UPGRADE；禁止降级 | 已实现 | CLI 接受三/四段版本；Demo 使用 NSIS 数字版本比较；同版本重装为修复 |
| 升级/修复锁定程序及数据目录 | 已实现 | 跳过选择页；忽略升级时另传的 `/D=`；保留配置中的 DataPath |
| 注册表残留版本但无路径时回退新装 | 改进 | 明确报错停止，要求修复注册信息，避免意外覆盖未知旧环境 |
| 首次通过 jsonUpdate.exe 修改 DataPath | 已实现等价行为 | 自包含 C# 辅助命令修改 JSON，保留未知字段、中文；无额外 jsonUpdate.exe，无安装时 JS 依赖 |
| 升级前备份、安装后恢复 SystemSetting | 已实现并加强 | 在新 `customBeforeInstall` 钩子备份；恢复原始字节（含 BOM/格式）；损坏、缺失、超过 4 MiB 或不可读配置中止升级 |
| 备份失败可选择继续；恢复失败只记日志并删除备份 | 改进 | 失败返回非零，不删除唯一备份；备份目录保留于 `%TEMP%/*-installer-recovery`，在 `installer-demo.ini` 中记录 |
| 整个程序目录 Everyone 完全控制 | 按用户要求调整 | 仅内置 Users 对配置、Data、Log 获得可继承 Modify；外部 DataPath 同样授权；不授予程序目录写权限或 ACL 修改权 |
| 卸载程序和桌面链接 | 已实现并加强 | 使用随包文件清单删除；保留 Conf、Data、Log 和用户添加的文件；新包升级时旧清单能移除旧程序文件 |
| 原 SetupFactory 禁用静默 | 增强 | 支持 NSIS 静默；关键配置动作不放在完成页事件中 |
| `SourceHanSansCN-VF.otf` 安装到系统 Fonts 并注册，卸载永久保留 | **尚未实现** | 见下面字体方案；空 fonts 目录不是字体安装 |
| 原日志文件、Office 皮肤、修复/升级按钮文字 | **未等价复刻** | 使用 NSIS MUI；恢复记录保留模式与路径，当前没有原引擎级完整操作日志 |
| 旧 SetupFactory 产品覆盖升级 | **尚未实现** | 独立 GUID，只支持本 Demo 各版本之间升级；不自动运行或删除真实旧安装 |

## 尚需补齐的能力与具体方案

1. **系统字体**：已在 `NeuracleEMG/src/Release` 找到真实字体（15,054,976 字节，SHA-256 `ff6c3bb614852de627faa2257390a164c6a6c24dfd2ecd83384026681ee415a7`），尚未复制进 Demo。下一步补齐随包授权文件及受控字体安装动作：检查现有字体版本，复制到 `%WINDIR%\Fonts`，写 Fonts 注册项，调用 `AddFontResourceExW` 并广播 `WM_FONTCHANGE`；处理占用、需要重启及错误码。原包 `NoRemove=1`，应明确保留系统字体，不在卸载时盲删其他软件共用的字体。当前缺口是系统安装动作和验证，没有用默认字体冒充已完成。
2. **接管旧 SetupFactory 安装**：明确实际 `#Name#` 产品名、版本和卸载信息；检查 HKLM 32/64 位视图、旧路径和配置，备份后受控调用旧卸载器，再写新 GUID。原包 `AllowSilent=0`，不能假设 `/S` 有效；需在含真实旧版的虚拟机验证交互卸载、失败恢复和数据保留。仅令 GUID 相同不能解决安装布局和卸载协议差异。
3. **真实发布目录与运行依赖**：已检查本机 Release 的关键目录，但正式交付仍须选定目标版本并比对完整文件清单；当前 Release 中可见 testhost 等开发残留，不能直接视为干净的正式发布物。现有 `.suf` 的通配符不足以说明所有插件、数据库、驱动和资源的业务用途。本 Demo 不安装驱动或 .NET 8 桌面运行时，不是检测软件替代物。
4. **DB 等额外可写位置**：真实 SystemSetting 还有 `DBDataPath` / SQLite `DBConnectionString` 指向 `../DB`。当前按已确认的配置、Data、Log 范围授权；接入真实软件前须盘点 DB、缓存等写入位置，将其纳入受控目录列表，不能重新开放整个程序树来掩盖遗漏。
5. **完整事务和失败回滚**：配置备份失败会在卸载前停止；恢复失败保留备份并报错，但旧版已卸载后的磁盘故障不会自动还原全部程序或注册表。下一步需 staging 目录、安装事务日志和提交/回滚阶段。当前不能宣称支持完整原子升级。
6. **通用配置能力**：当前多进程检查、权限和保留清单属于 Demo 的 include/辅助程序。若要给其他产品复用，应再抽取 `processChecks`、`writableDirectories`、`preserveFiles`、`fonts` 等经过路径校验的 YAML 配置，而不是让产品复制固定 InstallerDemo 路径。

Demo 当前只支持本地磁盘路径，拒绝 UNC、目录联接及符号链接。安装目录内的数据只允许专用 Data 子树；禁止把程序目录或其父目录当作 DataPath 以扩大写权限。新装要求空目录；卸载后保留的目录不能直接被当作全新安装覆盖，需先备份并自行安排这些保留文件。

字体后续实现依据 [Microsoft 字体安装和删除说明](https://learn.microsoft.com/zh-cn/windows/win32/gdi/font-installation-and-deletion)：运行期加载与永久注册是不同步骤，字体变更还需通知其他窗口。

## 验证

```powershell
dotnet test --no-restore
# 先执行上面的构建，再在测试环境运行真实安装生命周期。
$env:DOTNET_NSIS_INSTALL_TESTS = '1'
dotnet test --no-restore --filter 'FullyQualifiedName~InstallerDemoTests'
```

普通测试覆盖配置字节保留、损坏输入和目录授权边界。真实生命周期测试使用随机 appId、临时目录和当前用户安装，覆盖首次安装、同版本修复、第四段升级、降级拒绝、五进程阻止、坏配置阻止、Demo 实际运行及卸载后保留数据/日志/配置。测试不会安装真实产品。

整机 UAC 交互、不同普通用户实际访问、向导视觉与系统字体安装需要在专用测试环境验收；目录 ACL 规则检查不能代替这些验证。
