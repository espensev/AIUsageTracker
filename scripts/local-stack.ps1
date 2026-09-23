<#
.SYNOPSIS
Deploys or inspects the local Monitor + Web stack that Task Scheduler keeps running.

.DESCRIPTION
deploy (default) publishes AIUsageTracker.Monitor and AIUsageTracker.Web from this checkout
(Release, framework-dependent) into a new release directory, points the two scheduled tasks at
it, restarts them in the safe order (Web down, Monitor down, swap, Monitor up, Web up) and
verifies the dashboard answers. It prints the previous release and the exact rollback commands.

status prints the tasks, running processes, listening ports, monitor metadata and the
dashboard's view of the Monitor.

The tasks are found by what they run (AIUsageTracker.Monitor.exe / AIUsageTracker.Web.exe).
When no tasks exist they are created under -TaskFolder (default \SevGrp\AIUsageTracker\): Monitor at
boot (S4U, no stored password), Web at logon (hidden), both restarting on failure.

.EXAMPLE
pwsh -File scripts/local-stack.ps1            # deploy HEAD and restart the stack
pwsh -File scripts/local-stack.ps1 -Action status
#>
[CmdletBinding()]
param(
    [ValidateSet('deploy', 'status')]
    [string]$Action = 'deploy',
    [string]$ReleaseRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\AIUsageTracker\Web\releases'),
    [string]$TaskFolder = '\SevGrp\AIUsageTracker\',
    [string]$WebUrl = 'http://localhost:5100',
    [int]$MonitorPort = 5000,
    [int]$TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:MonitorExecutable = 'AIUsageTracker.Monitor.exe'
$script:WebExecutable = 'AIUsageTracker.Web.exe'
$script:RequiredReleaseFiles = @(
    'AIUsageTracker.Monitor.exe',
    'AIUsageTracker.Monitor.dll',
    'AIUsageTracker.Monitor.runtimeconfig.json',
    'AIUsageTracker.Monitor.deps.json',
    'AIUsageTracker.Web.exe',
    'AIUsageTracker.Web.dll',
    'AIUsageTracker.Web.runtimeconfig.json',
    'AIUsageTracker.Web.deps.json',
    'appsettings.json'
)
$script:RequiredReleaseDirectories = @('wwwroot')

# ---------------------------------------------------------------------------
# Pure helpers (covered by local-stack.Tests.ps1)
# ---------------------------------------------------------------------------

function Get-StackReleaseName {
    param(
        [Parameter(Mandatory)] [string]$CommitSha,
        [Parameter(Mandatory)] [datetime]$Timestamp,
        [string[]]$ExistingNames = @(),
        [switch]$Dirty
    )

    $shortSha = $CommitSha.Substring(0, [Math]::Min(8, $CommitSha.Length))
    $name = '{0}-{1:yyyyMMdd}' -f $shortSha, $Timestamp
    if ($Dirty) {
        $name += '-dirty'
    }
    if ($ExistingNames -contains $name) {
        $name += '-{0:HHmmss}' -f $Timestamp
    }
    return $name
}

function Test-StackRelease {
    <# Returns the required files/directories that are missing from a release directory. #>
    param([Parameter(Mandatory)] [string]$Path)

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $script:RequiredReleaseFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $file) -PathType Leaf)) {
            $missing.Add($file)
        }
    }
    foreach ($directory in $script:RequiredReleaseDirectories) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $directory) -PathType Container)) {
            $missing.Add($directory)
        }
    }
    return $missing.ToArray()
}

function Get-StackActionExecute {
    param($ActionObject)

    if ($null -eq $ActionObject) { return $null }
    $property = $ActionObject.PSObject.Properties['Execute']
    if ($null -eq $property) { return $null }
    return [string]$property.Value
}

function Get-StackActionProperty {
    param($ActionObject, [string]$Name)

    if ($null -eq $ActionObject) { return '' }
    $property = $ActionObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    return [string]$property.Value
}

