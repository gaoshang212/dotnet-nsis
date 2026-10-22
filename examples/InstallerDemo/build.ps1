param(
    [ValidatePattern('^(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*)){2,3}$')]
    [string]$Version = '1.0.0.0',
    [switch]$CurrentUser,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo 'artifacts/installer-demo'
$payload = Join-Path $output 'payload'
$support = Join-Path $output 'support'
$utf8 = New-Object Text.UTF8Encoding($false)

# 仅清理本 Demo 的生成输入，旧安装包留作修复、升级和降级测试。
foreach ($folder in @($payload, $support)) {
    $resolved = [IO.Path]::GetFullPath($folder)
    if (-not $resolved.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "生成目录越界：$resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

# 一个自包含演示程序提供五个可运行入口和安装辅助命令，不依赖目标机 .NET。
dotnet publish (Join-Path $PSScriptRoot 'App/InstallerDemo.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:DebugType=None' -o $support
if ($LASTEXITCODE -ne 0) { throw 'Demo 发布失败。' }
$programs = @('Neuracle.InstallerDemo.View', 'Neuracle.InstallerDemo.MasterPC.View', 'Neuracle.InstallerDemo.Simulator.View',
    'Neuracle.InstallerDemo.MonitorCalibrate.View', 'Neuracle.InstallerDemo.Stimulator.VEPStim')
foreach ($name in $programs) {
    $app = Join-Path $payload "$name/net8.0-windows"
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $support 'InstallerDemo.exe') -Destination (Join-Path $app "$name.exe")
    # 保留真实 Release 中各模块共用的配置目录树，内容使用 Demo 自己的配置。
    foreach ($directory in @('Conf/Factory/Picture', 'Conf/Factory/Report', 'Conf/Factory/SystemConf',
        'Conf/Factory/User/Study', 'Conf/Factory/User/User', 'Conf/System/Picture',
        'Conf/System/Study', 'Conf/System/System', 'Data', 'Log')) {
        New-Item -ItemType Directory -Path (Join-Path $payload "$name/$directory") -Force | Out-Null
    }
}
$settings = Join-Path $payload 'Neuracle.InstallerDemo.View/Conf/System/System/SystemSetting.json'
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($settings)) -Force | Out-Null
[IO.File]::WriteAllText($settings, (@{ DataPath = '..\Data'; HospitalName = '演示医院'; DemoOnly = $true; Nested = @{ Preserve = '保留未知字段' } } | ConvertTo-Json -Depth 5), $utf8)
New-Item -ItemType Directory -Path (Join-Path $payload 'Neuracle.InstallerDemo.View/net8.0-windows/fonts') -Force | Out-Null

# 卸载只删除随包分发的非配置文件，禁止递归删除安装树。
$manifest = New-Object 'Collections.Generic.List[string]'
$manifest.Add('; 自动生成：仅删除本版本随包程序文件。')
foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse) {
    $relative = $file.FullName.Substring($payload.Length + 1)
    if ($relative -match '(^|\\)(Conf|Data|Log)(\\|$)') { continue }
    $escaped = $relative.Replace('$', '$$')
    $manifest.Add(('Delete "$INSTDIR\{0}"' -f $escaped))
}
foreach ($directory in (Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending)) {
    $relative = $directory.FullName.Substring($payload.Length + 1).Replace('$', '$$')
    if ($relative -match '(^|\\)(Conf|Data|Log)(\\|$)') { continue }
    $manifest.Add(('RMDir "$INSTDIR\{0}"' -f $relative))
}
[IO.File]::WriteAllLines((Join-Path $output 'remove-files.nsh'), $manifest, $utf8)

# 生成配置的绝对路径仅用于本次构建；仓库示例保持相对路径。
$config = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'nsis.yml'))
$config = $config.Replace('version: 1.0.0.0', "version: $Version")
$config = $config.Replace('../../artifacts/installer-demo/payload', "'$($payload.Replace('\', '/').Replace("'", "''"))'")
$config = $config.Replace('../../artifacts/installer-demo/installers', "'$($output.Replace('\', '/').Replace("'", "''"))/installers'")
$include = (Join-Path $PSScriptRoot 'build/installer.nsh').Replace('\', '/').Replace("'", "''")
$config = $config.Replace('./build/installer.nsh', "'$include'")
if ($CurrentUser) { $config = $config.Replace('perMachine: true', 'perMachine: false') }
$configPath = Join-Path $output 'nsis.yml'
[IO.File]::WriteAllText($configPath, $config, $utf8)
if ($PrepareOnly) { return }
dotnet run --project (Join-Path $repo 'src/DotnetNsis') -- build --config $configPath
if ($LASTEXITCODE -ne 0) { throw 'Demo 打包失败。' }
