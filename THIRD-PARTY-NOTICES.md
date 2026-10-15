# 第三方资源说明

## electron-builder

- 来源：https://github.com/electron-userland/electron-builder
- 固定提交：`8eff005219db1d17363add3ef4902e5b14798241`
- 原始目录：`packages/app-builder-lib/templates/nsis`
- 本地副本：`src/DotnetNsis/Resources/nsis`
- 上游许可证副本：`src/DotnetNsis/Resources/ELECTRON-BUILDER-LICENSE.txt`

原始模板完整保留；各文件自带的版权和许可证声明同样保留。构建时由 `TemplateAdapter` 在临时副本应用以下适配：

1. 启动程序使用配置的相对路径；进程检查单独使用文件名。
2. 卸载器写入路径分别处理编译期和运行期转义。
3. 安装向导固定使用 YAML 指定的用户或整机权限，取消运行时权限选择页面。
4. 单架构 ARM64 包拒绝在其他架构系统上安装，整机安装使用 64 位 Program Files。

更新参数、更新器相关分支和应用数据删除逻辑保留上游实现；首版不启用这些功能。项目不承诺与现有 electron-builder 安装包的应用标识兼容。

## 下载的工具

下载来源：electron-userland/electron-builder-binaries。固定版本和 SHA-256 位于 `ToolCache.cs`，摘要来自上述固定提交的 `src/toolsets/nsis.ts` 和 `src/toolsets/7zip.ts`。

| 工具包 | 固定发布 | 归档 |
| --- | --- | --- |
| NSIS 和插件 | `nsis@1.2.1` | `nsis-bundle-3.12.tar.gz` |
| 7-Zip | `7zip@1.0.1` | `7zip-win-x64.tar.gz` |

缓存解压保留工具包内的许可证文件。重新分发工具包时，需要一并保留其许可证及版权声明；插件以各自声明为准。

## YamlDotNet

YAML 解析使用 [YamlDotNet](https://github.com/aaubry/YamlDotNet)，版本 `16.3.0`，采用 MIT 许可证。许可证副本位于 `src/DotnetNsis/Resources/YAMLDOTNET-LICENSE.txt`，随 CLI 分发。NuGet 锁文件记录精确依赖及包内容摘要。

## System.CommandLine

命令行解析使用 [System.CommandLine](https://www.nuget.org/packages/System.CommandLine/2.0.13)，版本 `2.0.13`，采用 MIT 许可证。许可证副本位于 `src/DotnetNsis/Resources/SYSTEM-COMMANDLINE-LICENSE.txt`，随 CLI 分发。NuGet 锁文件记录精确依赖及包内容摘要。