function Select-StackTask {
    <# Picks the scheduled task whose exec action runs the given executable name. #>
    param(
        [AllowEmptyCollection()] [AllowNull()] [object[]]$Tasks,
        [Parameter(Mandatory)] [string]$ExecutableName
    )

    foreach ($task in @($Tasks)) {
        if ($null -eq $task) { continue }
        $actionsProperty = $task.PSObject.Properties['Actions']
        if ($null -eq $actionsProperty -or $null -eq $actionsProperty.Value) { continue }
        foreach ($taskAction in @($actionsProperty.Value)) {
            $execute = Get-StackActionExecute -ActionObject $taskAction
            if ([string]::IsNullOrWhiteSpace($execute)) { continue }
            $leaf = Split-Path -Leaf ($execute.Trim('"'))
            if ($leaf -ieq $ExecutableName) {
                return $task
            }
        }
    }
    return $null
}

function Get-StackTaskAction {
    param($Task)

    if ($null -eq $Task) { return $null }
    $actionsProperty = $Task.PSObject.Properties['Actions']
    if ($null -eq $actionsProperty -or $null -eq $actionsProperty.Value) { return $null }
    foreach ($taskAction in @($actionsProperty.Value)) {
        $execute = Get-StackActionExecute -ActionObject $taskAction
        if (-not [string]::IsNullOrWhiteSpace($execute)) { return $taskAction }
    }
    return $null
}

function Get-StackTaskReleaseDirectory {
    <# The directory that holds the executable a task runs, i.e. the release it is pinned to. #>
    param($Task)

    $taskAction = Get-StackTaskAction -Task $Task
    if ($null -eq $taskAction) { return $null }
    $execute = (Get-StackActionExecute -ActionObject $taskAction).Trim('"')
    return Split-Path -Parent $execute
}

function Test-StackStatusPayload {
    <# True when /api/monitor/status says the Monitor runs and speaks a compatible contract.
       Provider health may be degraded (a failing provider is not a deploy failure). #>
    param($Payload)

    if ($null -eq $Payload) { return $false }
    $running = $Payload.PSObject.Properties['isRunning']
    $compatible = $Payload.PSObject.Properties['isContractCompatible']
    if ($null -eq $running -or $null -eq $compatible) { return $false }
    return ([bool]$running.Value -and [bool]$compatible.Value)
}

function ConvertTo-StackRollbackCommand {
    param(
        [Parameter(Mandatory)] [string]$TaskPath,
        [Parameter(Mandatory)] [string]$TaskName,
        [Parameter(Mandatory)] [string]$ReleaseDirectory,
        [Parameter(Mandatory)] [string]$ExecutableName,
        [string]$Arguments = ''
    )

    $execute = Join-Path $ReleaseDirectory $ExecutableName
    $actionText = "New-ScheduledTaskAction -Execute '$execute' -WorkingDirectory '$ReleaseDirectory'"
    if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
        $actionText += " -Argument '$Arguments'"
    }
    return "Set-ScheduledTask -TaskPath '$TaskPath' -TaskName '$TaskName' -Action ($actionText)"
}

function Assert-StackTaskNamespace {
    param(
        [AllowEmptyString()] [string]$Folder,
        [switch]$ExistingTask
    )

    # Existing owner contracts may remain; new registrations use SevGrp only.
    $owners = if ($ExistingTask) { 'SevGrp|Sevnet|AdminTasks' } else { 'SevGrp' }
    if ($Folder -notmatch "^\\($owners)\\[^\\]+(?:\\[^\\]+)*\\?$") {
        throw "task namespace must contain an owner folder under \SevGrp\ (existing \Sevnet\ and \AdminTasks\ owners may remain): $Folder"
    }
    foreach ($segment in $Folder.Trim('\').Split('\')) {
        if ($segment -in @('.', '..') -or $segment -ne $segment.Trim() -or $segment -match '[/:*?"<>|\x00-\x1f]') {
            throw "invalid task namespace segment in: $Folder"
        }
    }
}

# ---------------------------------------------------------------------------
# System access
# ---------------------------------------------------------------------------

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Detail {
    param([string]$Message)
    Write-Host "    $Message"
}

function Get-StackAppDataRoot {
    $configured = $env:AIUSAGETRACKER_LOCAL_APP_DATA_ROOT
    $root = if ([string]::IsNullOrWhiteSpace($configured)) { [Environment]::GetFolderPath('LocalApplicationData') } else { $configured }
    return Join-Path $root 'AIUsageTracker'
}

function Get-StackTasks {
    $all = @(Get-ScheduledTask -ErrorAction SilentlyContinue)
    return [pscustomobject]@{
        Monitor = Select-StackTask -Tasks $all -ExecutableName $script:MonitorExecutable
        Web     = Select-StackTask -Tasks $all -ExecutableName $script:WebExecutable
    }
}

function Get-StackProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name = '$($script:MonitorExecutable)' OR Name = '$($script:WebExecutable)'" -ErrorAction SilentlyContinue)
}

