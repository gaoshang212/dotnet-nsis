# dotnet-nsis

A .NET tool that packages a published application directory into a Windows NSIS
installer from YAML. Build installers on **Windows x64 with the .NET 8 SDK**.
Each installer targets x64, x86, or ARM64 application files.

## Install and use

```powershell
dotnet tool install --global dotnet-nsis
dotnet-nsis init --config ./nsis.yml
# Edit nsis.yml with your published application directory and entry executable.
dotnet-nsis validate --config ./nsis.yml
dotnet-nsis build --config ./nsis.yml
```

Update with `dotnet tool update --global dotnet-nsis`.

```yaml
appId: com.example.myapp
productName: My Application
version: 1.0.0
appDirectory: ./publish
executable: MyApp.exe
outputDirectory: ./dist
arch: x64
nsis:
  installerLanguages: [zh_CN, en_US]
  oneClick: false
  perMachine: false
```

Paths are relative to the YAML file. The CLI packages existing application files;
it does not compile them or install their runtime. Keep `appId` stable for upgrades
and store persistent application data outside the installation directory.

The tool includes pinned electron-builder templates and dependency licenses.
NSIS and 7-Zip are downloaded and hash-verified on first build, then cached at
`%LOCALAPPDATA%/dotnet-nsis/cache`. Node.js and manual NSIS installation are not
required. Custom NSIS scripts must be trusted. Signing and automatic updates are
not provided.

The project uses the MIT license. Third-party components retain their own
licenses, included alongside the tool. Full English/Chinese READMEs and
third-party notices are also included in the installed tool's content directory.

## 中文说明

安装与运行使用 .NET 8，打包主机使用 Windows x64。读取 YAML，将已发布的
应用目录生成 NSIS 安装程序，支持 x64、x86、ARM64 应用以及向导或一键安装。
首次构建自动下载并校验工具，后续复用缓存。CLI 提示保留中文。

此包为命令行工具，使用 `dotnet tool install` 安装；不作为 `PackageReference`
类库引用。源码仓库中的 HelloApp、InstallerDemo 和本机来源说明保持完整，
安装流程示例统一使用 InstallerDemo 名称，示例构建使用完整源码目录。
