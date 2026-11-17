# 安装 Interception 键盘驱动（需要管理员权限；由 Windows UAC 确认）。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root 'driver\install-interception.exe'
if (-not (Test-Path $installer)) {
    $installer = Join-Path $root 'thirdparty\Interception\install-interception.exe'
}
if (-not (Test-Path $installer)) { throw "找不到驱动安装器: $installer" }
& $installer install
if ($LASTEXITCODE -ne 0) { throw "驱动安装失败，退出码 $LASTEXITCODE" }
Write-Host 'Interception 驱动安装成功。请重启 MiRemote 后再试语音键。'
Read-Host '按 Enter 关闭'