function Get-StackListenerPid {
    param([int]$Port)

    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $listener) { return $null }
    return [int]$listener.OwningProcess
}

function Read-StackMonitorInfo {
    $path = Join-Path (Get-StackAppDataRoot) 'monitor.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "monitor.json is unreadable: $($_.Exception.Message)"
        return $null
    }
}

function Get-StackMonitorStartedAt {
    param($MonitorInfo)

    if ($null -eq $MonitorInfo) { return $null }
    $property = $MonitorInfo.PSObject.Properties['StartedAt']
    if ($null -eq $property) { return $null }
    $parsed = [datetime]::MinValue
    if ([datetime]::TryParseExact([string]$property.Value, 'yyyy-MM-dd HH:mm:ss', [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeLocal, [ref]$parsed)) {
        return $parsed
    }
    return $null
}

function Test-StackProcessAlive {
    param([int]$ProcessId)

    if ($ProcessId -le 0) { return $false }
    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Get-StackWebStatus {
    param([string]$BaseUrl)

    try {
        return Invoke-RestMethod -Uri "$BaseUrl/api/monitor/status" -TimeoutSec 10 -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Get-StackWebHttpCode {
    param([string]$Url)

    try {
        $response = Invoke-WebRequest -Uri $Url -TimeoutSec 15 -UseBasicParsing -ErrorAction Stop
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }
        return 0
    }
}

function Wait-StackCondition {
    param(
        [Parameter(Mandatory)] [string]$Description,
        [Parameter(Mandatory)] [scriptblock]$Condition,
        [int]$Seconds = 60
    )

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            Write-Detail "ok: $Description"
            return $true
        }
        Start-Sleep -Milliseconds 750
    }
    Write-Warning "timed out after ${Seconds}s waiting for: $Description"
    return $false
}

function Invoke-StackInstalledVerifier {
    param([Parameter(Mandatory)] [string]$Path)

    return @(& $Path)
}

function Invoke-StackIdentityGate {
    <# Require the installed verifier and the expected controller identity before mutation. #>
    $verifier = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'common_dev\v2\Test-LocalMachineIdentity.ps1'
    if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
        throw "machine identity verifier is not installed at $verifier; refusing to mutate tasks"
    }
    $results = @(Invoke-StackInstalledVerifier -Path $verifier)
    if ($results.Count -ne 1) {
        throw "machine identity verifier must return exactly one VERIFIED result (got $($results.Count)); refusing to mutate tasks"
    }
    $result = $results[0]
    if ($null -eq $result -or
        -not $result.PSObject.Properties['status'] -or $result.status -cne 'VERIFIED' -or
        -not $result.PSObject.Properties['machineId'] -or $result.machineId -cne 'snd-desk' -or
        -not $result.PSObject.Properties['instanceId'] -or $result.instanceId -cne 'ca96d510-7d87-4cec-8e1a-bd8fc3866903') {
        throw 'machine identity verifier did not verify snd-desk / ca96d510-7d87-4cec-8e1a-bd8fc3866903; refusing to mutate tasks'
    }
    Write-Detail "machine identity VERIFIED: $($result.machineId) / $($result.instanceId)"
}

function Get-StackGitState {
    param([string]$RepoRoot)

    $sha = (& git -C $RepoRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
        throw "cannot read git HEAD in $RepoRoot"
    }
    $branch = (& git -C $RepoRoot branch --show-current 2>$null)
    $changes = @(& git -C $RepoRoot status --porcelain --untracked-files=no 2>$null)
    return [pscustomobject]@{
        Sha    = $sha.Trim()
        Branch = if ([string]::IsNullOrWhiteSpace($branch)) { '(detached)' } else { $branch.Trim() }
        Dirty  = ($changes.Count -gt 0)
    }
}

