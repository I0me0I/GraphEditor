# GraphEditor screenshot harness.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\screenshot.ps1
#
# Builds the app, launches it, captures the real window and closes it again.
# Output: build\shot\app.png

[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'build\shot' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ }
if (-not $compilerCandidates) { throw 'Cannot find csc.exe.' }
$compiler = @($compilerCandidates)[0]

# 1) build the app
& (Join-Path $root 'build.ps1') -OutDir (Join-Path $OutDir 'app')
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }

# 2) build the screenshot tool
$sources = @()
$sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'src\GraphEditor') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$sources += (Join-Path $root 'tests\ScreenshotTool.cs')

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$toolPath = Join-Path $OutDir 'ScreenshotTool.exe'
& $compiler /nologo /target:exe /platform:anycpu /codepage:65001 /utf8output `
    '/main:GraphEditor.Tests.ScreenshotTool' "/out:$toolPath" @sources
if ($LASTEXITCODE -ne 0) { throw 'Screenshot tool build failed.' }

# 3) capture
$appPath = Join-Path $OutDir 'app\GraphEditor.exe'
$pngPath = Join-Path $OutDir 'app.png'
& $toolPath $appPath $pngPath 2500
exit $LASTEXITCODE
