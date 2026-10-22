<#
.SYNOPSIS
检查 NuGet 工具包内容，在隔离目录安装并验证命令及真实 NSIS 构建。
#>
param(
    # 默认使用项目当前版本对应的本地产物，不访问公开 NuGet 源。
    [string]$PackagePath,
    # 标签构建时核对稳定版版本；本地或分支构建可省略。
    [string]$Tag
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$project = [IO.File]::ReadAllText((Join-Path $repo 'src/DotnetNsis/DotnetNsis.csproj'))
$version = [string]$project.Project.PropertyGroup.Version
$packageId = [string]$project.Project.PropertyGroup.PackageId
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '无法读取三段项目版本。' }
if ($Tag -and $Tag -cne "v$version") { throw "标签 $Tag 与项目版本 v$version 不一致。" }
# 发布仅接受具备非空更新说明的版本，避免移除其他分发方式后遗漏版本门禁。
$notesPattern = '(?ms)^## \[' + [regex]::Escape($version) + '\][^\r\n]*\r?\n(?<body>.*?)(?=^## |\z)'
$notes = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'CHANGELOG.md')), $notesPattern)
if (!$notes.Success -or !$notes.Groups['body'].Value.Trim()) { throw "CHANGELOG.md 缺少 $version 的说明。" }
if (!$PackagePath) { $PackagePath = Join-Path $repo "artifacts/nuget/$packageId.$version.nupkg" }
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$toolRoot = 'tools/net8.0/any/'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)

# 核对随包文件与源码完全一致，防止许可证、文档或模板遗漏、过期。
function Assert-PackageFile([string]$Source, [string]$Entry) {
    $entryFile = $archive.GetEntry($Entry)
    if (!$entryFile) { throw "NuGet 包缺少：$Entry" }
    $stream = $entryFile.Open()
    $original = [IO.File]::OpenRead((Join-Path $repo $Source))
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        if ([BitConverter]::ToString($hash.ComputeHash($stream)) -cne
            [BitConverter]::ToString($hash.ComputeHash($original))) {
            throw "NuGet 包文件已过期：$Entry"
        }
    }
    finally { $hash.Dispose(); $original.Dispose(); $stream.Dispose() }
}

try {
    # 检查运行时基线，防止意外重新分发需要更新 .NET 的工具包。
    $runtimeEntry = $archive.GetEntry($toolRoot + 'dotnet-nsis.runtimeconfig.json')
    if (!$runtimeEntry) { throw 'NuGet 包缺少 .NET 8 运行配置。' }
    $reader = [IO.StreamReader]::new($runtimeEntry.Open())
    try { $runtime = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($runtime.runtimeOptions.tfm -cne 'net8.0' -or
        $runtime.runtimeOptions.framework.name -cne 'Microsoft.NETCore.App' -or
        $runtime.runtimeOptions.framework.version -cne '8.0.0') {
        throw 'NuGet 工具的运行时基线必须是 .NET 8。'
    }
    $manifestEntry = $archive.GetEntry("$packageId.nuspec")
    if (!$manifestEntry) { throw 'NuGet 包缺少项目清单。' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.package.metadata.id -cne $packageId -or
        $manifest.package.metadata.version -cne $version -or
        $manifest.package.metadata.packageTypes.packageType.name -cne 'DotnetTool') {
        throw 'NuGet 包的标识、版本或工具类型与项目不一致。'
    }
    Assert-PackageFile 'docs/nuget-readme.md' 'README.md'
    Assert-PackageFile 'LICENSE' 'LICENSE'
    foreach ($name in @('LICENSE', 'README.md', 'README.zh-CN.md', 'THIRD-PARTY-NOTICES.md',
        'CHANGELOG.md', 'SECURITY.md', 'CONTRIBUTING.md')) {
        Assert-PackageFile $name ($toolRoot + $name)
    }
    foreach ($mapping in @(
        @{ Source = 'src/DotnetNsis/Resources'; Destination = 'Resources' },
        @{ Source = 'docs'; Destination = 'docs' }
    )) {
        $sourceRoot = Join-Path $repo $mapping.Source
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
            $relative = $file.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
            Assert-PackageFile (Join-Path $mapping.Source $relative) ($toolRoot + $mapping.Destination + '/' + $relative)
        }
    }
}
finally { $archive.Dispose() }

# 使用独立工具目录、包缓存和仅含本地源的 NuGet 配置，避免误测公开或旧版本。
$scratch = Join-Path $repo ('artifacts/nuget-smoke/' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $scratch 'feed'
$toolPath = Join-Path $scratch 'tool'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
Copy-Item -LiteralPath $package -Destination $feed
$config = Join-Path $scratch 'NuGet.Config'
$escapedFeed = [Security.SecurityElement]::Escape($feed)
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key='local' value='$escapedFeed'/></packageSources></configuration>", $utf8)
$oldPackages = $env:NUGET_PACKAGES
$oldRollForward = $env:DOTNET_ROLL_FORWARD
try {
    $env:NUGET_PACKAGES = Join-Path $scratch 'packages'
    # 只允许补丁版本前滚，确保工具实际在 .NET 8 上验证。
    $env:DOTNET_ROLL_FORWARD = 'LatestPatch'
    & dotnet tool install $packageId --version $version --tool-path $toolPath --configfile $config --no-cache
    if ($LASTEXITCODE) { throw '本地 NuGet 工具安装失败。' }
    $cli = Join-Path $toolPath 'dotnet-nsis.exe'
    $reported = & $cli --version
    if ($LASTEXITCODE -ne 0 -or (($reported | Out-String).Trim() -split '\+', 2)[0] -cne $version) {
        throw '已安装工具版本不符。'
    }
    & $cli --help
    if ($LASTEXITCODE) { throw '已安装工具帮助命令失败。' }
    $settings = Join-Path $scratch 'nsis.yml'
    & $cli init --config $settings
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $settings)) { throw '工具配置生成失败。' }

    # 使用不运行的最小载荷验证资源解析和真正的编译；不会安装生成的程序。
    New-Item -ItemType Directory -Path (Join-Path $scratch 'app') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $scratch 'app/app.exe'), 'MZ tool package smoke payload', $utf8)
    [IO.File]::WriteAllText($settings, @'
appId: com.dotnetnsis.tool.smoke
productName: Tool Package Smoke
version: 1.0.0
appDirectory: ./app
executable: app.exe
outputDirectory: ./output
arch: x64
nsis:
  installerLanguages: [zh_CN, en_US]
'@, $utf8)
    & $cli validate --config $settings
    if ($LASTEXITCODE) { throw '已安装工具配置校验失败。' }
    & $cli build --config $settings
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $scratch 'output/Tool Package Smoke-1.0.0-x64-Setup.exe'))) {
        throw '已安装工具的 NSIS 构建失败。'
    }
    # 错误配置必须保留 CLI 退出码约定。
    [IO.File]::AppendAllText($settings, "`nunknownSetting: true`n", $utf8)
    & $cli validate --config $settings
    if ($LASTEXITCODE -ne 2) { throw '已安装工具未以退出码 2 拒绝错误配置。' }
}
finally {
    $env:NUGET_PACKAGES = $oldPackages
    $env:DOTNET_ROLL_FORWARD = $oldRollForward
}
Write-Host "NuGet 工具验证通过：$package；验证产物位于 $scratch"
# 最后一条负例命令的退出码不能传递给 CI。
exit 0
