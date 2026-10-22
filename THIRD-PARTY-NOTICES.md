# 第三方资源说明

dotnet-nsis 自有代码采用根目录 [LICENSE](LICENSE) 中的 MIT 许可证。以下组件保留各自版权和许可，项目许可不替代第三方声明。

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
4. 单架构 ARM64 包拒绝在其他架构系统上安装，提示按配置语言生成，整机安装使用 64 位 Program Files。

入口脚本支持上游内置的 27 种安装语言，语言文件名和 Windows 语言标识依据固定提交的 `packages/app-builder-lib/src/util/langs.ts` 与 `nsisLang.ts` 映射。缺失翻译回退英语。

应用 GUID 使用 `progId.ts` 中的命名空间 `50e065bc-3134-11e6-9bab-38c9862bdaf3` 和 UUID v5，与 electron-builder 默认算法一致。旧版 SHA-256 标识不自动迁移；GUID 一致不代表完整安装布局和升级流程兼容。

更新参数、更新器相关分支和应用数据删除逻辑保留上游实现；CLI 不生成更新配置，不启用默认数据删除。

## 下载的工具

下载来源：electron-userland/electron-builder-binaries。固定版本和 SHA-256 位于 `ToolCache.cs`，摘要来自上述固定提交的 `src/toolsets/nsis.ts` 和 `src/toolsets/7zip.ts`。

| 工具包 | 固定发布 | 归档 |
| --- | --- | --- |
| NSIS 和插件 | `nsis@1.2.1` | `nsis-bundle-3.12.tar.gz` |
| 7-Zip | `7zip@1.0.1` | `7zip-win-x64.tar.gz` |

缓存解压保留工具包内的许可证文件。重新分发工具包时，需要一并保留其许可证及版权声明；插件以各自声明为准。

## nsQuickJs 示例插件

- 版本：`0.1.0`，使用支持 `fs` 和 `allowFileAccess` 的更新构建；来源为本机 `D:\workspace\sources\nsQuickJs\dist\nsQuickJs` 发布包，复制时源仓库尚无提交。源项目现已配置公开地址：https://github.com/gaoshang212/nsQuickJs 。此地址用于追踪后续源码，不将当前 DLL 追溯为未经核验的发布标签；当前副本以下列摘要标识。
- 本地副本：`examples/build/nsQuickJs`，仅随示例分发。
- `Plugins/x86-unicode/nsQuickJs.dll` SHA-256：`db25df7895efd71654094ea57bb828109c84cbf0a3744618e8e7d06805b31802`。
- nsQuickJs 采用 MIT 许可证；保留 `LICENSE`、`THIRD_PARTY_NOTICES.md` 及 `licenses` 中的 QuickJS-NG、NSIS 声明。
- 静态链接 QuickJS-NG `v0.17.0`，固定提交 `6d46d07d04041b40f4f49eaa7fdebe44c314c699`。

## YamlDotNet

YAML 解析使用 [YamlDotNet](https://github.com/aaubry/YamlDotNet)，版本 `16.3.0`，采用 MIT 许可证。许可证副本位于 `src/DotnetNsis/Resources/YAMLDOTNET-LICENSE.txt`，随 CLI 分发。NuGet 锁文件记录精确依赖及包内容摘要。

## System.CommandLine

命令行解析使用 [System.CommandLine](https://www.nuget.org/packages/System.CommandLine/2.0.13)，版本 `2.0.13`，采用 MIT 许可证。许可证副本位于 `src/DotnetNsis/Resources/SYSTEM-COMMANDLINE-LICENSE.txt`，随 CLI 分发。NuGet 锁文件记录精确依赖及包内容摘要。
