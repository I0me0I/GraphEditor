# Push local commits to GitHub.
#
# Run this in a NORMAL PowerShell window (not inside the AI sandbox):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\push.ps1
#
# Why it exists: inside the AI sandbox the Windows credential store cannot be
# reached, and the msys2 ssh that Git ships with cannot create its signal pipe,
# so authentication always fails there. In a normal session Git Credential
# Manager works as usual.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$remote = 'https://github.com/I0me0I/GraphEditor.git'

# GitHub pushes are often reset on a direct connection; use the local proxy
# when one is listening on the usual port.
$proxy = 'http://127.0.0.1:7890'
$proxyUp = $false
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $proxyUp = $client.ConnectAsync('127.0.0.1', 7890).Wait(600) -and $client.Connected
    $client.Close()
} catch {
    $proxyUp = $false
}

Write-Host ('remote : ' + $remote)
if ($proxyUp) {
    Write-Host ('proxy  : ' + $proxy + ' (in use)')
} else {
    Write-Host 'proxy  : not running, direct connection'
}
Write-Host ''

$pending = 0
try { $pending = [int](git rev-list --count 'origin/main..HEAD' 2>$null) } catch { $pending = 0 }
if ($pending -eq 0) {
    try { $pending = [int](git rev-list --count HEAD 2>$null) } catch { }
}
Write-Host ('commits to push: ' + $pending)
Write-Host ''

$gitArgs = @()
if ($proxyUp) { $gitArgs += @('-c', ('http.proxy=' + $proxy)) }
$gitArgs += @('push', '-u', 'origin', 'main')

Write-Host ('git ' + ($gitArgs -join ' '))
& git @gitArgs
$code = $LASTEXITCODE

if ($code -eq 0) {
    Write-Host ''
    Write-Host 'PUSH OK' -ForegroundColor Green
    & git log --oneline -1
} else {
    Write-Host ''
    Write-Host ('PUSH FAILED (exit ' + $code + ')') -ForegroundColor Red
    Write-Host 'If it says "Connection was reset", start your proxy and run again.'
    Write-Host 'If it asks for credentials, sign in with a GitHub Personal Access Token.'
}

exit $code
