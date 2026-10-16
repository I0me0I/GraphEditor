# GraphEditor self-test build + run script.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1 -Only TestHitTesting
#
# Compiles the application sources together with tests\TestRunner.cs into a
# console executable and runs it. Exit code 0 means every check passed.
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads BOM-less
# script files using the ANSI code page, which corrupts non-ASCII text.

[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Only
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'build\tests' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

# The harness writes UTF-8 (it sets Console.OutputEncoding itself). Tell the
# parent console the same, otherwise the captured summary line is mojibake and
# the pass/fail pattern below cannot match.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ }
if (-not $compilerCandidates) { throw 'Cannot find csc.exe.' }
$compiler = @($compilerCandidates)[0]

# Application sources plus the self-test entry point only
# (ScreenshotTool.cs has its own Main and is built separately).
$sources = @()
$sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'src\GraphEditor') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$sources += (Join-Path $root 'tests\TestRunner.cs')
if (-not (Test-Path -LiteralPath $sources[-1])) { throw "Test source not found: $($sources[-1])" }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$exePath = Join-Path $OutDir 'GraphEditorTests.exe'

$arguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/warn:4',
    '/codepage:65001',
    '/utf8output',
    '/main:GraphEditor.Tests.TestProgram',
    "/out:$exePath"
) + $sources

Write-Host "compiler : $compiler"
Write-Host "output   : $exePath"
Write-Host "sources  : $($sources.Count)"
Write-Host ''

& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed (csc exit code $LASTEXITCODE)." }

# The harness creates WinForms controls without a message loop. Their teardown
# during CLR shutdown can raise an escaping window-callback exception
# (0xC000041D) and clobber the process exit code, so the pass/fail verdict is
# taken from the harness's summary line instead.
$runArguments = @($root)
if ($Only) { $runArguments += "/only:$Only" }
$lines = @(& $exePath @runArguments 2>&1)
$lines | ForEach-Object { Write-Host $_ }

$summary = ($lines | Select-String -Pattern 'RESULT passed=(\d+) failed=(\d+)' | Select-Object -Last 1)
if (-not $summary) {
    Write-Host ''
    Write-Host 'TESTS FAILED (no RESULT line; the harness did not finish)'
    exit 1
}

$passed = [int]$summary.Matches[0].Groups[1].Value
$failed = [int]$summary.Matches[0].Groups[2].Value

Write-Host ''
if ($failed -eq 0) {
    Write-Host "TESTS PASSED ($passed checks)"
    exit 0
}
Write-Host "TESTS FAILED ($passed passed, $failed failed)"
exit 1
