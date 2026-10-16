<# 在安装时保存 UTF-8 JSON，在升级前读取已选目录；中间文件使用 UTF-16LE。 #>
param(
    [Parameter(Mandatory = $true)][ValidateSet('Read', 'Write')][string]$Mode,
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [Parameter(Mandatory = $true)][string]$ValueFile
)
$ErrorActionPreference = 'Stop'
try {
    if ($Mode -eq 'Read') {
        $config = [IO.File]::ReadAllText($ConfigPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        $directory = $config.dataDirectory
    }
    else {
        $directory = [IO.File]::ReadAllText($ValueFile, [Text.Encoding]::Unicode)
    }
    # NSIS 变量最多保存 1023 个字符；拒绝损坏配置，避免升级时改用错误目录。
    if ($directory -isnot [string] -or [string]::IsNullOrWhiteSpace($directory) -or
        $directory.Length -gt 1023 -or $directory -match '[\x00-\x1f]' -or
        $directory -notmatch '^(?:[A-Za-z]:[\\/]|[\\/]{2}[^\\/]+[\\/][^\\/]+)' -or
        $directory.IndexOfAny([IO.Path]::GetInvalidPathChars()) -ge 0) {
        throw '数据目录必须是有效的绝对路径。'
    }
    if ($Mode -eq 'Read') {
        [IO.File]::WriteAllText($ValueFile, $directory, [Text.Encoding]::Unicode)
    }
    else {
        $json = [ordered]@{ dataDirectory = $directory } | ConvertTo-Json
        [IO.File]::WriteAllText($ConfigPath, $json, (New-Object Text.UTF8Encoding $false))
    }
}
catch {
    Write-Error '无法读写数据目录 JSON 配置。' -ErrorAction Continue
    exit 1
}
