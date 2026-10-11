# 用 Windows 內建的 .NET Framework 編譯器把 payload zip 包成安裝程式 EXE。
# 先跑 tools\build_package.py 產生 zip，再執行：
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
param(
    [string]$Version = '1.1'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repo 'dist'
$zip = Join-Path $dist "CONVRGENCE-zh-TW-v$Version.zip"
$exe = Join-Path $dist "CONVRGENCE-zh-TW-v$Version-Setup.exe"
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'

if (-not (Test-Path $zip)) { throw "找不到 $zip，請先執行 tools\build_package.py" }

& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    "/out:$exe" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" `
    "/resource:$zip,payload.zip" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "/r:$(Join-Path $fw 'System.IO.Compression.dll')" `
    (Join-Path $PSScriptRoot 'Installer.cs')
if ($LASTEXITCODE -ne 0) { throw "csc 失敗（$LASTEXITCODE）" }
Get-Item $exe | Select-Object Name, Length
