# =====================================================================
#  太阳能光伏计算器 —— 一键编译脚本
#  用法:  powershell -ExecutionPolicy Bypass -File .\build.ps1
#         powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
# =====================================================================
param(
    [switch]$Test,          # 编译后立即运行自检
    [switch]$Clean          # 先清除 bin 目录
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# ---- 定位 C# 编译器（.NET Framework 自带，无需安装 SDK） ----
$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw "未找到 csc.exe，请确认已安装 .NET Framework 4.x"
}
Write-Host "编译器: $csc" -ForegroundColor Cyan

if ($Clean -and (Test-Path 'bin')) { Remove-Item 'bin' -Recurse -Force }
New-Item -ItemType Directory -Force -Path 'bin' | Out-Null

$refs = @('/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
$common = @('/nologo', '/codepage:65001', '/platform:anycpu')

# ---- 1) 主程序（Windows 窗体程序） ----
Write-Host "`n[1/2] 编译主程序 SolarCalculator.exe ..." -ForegroundColor Cyan
& $csc @common '/target:winexe' '/optimize+' '/out:bin\SolarCalculator.exe' @refs `
    'src\SolarMath.cs' 'src\SolarForm.cs'
if ($LASTEXITCODE -ne 0) { throw "主程序编译失败" }
Copy-Item 'src\app.config' 'bin\SolarCalculator.exe.config' -Force

# ---- 2) 自检程序（控制台） ----
Write-Host "[2/2] 编译自检程序 SolarTests.exe ..." -ForegroundColor Cyan
& $csc @common '/target:exe' '/out:bin\SolarTests.exe' `
    'src\SolarMath.cs' 'tests\SolarTests.cs'
if ($LASTEXITCODE -ne 0) { throw "自检程序编译失败" }

Write-Host "`n编译完成：" -ForegroundColor Green
Get-ChildItem 'bin' | Select-Object Name, Length | Format-Table -AutoSize

if ($Test) {
    Write-Host "运行自检 ..." -ForegroundColor Cyan
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    & '.\bin\SolarTests.exe'
    exit $LASTEXITCODE
}
Write-Host "双击 bin\SolarCalculator.exe 即可运行。" -ForegroundColor Green
