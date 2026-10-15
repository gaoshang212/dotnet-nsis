<#
.SYNOPSIS
发布三种架构的示例应用并执行真实工具链测试。
.PARAMETER Install
同时启用当前用户安装、升级、启动和卸载测试，会创建并清理临时系统项。
.PARAMETER Filter
可选的 dotnet test 筛选表达式，用于只运行相关测试。
#>
param([switch]$Install, [string]$Filter)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$fixtureRoot = Join-Path $repo 'artifacts/fixtures'
# 为每个目标架构准备自包含应用，安装测试无需依赖本机对应架构的运行时。
foreach ($arch in @('x64', 'x86', 'arm64')) {
    & dotnet publish (Join-Path $repo 'examples/HelloApp/HelloApp.csproj') -r "win-$arch" --self-contained true -p:PublishTrimmed=true -p:DebugType=None -o (Join-Path $fixtureRoot $arch)
    if ($LASTEXITCODE -ne 0) { throw "示例应用发布失败：$arch" }
}
# 保存调用者环境，测试结束或失败后统一恢复。
$oldIntegration = $env:DOTNET_NSIS_INTEGRATION
$oldInstall = $env:DOTNET_NSIS_INSTALL_TESTS
$oldFixtures = $env:DOTNET_NSIS_FIXTURES
try {
    # 普通单元测试默认跳过这些用例；仅在本脚本中显式启用所需测试。
    $env:DOTNET_NSIS_INTEGRATION = '1'
    $env:DOTNET_NSIS_INSTALL_TESTS = if ($Install) { '1' } else { '0' }
    $env:DOTNET_NSIS_FIXTURES = $fixtureRoot
    $testArgs = @('test', (Join-Path $repo 'DotnetNsis.slnx'), '--logger', 'trx;LogFileName=integration.trx')
    if ($Filter) { $testArgs += @('--filter', $Filter) }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw '集成测试失败，请查看 TestResults。' }
}
finally {
    $env:DOTNET_NSIS_INTEGRATION = $oldIntegration
    $env:DOTNET_NSIS_INSTALL_TESTS = $oldInstall
    $env:DOTNET_NSIS_FIXTURES = $oldFixtures
}