function Publish-StackRelease {
    param(
        [Parameter(Mandatory)] [string]$RepoRoot,
        [Parameter(Mandatory)] [string]$ReleaseDirectory
    )

    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'

    foreach ($project in @('AIUsageTracker.Monitor\AIUsageTracker.Monitor.csproj', 'AIUsageTracker.Web\AIUsageTracker.Web.csproj')) {
        $projectPath = Join-Path $RepoRoot $project
        Write-Detail "dotnet publish $project -> $ReleaseDirectory"
        & dotnet publish $projectPath --configuration Release --output $ReleaseDirectory --nologo -v quiet
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $project (exit $LASTEXITCODE)"
        }
    }
}

function New-StackTaskAction {
    param(
        [Parameter(Mandatory)] [string]$ReleaseDirectory,
        [Parameter(Mandatory)] [string]$ExecutableName,
        [string]$Arguments = ''
    )

    $execute = Join-Path $ReleaseDirectory $ExecutableName
    if ([string]::IsNullOrWhiteSpace($Arguments)) {
        return New-ScheduledTaskAction -Execute $execute -WorkingDirectory $ReleaseDirectory
    }
    return New-ScheduledTaskAction -Execute $execute -Argument $Arguments -WorkingDirectory $ReleaseDirectory
}

function Set-StackTaskRelease {
    <# Replaces only the Actions of an existing task; principal, triggers and settings stay as they are. #>
    param(
        [Parameter(Mandatory)] $Task,
        [Parameter(Mandatory)] [string]$ReleaseDirectory,
        [Parameter(Mandatory)] [string]$ExecutableName,
        [string]$Arguments = ''
    )

    Assert-StackTaskNamespace -Folder $Task.TaskPath -ExistingTask
    $newAction = New-StackTaskAction -ReleaseDirectory $ReleaseDirectory -ExecutableName $ExecutableName -Arguments $Arguments
    Set-ScheduledTask -TaskPath $Task.TaskPath -TaskName $Task.TaskName -Action $newAction | Out-Null
}

function Register-StackTasks {
    <# First-time registration: Monitor at boot (S4U), Web at logon (hidden), both restart on failure. #>
    param(
        [Parameter(Mandatory)] [string]$Folder,
        [Parameter(Mandatory)] [string]$ReleaseDirectory,
        [Parameter(Mandatory)] [string]$WebArguments
    )

    Assert-StackTaskNamespace -Folder $Folder
    $userId = "$env:USERDOMAIN\$env:USERNAME"
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
        -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
    $webSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
        -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable -Hidden

    $monitorAction = New-StackTaskAction -ReleaseDirectory $ReleaseDirectory -ExecutableName $script:MonitorExecutable
    $monitorPrincipal = New-ScheduledTaskPrincipal -UserId $userId -LogonType S4U -RunLevel Limited
    Register-ScheduledTask -TaskPath $Folder -TaskName 'Monitor' -Action $monitorAction -Trigger (New-ScheduledTaskTrigger -AtStartup) `
        -Principal $monitorPrincipal -Settings $settings -Description "AIUsageTracker Monitor loopback API (:$MonitorPort). Managed by scripts/local-stack.ps1." | Out-Null

    $webAction = New-StackTaskAction -ReleaseDirectory $ReleaseDirectory -ExecutableName $script:WebExecutable -Arguments $WebArguments
    $webPrincipal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskPath $Folder -TaskName 'Web' -Action $webAction -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $userId) `
        -Principal $webPrincipal -Settings $webSettings -Description "AIUsageTracker Web dashboard ($WebUrl). Managed by scripts/local-stack.ps1." | Out-Null
}

function Stop-StackComponent {
    param(
        $Task,
        [Parameter(Mandatory)] [string]$ExecutableName,
        [Parameter(Mandatory)] [int]$Port
    )

    if ($null -ne $Task) {
        Write-Detail "stopping task $($Task.TaskPath)$($Task.TaskName)"
        Stop-ScheduledTask -TaskPath $Task.TaskPath -TaskName $Task.TaskName -ErrorAction SilentlyContinue
    }

    $gone = Wait-StackCondition -Description "$ExecutableName exited and port $Port is free" -Seconds 20 -Condition {
        $procs = @(Get-CimInstance Win32_Process -Filter "Name = '$ExecutableName'" -ErrorAction SilentlyContinue)
        ($procs.Count -eq 0) -and ($null -eq (Get-StackListenerPid -Port $Port))
    }
    if (-not $gone) {
        foreach ($proc in @(Get-CimInstance Win32_Process -Filter "Name = '$ExecutableName'" -ErrorAction SilentlyContinue)) {
            Write-Warning "force-stopping $ExecutableName pid $($proc.ProcessId) (not owned by the task or slow to exit)"
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        }
        $null = Wait-StackCondition -Description "port $Port is free" -Seconds 15 -Condition { $null -eq (Get-StackListenerPid -Port $Port) }
    }
}

