#Requires -Version 5.1
<#
.SYNOPSIS
    安装 / 更新 OhMyTile（松散文件注册，无需打包、无需管理员）。

.DESCRIPTION
    OhMyTile 走的是「松散文件 + AppxManifest.xml 注册」（Add-AppxPackage -Register），
    因此没有 .appx 需要签名，也不需要把证书装进受信任存储。
    但必须先在系统里开启「开发者模式 / 从任意源安装应用」。

    本脚本会：
      1. 检查开发者模式是否开启（未开启则给出开启步骤并退出）
      2. 卸载由旧发布者（CN=Developer）注册的同名包，避免出现两份
      3. 注册 app\AppxManifest.xml
      4. 打印注册结果

.PARAMETER AppDir
    app 文件夹路径（含 OhMyTile.exe 与 AppxManifest.xml）。
    默认取脚本同级的 app 目录。

.PARAMETER Uninstall
    取消注册（卸载），并可选一并删除已固定的磁贴缓存说明。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall

.NOTES
    注册后请勿移动或删除本文件夹：松散文件应用直接引用该目录，删了就启动不了。
#>
[CmdletBinding()]
param(
    [string]$AppDir,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$PackageName    = 'OhMyTile.Final.Project'
$ExpectedPublisher = 'CN=F3rcc'
$Root           = $PSScriptRoot

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ---------- 卸载 ----------
if ($Uninstall) {
    $pkgs = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if (-not $pkgs) {
        Write-Host '未注册 OhMyTile，无需卸载。' -ForegroundColor Yellow
        return
    }
    foreach ($p in @($pkgs)) {
        Write-Host "取消注册：$($p.PackageFullName)" -ForegroundColor Cyan
        Remove-AppxPackage -Package $p.PackageFullName
    }
    Write-Host '已卸载。' -ForegroundColor Green
    return
}

# ---------- 定位 app 目录 ----------
if (-not $AppDir) { $AppDir = Join-Path $Root 'app' }
if (-not (Test-Path $AppDir)) { throw "找不到 app 目录：$AppDir" }
$AppDir = (Resolve-Path $AppDir).Path

$manifest = Join-Path $AppDir 'AppxManifest.xml'
if (-not (Test-Path $manifest)) { throw "找不到 AppxManifest.xml：$manifest" }

$exe = Join-Path $AppDir 'OhMyTile.exe'
if (-not (Test-Path $exe)) { throw "找不到 OhMyTile.exe：$exe" }

# ---------- 检查开发者模式 ----------
$unlock = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
$devMode = ($unlock.AllowDevelopmentWithoutDevLicense -eq 1) -or ($unlock.AllowAllTrustedApps -eq 1)

if (-not $devMode) {
    Write-Host ''
    Write-Host '未开启「开发者模式 / 从任意源安装应用」，松散文件注册会失败（0x80073CFF）。' -ForegroundColor Red
    Write-Host '请手动开启后重试：' -ForegroundColor Yellow
    Write-Host '  设置 → 更新和安全 → 开发者选项 → 勾选「从任意源（包括松散文件）安装应用」' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '或用管理员 PowerShell 执行：' -ForegroundColor Yellow
    Write-Host '  reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v AllowDevelopmentWithoutDevLicense /d 1' -ForegroundColor DarkGray
    if (-not (Test-Admin)) {
        Write-Host ''
        Write-Host '（本脚本以普通权限运行，不会擅自修改该安全设置）' -ForegroundColor DarkGray
    }
    exit 1
}

# ---------- 清理旧发布者（CN=Developer）----------
$existing = @(Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue)
foreach ($p in $existing) {
    if ($p.Publisher -ne $ExpectedPublisher) {
        Write-Host "发现旧发布者版本：$($p.PackageFullName)（$($p.Publisher)），先卸载以免重复" -ForegroundColor Yellow
        Remove-AppxPackage -Package $p.PackageFullName
    }
}

# ---------- 注册 ----------
Write-Host "注册 $manifest ..." -ForegroundColor Cyan
try {
    Add-AppxPackage -Register $manifest -ErrorAction Stop
} catch {
    Write-Host ''
    Write-Host '注册失败。常见原因：' -ForegroundColor Red
    Write-Host '  1) 开发者模式未开启（见上）' -ForegroundColor Yellow
    Write-Host '  2) 文件夹被移动/删除，或缺少 Assets 里的图片资源' -ForegroundColor Yellow
    Write-Host '  3) 同名包被占用：先运行本脚本 -Uninstall 再装' -ForegroundColor Yellow
    throw
}

$pkg = Get-AppxPackage -Name $PackageName | Select-Object -First 1
if (-not $pkg) { throw '注册命令未报错，但查不到包，请检查事件查看器。' }

Write-Host ''
Write-Host '注册成功' -ForegroundColor Green
Write-Host "  名称    ：$($pkg.Name)"
Write-Host "  版本    ：$($pkg.Version)"
Write-Host "  发布者  ：$($pkg.Publisher)"
Write-Host "  安装位置：$($pkg.InstallLocation)"
Write-Host ''
Write-Host '从开始菜单搜索 OhMyTile 即可启动。' -ForegroundColor Green
Write-Host '注意：不要移动或删除本文件夹，松散文件应用直接引用该目录。' -ForegroundColor DarkYellow
