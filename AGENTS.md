# Repository Guidelines

## 项目结构与职责

- `src/DotnetNsis/`：.NET 8 CLI。`CommandLineApplication` 使用 System.CommandLine 定义命令和选项，`Configuration` 校验 YAML，`ToolCache` 管理依赖，`TemplateAdapter` 适配模板，`PackageBuilder` 编排打包，`ProcessRunner` 执行外部工具。
- `src/DotnetNsis/Resources/`：固定版本的 electron-builder 原始模板及第三方许可证。
- `tests/DotnetNsis.Tests/`：xUnit 单元测试、构建矩阵和实际安装测试。
- `examples/`：示例应用与 `nsis.yml`；`scripts/`：模板获取与集成测试脚本。
- `artifacts/`、`bin/`、`obj/`、`.cache/` 为生成目录，不提交产物。

## 构建与开发命令

在仓库根目录执行，开发需要 .NET 10 SDK，运行 CLI 与测试需要 .NET 8 运行时；真实打包使用 Windows x64。Demo 保持 .NET 10 目标，分发的 NuGet 工具以 .NET 8 为运行基线。

```powershell
dotnet restore --locked-mode
dotnet build
dotnet test --no-restore
dotnet run --project src/DotnetNsis -- init
dotnet run --project src/DotnetNsis -- validate --config examples/nsis.yml
dotnet run --project src/DotnetNsis -- build --config examples/nsis.yml
dotnet pack src/DotnetNsis -c Release -o artifacts/nuget -p:RestoreLockedMode=true
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-tool-package.ps1
```

依次用于锁定依赖还原、编译、测试、生成配置、校验、打包应用及生成和验证 NuGet 工具包。示例打包前按 README 发布 `HelloApp`。NuGet 包须包含 `Resources`、许可证和文档。

最后两条生成并验证 NuGet `.NET Tool` 包，包 ID 与命令均为 `dotnet-nsis`，需要 .NET 8。
NuGet 发布沿用 `vX.Y.Z` 标签流程，通过 Trusted Publishing（OIDC）以 `gaoshang212` 登录，仅发布任务授予 `id-token: write`；具体配置见 `docs/build.md`。
修改分发配置时验证 NuGet 工具包安装，保留示例和本机来源说明。

## 编码风格与命名

C# 使用四空格缩进、文件范围命名空间和可空引用类型。类型、方法及属性使用 `PascalCase`，参数和局部变量使用 `camelCase`，异步方法以 `Async` 结尾并传递取消令牌。YAML 字段使用 `camelCase`，缩进两空格。

保持现有格式，遵循 `.editorconfig` 和 `.gitattributes`；没有专用格式化器或 lint 命令。编译警告视为错误。实现注释和面向用户的 CLI 提示使用简洁中文，保留上游许可证原文。默认 `README.md` 使用英文，与 `README.zh-CN.md` 同步；贡献指南及 `docs/` 文档使用英文。保留现有 Demo 和本机来源说明，安装流程示例的项目与模块统一使用 InstallerDemo 名称。外部进程优先使用 `ArgumentList`，避免拼接 shell 命令。

## 注释要求

所有类、方法和成员都必须有中文注释，包括公开与非公开的构造函数、方法、属性、字段、常量、事件及枚举成员；测试代码同样适用。接口、结构体和记录类型也必须注明用途。

C# 声明使用 `///` XML 文档注释，至少包含 `<summary>`，说明职责、用途或行为，避免只复述名称。局部函数使用简短中文注释说明用途。修改行为时同步更新注释，保持简洁准确。第三方原始模板保持原样。

## 测试要求

使用 xUnit `[Fact]`、`[Theory]`，测试名直接描述行为，例如 `RejectsInvalidSettings`。未设置覆盖率百分比门槛；行为修改应覆盖成功路径、错误输入及回归场景。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1 -Install
```

第一条验证三种架构、两种安装模式及两种权限的构建；第二条增加 x86/x64 实际安装、升级和卸载。普通 `dotnet test` 跳过真实工具链测试。安装测试会创建临时注册表项和快捷方式，应在测试环境运行；ARM64 实际安装及 UAC 交互需单独验证。

## 提交与拉取请求

仓库已存在 Git 历史。新提交参照 nsQuickJs 的开源规范，使用 `feat:`、`fix:`、`docs:`、`test:` 等 Conventional Commits 前缀及简短英文说明，正文使用英文，一次提交聚焦一项改动。

PR 说明问题、行为变化和验证结果；有关联任务则附链接。配置或模板变更应同步示例、README 和测试。安装界面变化附截图，未验证的平台明确标注。

## 配置与模板维护

配置路径相对 YAML 所在目录解析，启动程序必须位于应用目录内。保持 `appId` 稳定；输出目录不得位于输入目录内。

原始模板保持完整，适配集中在 `TemplateAdapter` 的临时副本。更新上游版本或工具包时同步固定提交、下载摘要及第三方说明，并运行构建矩阵。区分工具包版本与 NSIS 编译器版本。保留上游更新参数和数据删除逻辑，首版默认不启用；不引入签名流程。