function Format-StackTaskLine {
    param($Task)

    if ($null -eq $Task) { return 'missing' }
    $info = Get-ScheduledTaskInfo -TaskPath $Task.TaskPath -TaskName $Task.TaskName -ErrorAction SilentlyContinue
    $lastResult = if ($null -ne $info) { '0x{0:X}' -f $info.LastTaskResult } else { '?' }
    $lastRun = if ($null -ne $info) { $info.LastRunTime } else { '?' }
    return '{0}{1}: {2}, last result {3}, last run {4}, release {5}' -f $Task.TaskPath, $Task.TaskName, $Task.State, $lastResult, $lastRun, (Get-StackTaskReleaseDirectory -Task $Task)
}

function Get-StackExecutableVersion {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 'missing' }
    return (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
}

# ---------------------------------------------------------------------------
# Actions
# ---------------------------------------------------------------------------

function Invoke-StackStatus {
    $tasks = Get-StackTasks
    $healthy = $true

    Write-Step 'Scheduled tasks'
    Write-Detail ('Monitor task: ' + (Format-StackTaskLine -Task $tasks.Monitor))
    Write-Detail ('Web task:     ' + (Format-StackTaskLine -Task $tasks.Web))
    if ($null -eq $tasks.Monitor -or $null -eq $tasks.Web) { $healthy = $false }

    Write-Step 'Processes'
    $procs = @(Get-StackProcesses)
    if ($procs.Count -eq 0) {
        Write-Detail 'none running'
        $healthy = $false
    }
    foreach ($proc in $procs) {
        Write-Detail ('{0} pid {1} started {2} ({3}) {4}' -f $proc.Name, $proc.ProcessId, $proc.CreationDate, (Get-StackExecutableVersion -Path $proc.ExecutablePath), $proc.ExecutablePath)
    }

    Write-Step 'Ports'
    foreach ($port in @($MonitorPort, ([uri]$WebUrl).Port)) {
        $owner = Get-StackListenerPid -Port $port
        if ($null -eq $owner) {
            Write-Detail "port ${port}: not listening"
            $healthy = $false
        }
        else {
            Write-Detail "port ${port}: pid $owner"
        }
    }

    Write-Step 'Monitor metadata (monitor.json)'
    $info = Read-StackMonitorInfo
    if ($null -eq $info) {
        Write-Detail 'missing or unreadable'
        $healthy = $false
    }
    else {
        $alive = Test-StackProcessAlive -ProcessId ([int]$info.ProcessId)
        Write-Detail ('port {0}, pid {1} ({2}), started {3}' -f $info.Port, $info.ProcessId, ($(if ($alive) { 'alive' } else { 'NOT running' })), $info.StartedAt)
        if (-not $alive) { $healthy = $false }
    }

    Write-Step "Dashboard ($WebUrl)"
    $code = Get-StackWebHttpCode -Url "$WebUrl/"
    Write-Detail "GET / -> HTTP $code"
    if ($code -ne 200) { $healthy = $false }
    $status = Get-StackWebStatus -BaseUrl $WebUrl
    if ($null -eq $status) {
        Write-Detail '/api/monitor/status unreachable'
        $healthy = $false
    }
    else {
        $failing = @($status.failingProviders)
        Write-Detail ('monitor running={0} contractCompatible={1} health={2} failingProviders=[{3}]' -f $status.isRunning, $status.isContractCompatible, $status.serviceHealth, ($failing -join ', '))
        Write-Detail $status.message
        if (-not (Test-StackStatusPayload -Payload $status)) { $healthy = $false }
    }

    if ($healthy) {
        Write-Host 'STACK OK' -ForegroundColor Green
        return 0
    }
    Write-Host 'STACK DEGRADED: see the lines above' -ForegroundColor Yellow
    return 2
}

