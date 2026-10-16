# Registers the .graph file type for the current user (no administrator needed).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\register-filetype.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\register-filetype.ps1 -Unregister
#
# Writes only under HKCU:\Software\Classes, so it affects the current user only.
# NOTE: keep this file ASCII-only (Windows PowerShell 5.1 reads BOM-less
# scripts using the ANSI code page).

[CmdletBinding()]
param(
    [string]$ExePath,
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $root 'build\GraphEditor.exe' }
$ExePath = [System.IO.Path]::GetFullPath($ExePath)

$progId = 'GraphEditor.GraphFile'
$friendlyName = 'GraphEditor Graph File'
$classes = 'HKCU:\Software\Classes'

if ($Unregister) {
    foreach ($key in @("$classes\$progId", "$classes\.graph")) {
        if (Test-Path -LiteralPath $key) {
            Remove-Item -LiteralPath $key -Recurse -Force
            Write-Host "removed $key"
        }
    }
    Write-Host ''
    Write-Host '.graph association removed for the current user.'
    return
}

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "GraphEditor.exe not found at $ExePath. Run build.ps1 first, or pass -ExePath."
}

# .graph -> ProgId
New-Item -Path "$classes\.graph" -Force | Out-Null
Set-ItemProperty -Path "$classes\.graph" -Name '(default)' -Value $progId
New-Item -Path "$classes\.graph\OpenWithProgids" -Force | Out-Null
Set-ItemProperty -Path "$classes\.graph\OpenWithProgids" -Name $progId -Value ([byte[]]@()) -Type Binary

# ProgId -> display name + icon + open command
New-Item -Path "$classes\$progId" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId" -Name '(default)' -Value $friendlyName
New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId\DefaultIcon" -Name '(default)' -Value ("$ExePath,0")
New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name '(default)' -Value ('"' + $ExePath + '" "%1"')

# Tell Explorer that the association changed
try {
    Add-Type -Namespace Win32 -Name Shell -MemberDefinition @'
[DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, int uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    [Win32.Shell]::SHChangeNotify(0x08000000, 0x0000, [System.IntPtr]::Zero, [System.IntPtr]::Zero)
} catch {
    Write-Host 'note: could not notify Explorer; a sign-out may be needed for the new icon to appear.'
}

Write-Host ''
Write-Host "Registered .graph -> $ExePath"
Write-Host 'Double-clicking a .graph file will now open it in GraphEditor.'
