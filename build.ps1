# GraphEditor build script.
# Uses the C# compiler that ships with .NET Framework 4.8, so no .NET SDK is required.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#
# Output: build\GraphEditor.exe

[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'build' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

# ---------- locate the C# compiler ----------
$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ }
if (-not $compilerCandidates) {
    throw 'Cannot find the .NET Framework C# compiler (csc.exe). Please enable .NET Framework 4.x.'
}
$compiler = @($compilerCandidates)[0]

# ---------- locate reference assemblies (highest version in the GAC) ----------
function Get-FrameworkReference {
    param([Parameter(Mandatory = $true)][string]$SimpleName)

    $folders = @(
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$SimpleName"),
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_32\$SimpleName"),
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_64\$SimpleName")
    )
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        $found = @(Get-ChildItem -LiteralPath $folder -Recurse -Filter "$SimpleName.dll" -File -ErrorAction SilentlyContinue)
        if ($found.Count -gt 0) {
            $scored = $found | ForEach-Object {
                $versionText = ($_.Directory.Name -split '_')[0]
                [pscustomobject]@{ Path = $_.FullName; Version = $versionText }
            }
            $best = $scored | Sort-Object -Property Version | Select-Object -Last 1
            return $best.Path
        }
    }
    $direct = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\$SimpleName.dll"
    if (Test-Path -LiteralPath $direct) { return $direct }
    throw "Cannot find reference assembly: $SimpleName"
}

# ---------- reference assemblies ----------
# csc.exe already references mscorlib, System, System.Core, System.Xml,
# System.Drawing and System.Windows.Forms from its own directory, so nothing
# extra is needed here. Adding them again causes CS1703 (duplicate assembly).
$references = @()

# ---------- collect source files ----------
$sourceDir = Join-Path $root 'src\GraphEditor'
$sources = @(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'No source files found.' }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$exePath = Join-Path $OutDir 'GraphEditor.exe'

# ---------- application icon (optional) ----------
$iconPath = Join-Path $root 'assets\GraphEditor.ico'
$iconArgument = @()
if (Test-Path -LiteralPath $iconPath) {
    $iconArgument = @("/win32icon:$iconPath")
    Copy-Item -LiteralPath $iconPath -Destination (Join-Path $OutDir 'GraphEditor.ico') -Force
} else {
    Write-Host 'note: assets\GraphEditor.ico not found; building without a custom icon'
}

# ---------- compile ----------
# Compile to a temp file, then swap it into place. csc cannot overwrite the final
# exe while an antivirus/indexer still holds a handle on it (CS0016) - and that
# happens often right after a previous build wrote it. Swapping by rename avoids
# the conflict, and keeps a working exe if compilation fails.
$tempExe = Join-Path $OutDir 'GraphEditor.build.tmp.exe'
Remove-Item -LiteralPath $tempExe -Force -ErrorAction SilentlyContinue

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/codepage:65001',
    '/utf8output',
    "/out:$tempExe"
) + $iconArgument + $references + $sources

Write-Host "compiler : $compiler"
Write-Host "output   : $exePath"
Write-Host "sources  : $($sources.Count)"
Write-Host ''

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $tempExe -Force -ErrorAction SilentlyContinue
    throw "Compilation failed (csc exit code $LASTEXITCODE)."
}

# Swap the freshly built exe into place. A scanner (Windows Defender on-access
# scan) often keeps a handle on the previous exe for a while: that handle allows
# renaming but forbids overwriting, which makes csc fail with CS0016 if we build
# straight to the final name. Hence: build to a temp name, then delete + rename,
# retrying for a few seconds.
$swapped = $false
for ($attempt = 1; $attempt -le 15 -and -not $swapped; $attempt++) {
    try {
        if (Test-Path -LiteralPath $exePath) {
            Remove-Item -LiteralPath $exePath -Force -ErrorAction Stop
        }
        Move-Item -LiteralPath $tempExe -Destination $exePath -ErrorAction Stop
        $swapped = $true
    } catch {
        Start-Sleep -Milliseconds 400
    }
}

if (-not $swapped) {
    # Never leave the user with no exe at all: keep the temp file as the new name.
    $fallback = Join-Path $OutDir 'GraphEditor.new.exe'
    Remove-Item -LiteralPath $fallback -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $tempExe -Destination $fallback -Force -ErrorAction SilentlyContinue
    Write-Host ''
    Write-Host 'WARNING: the existing GraphEditor.exe is locked by another process'
    Write-Host '         (usually an antivirus scan of the previous build).'
    Write-Host "         The new build was written to: $fallback"
    Write-Host '         Close any running GraphEditor.exe and run this script again.'
    exit 0
}

$size = (Get-Item -LiteralPath $exePath).Length
Write-Host ''
Write-Host ("BUILD OK -> {0} ({1:N0} bytes)" -f $exePath, $size)

# ---------- copy the user-facing files next to the exe ----------
foreach ($extra in @('README.md', 'demand.md', 'LICENSE')) {
    $candidate = Join-Path $root $extra
    if (Test-Path -LiteralPath $candidate) {
        Copy-Item -LiteralPath $candidate -Destination $OutDir -Force
    }
}
# docs/ (troubleshooting + screenshots) ships alongside so the README links resolve
$docsSrc = Join-Path $root 'docs'
if (Test-Path -LiteralPath $docsSrc) {
    $docsDst = Join-Path $OutDir 'docs'
    if (Test-Path -LiteralPath $docsDst) { Remove-Item -LiteralPath $docsDst -Recurse -Force }
    Copy-Item -LiteralPath $docsSrc -Destination $docsDst -Recurse -Force
}
Write-Host ("run: `"{0}`"" -f $exePath)