function Invoke-StackDeploy {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $startedAt = Get-Date

    Write-Step 'Preflight'
    Assert-StackTaskNamespace -Folder $TaskFolder
    $git = Get-StackGitState -RepoRoot $repoRoot
    Write-Detail "source: $repoRoot @ $($git.Sha.Substring(0, 8)) ($($git.Branch))$(if ($git.Dirty) { ' with uncommitted tracked changes' })"
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet SDK not found on PATH'
    }
    Invoke-StackIdentityGate

    $tasks = Get-StackTasks
    foreach ($task in @($tasks.Monitor, $tasks.Web)) {
        if ($null -ne $task) { Assert-StackTaskNamespace -Folder $task.TaskPath -ExistingTask }
    }
    $previousMonitorRelease = Get-StackTaskReleaseDirectory -Task $tasks.Monitor
    $previousWebRelease = Get-StackTaskReleaseDirectory -Task $tasks.Web
    $webAction = Get-StackTaskAction -Task $tasks.Web
    $webArguments = if ($null -ne $webAction) { Get-StackActionProperty -ActionObject $webAction -Name 'Arguments' } else { "--urls $WebUrl" }
    if ([string]::IsNullOrWhiteSpace($webArguments)) { $webArguments = "--urls $WebUrl" }

    if ($null -ne $tasks.Monitor) { Write-Detail "Monitor task: $($tasks.Monitor.TaskPath)$($tasks.Monitor.TaskName) currently -> $previousMonitorRelease" }
    else { Write-Detail "Monitor task: none found; will register $TaskFolder\Monitor" }
    if ($null -ne $tasks.Web) { Write-Detail "Web task:     $($tasks.Web.TaskPath)$($tasks.Web.TaskName) currently -> $previousWebRelease" }
    else { Write-Detail "Web task:     none found; will register $TaskFolder\Web" }
    if (($null -eq $tasks.Monitor) -ne ($null -eq $tasks.Web)) {
        throw 'found only one of the two stack tasks; fix or remove it before deploying'
    }

    if (-not (Test-Path -LiteralPath $ReleaseRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
    }
    $existing = @(Get-ChildItem -LiteralPath $ReleaseRoot -Directory | Select-Object -ExpandProperty Name)
    $releaseName = Get-StackReleaseName -CommitSha $git.Sha -Timestamp $startedAt -ExistingNames $existing -Dirty:$git.Dirty
    $releaseDirectory = Join-Path $ReleaseRoot $releaseName
    Write-Detail "new release: $releaseDirectory"

    Write-Step 'Publish'
    Publish-StackRelease -RepoRoot $repoRoot -ReleaseDirectory $releaseDirectory
    $missing = @(Test-StackRelease -Path $releaseDirectory)
    if ($missing.Count -gt 0) {
        throw "published release is incomplete, missing: $($missing -join ', ')"
    }
    Write-Detail ('Monitor {0}, Web {1}' -f (Get-StackExecutableVersion -Path (Join-Path $releaseDirectory $script:MonitorExecutable)), (Get-StackExecutableVersion -Path (Join-Path $releaseDirectory $script:WebExecutable)))

    Write-Step 'Stop (Web first so it cannot respawn the old Monitor)'
    Stop-StackComponent -Task $tasks.Web -ExecutableName $script:WebExecutable -Port ([uri]$WebUrl).Port
    Stop-StackComponent -Task $tasks.Monitor -ExecutableName $script:MonitorExecutable -Port $MonitorPort

    Write-Step 'Point tasks at the new release'
    if ($null -eq $tasks.Monitor) {
        Register-StackTasks -Folder $TaskFolder -ReleaseDirectory $releaseDirectory -WebArguments $webArguments
        $tasks = Get-StackTasks
        if ($null -eq $tasks.Monitor -or $null -eq $tasks.Web) { throw 'task registration did not produce both tasks' }
        Write-Detail "registered $($tasks.Monitor.TaskPath)Monitor and $($tasks.Web.TaskPath)Web"
    }
    else {
        Set-StackTaskRelease -Task $tasks.Monitor -ReleaseDirectory $releaseDirectory -ExecutableName $script:MonitorExecutable
        Set-StackTaskRelease -Task $tasks.Web -ReleaseDirectory $releaseDirectory -ExecutableName $script:WebExecutable -Arguments $webArguments
        Write-Detail 'actions replaced; principals, triggers and settings untouched'
    }

    $rollback = @()
    if (-not [string]::IsNullOrWhiteSpace($previousMonitorRelease)) {
        $rollback += ConvertTo-StackRollbackCommand -TaskPath $tasks.Monitor.TaskPath -TaskName $tasks.Monitor.TaskName -ReleaseDirectory $previousMonitorRelease -ExecutableName $script:MonitorExecutable
        $rollback += ConvertTo-StackRollbackCommand -TaskPath $tasks.Web.TaskPath -TaskName $tasks.Web.TaskName -ReleaseDirectory $previousWebRelease -ExecutableName $script:WebExecutable -Arguments $webArguments
        $rollback += "Stop-ScheduledTask -TaskPath '$($tasks.Web.TaskPath)' -TaskName '$($tasks.Web.TaskName)'; Stop-ScheduledTask -TaskPath '$($tasks.Monitor.TaskPath)' -TaskName '$($tasks.Monitor.TaskName)'"
        $rollback += "Start-ScheduledTask -TaskPath '$($tasks.Monitor.TaskPath)' -TaskName '$($tasks.Monitor.TaskName)'; Start-ScheduledTask -TaskPath '$($tasks.Web.TaskPath)' -TaskName '$($tasks.Web.TaskName)'"
    }

    try {
        Write-Step 'Start Monitor'
        Start-ScheduledTask -TaskPath $tasks.Monitor.TaskPath -TaskName $tasks.Monitor.TaskName
        $monitorUp = Wait-StackCondition -Description "Monitor listening on $MonitorPort with fresh monitor.json" -Seconds $TimeoutSeconds -Condition {
            $info = Read-StackMonitorInfo
            $started = Get-StackMonitorStartedAt -MonitorInfo $info
            ($null -ne (Get-StackListenerPid -Port $MonitorPort)) -and
            ($null -ne $info) -and (Test-StackProcessAlive -ProcessId ([int]$info.ProcessId)) -and
            ($null -ne $started) -and ($started -ge $startedAt.AddMinutes(-1))
        }
        if (-not $monitorUp) { throw 'Monitor did not come up' }

        Write-Step 'Start Web'
        Start-ScheduledTask -TaskPath $tasks.Web.TaskPath -TaskName $tasks.Web.TaskName
        $webUp = Wait-StackCondition -Description "dashboard answers at $WebUrl and sees a compatible Monitor" -Seconds $TimeoutSeconds -Condition {
            ((Get-StackWebHttpCode -Url "$WebUrl/") -eq 200) -and (Test-StackStatusPayload -Payload (Get-StackWebStatus -BaseUrl $WebUrl))
        }
        if (-not $webUp) { throw 'Web did not come up or does not see the Monitor' }
    }
    catch {
        Write-Host "DEPLOY FAILED: $($_.Exception.Message)" -ForegroundColor Red
        if ($rollback.Count -gt 0) {
            Write-Host 'Rollback (previous release is still on disk):' -ForegroundColor Yellow
            $rollback | ForEach-Object { Write-Host "  $_" }
        }
        throw
    }

    Write-Step 'Result'
    $status = Get-StackWebStatus -BaseUrl $WebUrl
    $failing = @($status.failingProviders)
    Write-Detail "running release: $releaseDirectory"
    Write-Detail "previous release: $(if ([string]::IsNullOrWhiteSpace($previousMonitorRelease)) { '(none)' } else { $previousMonitorRelease })"
    Write-Detail ('monitor health: {0}; failing providers: [{1}]' -f $status.serviceHealth, ($failing -join ', '))
    if ($rollback.Count -gt 0) {
        Write-Detail 'rollback:'
        $rollback | ForEach-Object { Write-Detail "  $_" }
    }
    Write-Host 'DEPLOY OK' -ForegroundColor Green
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    # Native exit codes (git, dotnet) are checked explicitly via $LASTEXITCODE.
    $PSNativeCommandUseErrorActionPreference = $false
    $result = if ($Action -eq 'status') { Invoke-StackStatus } else { Invoke-StackDeploy }
    exit [int](@($result) | Select-Object -Last 1)
}
