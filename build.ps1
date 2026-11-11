# 闪启 Launcher 一键构建
#   源码：.\app\
#   成品：.\release\            框架依赖版（目标机需装 .NET 9 Desktop Runtime）
#         .\release-standalone\ 自包含版（内含运行时，拷走即用）
#
# 用法：
#   .\build.ps1                 构建 Debug + 同步到 release
#   .\build.ps1 -Standalone     另外发布自包含版到 release-standalone
#   .\build.ps1 -Configuration Release

param(
    [string]$Configuration = "Debug",
    [switch]$Standalone,
    [switch]$Installer,
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$app = Join-Path $root "app"
$sln = Join-Path $app "Flow.Launcher.sln"
$releaseDir = Join-Path $root "release"
$standaloneDir = Join-Path $root "release-standalone"

function Sync-Output {
    param([string]$From, [string]$To)

    if (-not (Test-Path $From)) {
        throw "找不到构建输出：$From"
    }

    if (Test-Path $To) { Remove-Item $To -Recurse -Force }
    New-Item -ItemType Directory -Path $To -Force | Out-Null
    Copy-Item (Join-Path $From "*") $To -Recurse -Force
}

Write-Host "==> 构建 $Configuration" -ForegroundColor Cyan
dotnet build $sln -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败，已中止。" -ForegroundColor Red
    exit 1
}

Write-Host "==> 同步到 release" -ForegroundColor Cyan
Sync-Output -From (Join-Path $app "Output\$Configuration") -To $releaseDir

if ($Standalone) {
    Write-Host "==> 发布自包含版（win-x64，内含运行时）" -ForegroundColor Cyan

    # 自包含版固定用 Release 产物，插件也要先有 Release 版
    if ($Configuration -ne "Release") {
        dotnet build $sln -c Release --nologo | Out-Null
    }

    $outDir = Join-Path $root "release-standalone"

    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    dotnet publish (Join-Path $app "Flow.Launcher\Flow.Launcher.csproj") `
        -c Release -r win-x64 --self-contained true -o $outDir --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "自包含发布失败。" -ForegroundColor Red
        exit 1
    }

    # publish 只产出主程序，插件要单独拷进去
    $pluginDst = Join-Path $outDir "Plugins"
    New-Item -ItemType Directory -Path $pluginDst -Force | Out-Null
    Copy-Item (Join-Path $app "Output\Release\Plugins\*") $pluginDst -Recurse -Force

    Write-Host "==> 自包含版：$(Join-Path $outDir 'Flow.Launcher.exe')" -ForegroundColor Green
}

if ($Installer) {
    Write-Host "==> 生成安装包（WiX）" -ForegroundColor Cyan

    if (-not (Test-Path (Join-Path $standaloneDir "Flow.Launcher.exe"))) {
        Write-Host "缺少 release-standalone，请先加 -Standalone 打包。" -ForegroundColor Red
        exit 1
    }

    $wix = (Get-Command wix -ErrorAction SilentlyContinue)
    if (-not $wix) {
        Write-Host "找不到 wix 命令，请先执行：dotnet tool install --global wix --version 5.0.2" -ForegroundColor Red
        exit 1
    }

    $wxs = Join-Path $root "installer\installer.wxs"
    $msi = Join-Path $root "ShanQiLauncher-Setup-$Version.msi"
    $setupExe = Join-Path $root "ShanQiLauncher-Setup-$Version.exe"

    wix build $wxs -o $msi -d Version=$Version
    if ($LASTEXITCODE -ne 0) {
        Write-Host "MSI 生成失败。" -ForegroundColor Red
        exit 1
    }

    # 用 .NET Framework 自带 csc 把 MSI 包成 exe 引导器（目标机无需额外运行时）
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $icon = Join-Path $app "Flow.Launcher\Resources\app.ico"

    # 生成版本信息，让 exe 属性页显示产品名/版本/公司
    $fileVer = if ($Version -match '^\d+\.\d+\.\d+\.\d+$') { $Version } else { "$Version.0" }
    $verSrc = Join-Path $env:TEMP "ShanQiLauncherSetupVersionInfo.cs"
    $verCode = @"
using System.Reflection;
[assembly: AssemblyTitle("闪启 Launcher 安装程序")]
[assembly: AssemblyDescription("闪启 Launcher 安装程序")]
[assembly: AssemblyProduct("闪启 Launcher")]
[assembly: AssemblyCompany("闪启")]
[assembly: AssemblyCopyright("Copyright (C) 2026 闪启")]
[assembly: AssemblyVersion("$fileVer")]
[assembly: AssemblyFileVersion("$fileVer")]
"@
    [System.IO.File]::WriteAllText($verSrc, $verCode, (New-Object System.Text.UTF8Encoding($true)))

    & $csc /nologo /target:winexe "/out:$setupExe" "/win32icon:$icon" `
        "/resource:$msi,app.msi" "/resource:$icon,setup.ico" `
        (Join-Path $root "installer\Bootstrapper.cs") $verSrc `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll

    Remove-Item $verSrc -Force -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) {
        Write-Host "exe 安装包生成失败。" -ForegroundColor Red
        exit 1
    }

    Write-Host "==> 安装包：$setupExe" -ForegroundColor Green
}

Write-Host "==> 完成：$(Join-Path $releaseDir 'Flow.Launcher.exe')" -ForegroundColor Green
