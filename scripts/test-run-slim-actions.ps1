# Exercise process-only actions without an SDK or access to live app processes.
$ErrorActionPreference = 'Stop'
$originalLocalAppData = $env:LOCALAPPDATA
$isolatedAppData = Join-Path ([IO.Path]::GetTempPath()) ('tracker-action-test-' + [Guid]::NewGuid().ToString('N'))

function dotnet { throw 'Process-only actions must not invoke the SDK.' }
function Get-Process {
    [CmdletBinding()]
    param([string]$Name, [int]$Id)
    return @()
}
function Stop-Process { throw 'The fixture has no processes to stop.' }

try {
    $env:LOCALAPPDATA = $isolatedAppData
    foreach ($action in @('status', 'stop')) {
        & "$PSScriptRoot/run-slim-and-monitor.ps1" -Action $action
    }
    Write-Host 'Process-only actions passed without SDK resolution or live process access.'
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
}
