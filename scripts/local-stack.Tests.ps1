# Pester tests for helpers and mocked system boundaries in local-stack.ps1.
# Run: pwsh -NoProfile -Command "Invoke-Pester -Path scripts/local-stack.Tests.ps1"

BeforeAll {
    . (Join-Path $PSScriptRoot 'local-stack.ps1')
}

Describe 'Get-StackReleaseName' {
    It 'combines the short sha and the date' {
        $name = Get-StackReleaseName -CommitSha 'af688ee29c3f3f3826134555e905a18a57b3a7c7' -Timestamp ([datetime]'2026-09-21T10:15:00') -ExistingNames @()
        $name | Should -Be 'af688ee2-20260921'
    }

    It 'marks a release built from a dirty tree' {
        $name = Get-StackReleaseName -CommitSha 'af688ee29c3f3f3826134555e905a18a57b3a7c7' -Timestamp ([datetime]'2026-09-21T10:15:00') -ExistingNames @() -Dirty
        $name | Should -Be 'af688ee2-20260921-dirty'
    }

    It 'adds a time suffix when the name is already taken' {
        $name = Get-StackReleaseName -CommitSha 'af688ee29c3f3f3826134555e905a18a57b3a7c7' -Timestamp ([datetime]'2026-09-21T10:15:07') -ExistingNames @('af688ee2-20260921')
        $name | Should -Be 'af688ee2-20260921-101507'
    }
}

Describe 'Test-StackRelease' {
    It 'reports every required file that is missing' {
        $dir = Join-Path $TestDrive 'empty-release'
        New-Item -ItemType Directory -Path $dir | Out-Null

        $missing = @(Test-StackRelease -Path $dir)

        $missing | Should -Contain 'AIUsageTracker.Monitor.exe'
        $missing | Should -Contain 'AIUsageTracker.Monitor.runtimeconfig.json'
        $missing | Should -Contain 'AIUsageTracker.Web.exe'
        $missing | Should -Contain 'AIUsageTracker.Web.runtimeconfig.json'
        $missing | Should -Contain 'wwwroot'
    }

    It 'returns nothing for a complete release' {
        $dir = Join-Path $TestDrive 'complete-release'
        New-Item -ItemType Directory -Path (Join-Path $dir 'wwwroot') -Force | Out-Null
        foreach ($file in @(
                'AIUsageTracker.Monitor.exe', 'AIUsageTracker.Monitor.dll', 'AIUsageTracker.Monitor.runtimeconfig.json', 'AIUsageTracker.Monitor.deps.json',
                'AIUsageTracker.Web.exe', 'AIUsageTracker.Web.dll', 'AIUsageTracker.Web.runtimeconfig.json', 'AIUsageTracker.Web.deps.json',
                'appsettings.json')) {
            Set-Content -LiteralPath (Join-Path $dir $file) -Value 'x'
        }

        @(Test-StackRelease -Path $dir).Count | Should -Be 0
    }

    It 'reports a missing directory as missing everything' {
        $missing = @(Test-StackRelease -Path (Join-Path $TestDrive 'does-not-exist'))
        $missing | Should -Contain 'AIUsageTracker.Monitor.exe'
        $missing | Should -Contain 'wwwroot'
    }
}

Describe 'Select-StackTask' {
    BeforeAll {
        $script:tasks = @(
            [pscustomobject]@{
                TaskName = 'Monitor'; TaskPath = '\Sevnet\AIUsageTracker\'
                Actions  = @([pscustomobject]@{ Execute = 'C:\r\one\AIUsageTracker.Monitor.exe'; Arguments = ''; WorkingDirectory = 'C:\r\one' })
            },
            [pscustomobject]@{
                TaskName = 'Web'; TaskPath = '\Sevnet\AIUsageTracker\'
                Actions  = @([pscustomobject]@{ Execute = 'C:\r\one\AIUsageTracker.Web.exe'; Arguments = '--urls http://localhost:5100'; WorkingDirectory = 'C:\r\one' })
            },
            [pscustomobject]@{
                TaskName = 'Server'; TaskPath = '\Sevnet\AIUsage\'
                Actions  = @([pscustomobject]@{ Execute = 'C:\Program Files\PowerShell\7\pwsh.exe'; Arguments = '-File x.ps1'; WorkingDirectory = '' })
            },
            [pscustomobject]@{ TaskName = 'NoActions'; TaskPath = '\'; Actions = $null }
        )
    }

    It 'finds the task whose action runs the executable, case-insensitively' {
        (Select-StackTask -Tasks $script:tasks -ExecutableName 'aiusagetracker.web.exe').TaskName | Should -Be 'Web'
        (Select-StackTask -Tasks $script:tasks -ExecutableName 'AIUsageTracker.Monitor.exe').TaskName | Should -Be 'Monitor'
    }

    It 'returns nothing when no task runs the executable' {
        Select-StackTask -Tasks $script:tasks -ExecutableName 'AIUsageTracker.exe' | Should -BeNullOrEmpty
    }

    It 'tolerates an empty task list' {
        Select-StackTask -Tasks @() -ExecutableName 'AIUsageTracker.Web.exe' | Should -BeNullOrEmpty
    }
}

Describe 'Get-StackTaskReleaseDirectory' {
    It 'returns the directory that holds the action executable' {
        $task = [pscustomobject]@{
            TaskName = 'Web'; TaskPath = '\Sevnet\AIUsageTracker\'
            Actions  = @([pscustomobject]@{ Execute = 'C:\r\one\AIUsageTracker.Web.exe'; Arguments = ''; WorkingDirectory = 'C:\r\one' })
        }
        Get-StackTaskReleaseDirectory -Task $task | Should -Be 'C:\r\one'
    }

    It 'returns nothing for a task without actions' {
        Get-StackTaskReleaseDirectory -Task ([pscustomobject]@{ TaskName = 'x'; TaskPath = '\'; Actions = $null }) | Should -BeNullOrEmpty
    }
}

Describe 'Test-StackStatusPayload' {
    It 'accepts a running, contract-compatible monitor even when provider health is degraded' {
        $payload = [pscustomobject]@{ isRunning = $true; isContractCompatible = $true; serviceHealth = 'degraded'; failingProviders = @('groq') }
        Test-StackStatusPayload -Payload $payload | Should -BeTrue
    }

    It 'rejects a monitor that is not running' {
        Test-StackStatusPayload -Payload ([pscustomobject]@{ isRunning = $false; isContractCompatible = $true }) | Should -BeFalse
    }

    It 'rejects an incompatible agent contract' {
        Test-StackStatusPayload -Payload ([pscustomobject]@{ isRunning = $true; isContractCompatible = $false }) | Should -BeFalse
    }

    It 'rejects an empty payload' {
        Test-StackStatusPayload -Payload $null | Should -BeFalse
    }
}

Describe 'ConvertTo-StackRollbackCommand' {
    It 'produces a Set-ScheduledTask line that points the task back at the previous release' {
        $line = ConvertTo-StackRollbackCommand -TaskPath '\Sevnet\AIUsageTracker\' -TaskName 'Web' -ReleaseDirectory 'C:\r\prev' -ExecutableName 'AIUsageTracker.Web.exe' -Arguments '--urls http://localhost:5100'
        $line | Should -Match '^Set-ScheduledTask '
        $line | Should -Match "-TaskPath '\\Sevnet\\AIUsageTracker\\'"
        $line | Should -Match "-TaskName 'Web'"
        $line | Should -Match "-Execute 'C:\\r\\prev\\AIUsageTracker\.Web\.exe'"
        $line | Should -Match "-WorkingDirectory 'C:\\r\\prev'"
        $line | Should -Match "-Argument '--urls http://localhost:5100'"
    }

    It 'omits the argument clause when there are no arguments' {
        $line = ConvertTo-StackRollbackCommand -TaskPath '\Sevnet\AIUsageTracker\' -TaskName 'Monitor' -ReleaseDirectory 'C:\r\prev' -ExecutableName 'AIUsageTracker.Monitor.exe' -Arguments ''
        $line | Should -Not -Match '-Argument'
    }
}

Describe 'Deployment identity gate' {
    BeforeEach {
        Mock Write-Detail {}
        Mock Test-Path { $true }
        Mock Invoke-StackInstalledVerifier {
            [pscustomobject]@{
                status = 'VERIFIED'
                machineId = 'snd-desk'
                instanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
                computerName = 'fixture-host'
            }
        }
    }

    It 'refuses an absent installed verifier' {
        Mock Test-Path { $false }
        { Invoke-StackIdentityGate } | Should -Throw '*verifier*'
        Should -Invoke Invoke-StackInstalledVerifier -Times 0 -Exactly
    }

    It 'refuses a verified identity from a different machine or instance' -ForEach @(
        @{ MachineId = 'another-machine'; InstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903' }
        @{ MachineId = 'snd-desk'; InstanceId = '00000000-0000-0000-0000-000000000001' }
    ) {
        Mock Invoke-StackInstalledVerifier {
            [pscustomobject]@{ status = 'VERIFIED'; machineId = $MachineId; instanceId = $InstanceId; computerName = 'fixture-host' }
        }
        { Invoke-StackIdentityGate } | Should -Throw '*identity*'
    }

    It 'requires exactly one result from the installed verifier' {
        Mock Invoke-StackInstalledVerifier {
            [pscustomobject]@{ status = 'VERIFIED'; machineId = 'snd-desk'; instanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'; computerName = 'fixture-host' }
            [pscustomobject]@{ status = 'UNVERIFIED' }
        }
        { Invoke-StackIdentityGate } | Should -Throw '*identity*'
    }

    It 'rejects incomplete or unverified results' -ForEach @(
        @{ Result = $null }
        @{ Result = [pscustomobject]@{ status = 'UNVERIFIED'; machineId = 'snd-desk'; instanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903' } }
        @{ Result = [pscustomobject]@{ status = 'VERIFIED'; machineId = 'snd-desk' } }
        @{ Result = 'unexpected verifier output' }
    ) {
        Mock Invoke-StackInstalledVerifier { $Result }
        { Invoke-StackIdentityGate } | Should -Throw '*identity*'
    }

    It 'propagates verifier failure' {
        Mock Invoke-StackInstalledVerifier { throw 'fixture verifier failure' }
        { Invoke-StackIdentityGate } | Should -Throw '*fixture verifier failure*'
    }

    It 'accepts the expected machine only through the known-folder verifier' {
        { Invoke-StackIdentityGate } | Should -Not -Throw
        Should -Invoke Invoke-StackInstalledVerifier -Times 1 -Exactly -ParameterFilter {
            $Path -eq (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'common_dev\v2\Test-LocalMachineIdentity.ps1')
        }
    }
}

Describe 'Scheduled-task registration namespace' {
    BeforeEach {
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{} }
        Mock New-ScheduledTaskTrigger { [pscustomobject]@{} }
        Mock New-StackTaskAction { [pscustomobject]@{} }
        Mock Register-ScheduledTask {} -RemoveParameterType Action, Principal, Settings, Trigger -RemoveParameterValidation Action, Principal, Settings, Trigger
    }

    It 'rejects <Folder> before any scheduler preparation or registration' -ForEach @(
        @{ Folder = '\' }
        @{ Folder = '\MyTasks\AIUsageTracker\' }
        @{ Folder = '\MyTasks\' }
        @{ Folder = '\SevGrp\' }
        @{ Folder = '\AIUsageTracker\' }
        @{ Folder = '\Unrelated\Service\' }
        @{ Folder = '\Sevnet\AIUsageTracker\' }
        @{ Folder = '\AdminTasks\AIUsageTracker\' }
        @{ Folder = '\SevGrp\..\MyTasks\' }
    ) {
        { Register-StackTasks -Folder $Folder -ReleaseDirectory $TestDrive -WebArguments '--urls http://localhost:5100' } | Should -Throw '*namespace*'
        Should -Invoke New-ScheduledTaskSettingsSet -Times 0 -Exactly
        Should -Invoke Register-ScheduledTask -Times 0 -Exactly
    }

    It 'registers both tasks beneath the application owner folder' {
        Register-StackTasks -Folder '\SevGrp\AIUsageTracker\' -ReleaseDirectory $TestDrive -WebArguments '--urls http://localhost:5100'
        Should -Invoke Register-ScheduledTask -Times 2 -Exactly -ParameterFilter { $TaskPath -eq '\SevGrp\AIUsageTracker\' }
    }
}

Describe 'Stack status without running processes' {
    It 'returns degraded status when both processes are absent' {
        Mock Get-StackTasks { [pscustomobject]@{ Monitor = $null; Web = $null } }
        Mock Get-CimInstance {}
        Mock Get-StackListenerPid { $null }
        Mock Read-StackMonitorInfo { $null }
        Mock Get-StackWebHttpCode { 0 }
        Mock Get-StackWebStatus { $null }
        Mock Write-Step {}
        Mock Write-Detail {}
        Mock Write-Host {}
        Invoke-StackStatus | Should -Be 2
    }
}

Describe 'Task namespace preflight' {
    BeforeEach {
        Mock Write-Step {}
        Mock Write-Detail {}
        Mock Get-StackGitState { [pscustomobject]@{ Sha = '1234567890'; Branch = 'fixture'; Dirty = $false } }
        Mock Get-Command { [pscustomobject]@{} } -ParameterFilter { $Name -eq 'dotnet' }
        Mock Invoke-StackIdentityGate {}
        Mock Publish-StackRelease { throw 'unexpected publish' }
        Mock Stop-StackComponent { throw 'unexpected process stop' }
        Mock Get-StackTasks {
            [pscustomobject]@{
                Monitor = [pscustomobject]@{ TaskPath = '\MyTasks\AIUsageTracker\' }
                Web = $null
            }
        }
    }

    It 'rejects an explicit forbidden folder before any deployment preparation' {
        & {
            $TaskFolder = '\'
            { Invoke-StackDeploy } | Should -Throw '*namespace*'
        }
        Should -Invoke Get-StackGitState -Times 0 -Exactly
        Should -Invoke Invoke-StackIdentityGate -Times 0 -Exactly
        Should -Invoke Publish-StackRelease -Times 0 -Exactly
        Should -Invoke Stop-StackComponent -Times 0 -Exactly
    }

    It 'rejects an existing forbidden task before publish or stop' {
        { Invoke-StackDeploy } | Should -Throw '*namespace*'
        Should -Invoke Publish-StackRelease -Times 0 -Exactly
        Should -Invoke Stop-StackComponent -Times 0 -Exactly
    }

    It 'preserves approved existing owner contracts at <Folder>' -ForEach @(
        @{ Folder = '\SevGrp\AIUsageTracker\' }
        @{ Folder = '\Sevnet\AIUsageTracker\' }
        @{ Folder = '\AdminTasks\AIUsageTracker\' }
    ) {
        { Assert-StackTaskNamespace -Folder $Folder -ExistingTask } | Should -Not -Throw
    }

    It 'rejects malformed or unowned existing task namespaces at <Folder>' -ForEach @(
        @{ Folder = '\Sevnet\' }
        @{ Folder = '\AdminTasks\' }
        @{ Folder = '\SevGrp\ Owner\' }
        @{ Folder = '\SevGrp\Owner/Child\' }
        @{ Folder = '\SevGrp\\Owner\' }
        @{ Folder = '\SevGrp\Owner\..\' }
        @{ Folder = '\SevGrp\Owner:\' }
    ) {
        { Assert-StackTaskNamespace -Folder $Folder -ExistingTask } | Should -Throw '*namespace*'
    }
}

Describe 'Stack task selection ownership' {
    BeforeAll {
        $script:ambiguousTasks = @(
            [pscustomobject]@{
                TaskName = 'Monitor'; TaskPath = '\SevGrp\AIUsageTracker\'
                Actions  = @([pscustomobject]@{ Execute = 'C:\r\one\AIUsageTracker.Monitor.exe'; Arguments = ''; WorkingDirectory = 'C:\r\one' })
            },
            [pscustomobject]@{
                TaskName = 'Backup'; TaskPath = '\SevGrp\Elsewhere\'
                Actions  = @([pscustomobject]@{ Execute = 'D:\other\AIUsageTracker.Monitor.exe'; Arguments = ''; WorkingDirectory = 'D:\other' })
            }
        )
    }

    It 'refuses to choose when several tasks run the same executable' {
        { Select-StackTask -Tasks $script:ambiguousTasks -ExecutableName 'AIUsageTracker.Monitor.exe' } | Should -Throw '*AIUsageTracker.Monitor.exe*'
    }

    It 'names every candidate task in the refusal' {
        { Select-StackTask -Tasks $script:ambiguousTasks -ExecutableName 'AIUsageTracker.Monitor.exe' } | Should -Throw '*\SevGrp\Elsewhere\Backup*'
        { Select-StackTask -Tasks $script:ambiguousTasks -ExecutableName 'AIUsageTracker.Monitor.exe' } | Should -Throw '*\SevGrp\AIUsageTracker\Monitor*'
    }
}

Describe 'Stack process ownership' {
    It 'accepts an executable inside an owned release directory' {
        Test-StackProcessOwnership -ExecutablePath 'C:\r\prev\AIUsageTracker.Web.exe' -OwnedDirectories @('C:\r\prev') | Should -BeTrue
    }

    It 'accepts an executable nested beneath an owned release directory' {
        Test-StackProcessOwnership -ExecutablePath 'C:\r\prev\sub\tool.exe' -OwnedDirectories @('C:\r\prev') | Should -BeTrue
    }

    It 'preserves a same-named executable outside every owned directory' {
        Test-StackProcessOwnership -ExecutablePath 'C:\Elsewhere\AIUsageTracker.Web.exe' -OwnedDirectories @('C:\r\prev') | Should -BeFalse
    }

    It 'preserves an executable in a sibling directory that only shares a name prefix' {
        Test-StackProcessOwnership -ExecutablePath 'C:\r\previous\AIUsageTracker.Web.exe' -OwnedDirectories @('C:\r\prev') | Should -BeFalse
    }

    It 'matches directory case-insensitively' {
        Test-StackProcessOwnership -ExecutablePath 'c:\R\PREV\AIUsageTracker.Web.exe' -OwnedDirectories @('C:\r\prev') | Should -BeTrue
    }

    It 'rejects an unreadable executable path' {
        Test-StackProcessOwnership -ExecutablePath '' -OwnedDirectories @('C:\r\prev') | Should -BeFalse
    }

    It 'owns nothing when no directories are given' {
        Test-StackProcessOwnership -ExecutablePath 'C:\r\prev\AIUsageTracker.Web.exe' -OwnedDirectories @() | Should -BeFalse
    }
}

Describe 'Stack component stop scope' {
    BeforeEach {
        Mock Write-Detail {}
        Mock Write-Warning {}
        Mock Stop-ScheduledTask {} -RemoveParameterType TaskPath, TaskName -RemoveParameterValidation TaskPath, TaskName
        Mock Get-StackListenerPid { 4321 }
        $script:task = [pscustomobject]@{ TaskPath = '\SevGrp\AIUsageTracker\'; TaskName = 'Web' }
        $script:processes = @(
            [pscustomobject]@{ ProcessId = 111; Name = 'AIUsageTracker.Web.exe'; ExecutablePath = 'C:\r\prev\AIUsageTracker.Web.exe' },
            [pscustomobject]@{ ProcessId = 222; Name = 'AIUsageTracker.Web.exe'; ExecutablePath = 'C:\Elsewhere\AIUsageTracker.Web.exe' }
        )
    }

    It 'force-stops only processes from owned release directories' {
        Mock Wait-StackCondition { if ($Description -like 'port *') { $true } else { $false } }
        Mock Get-CimInstance { $script:processes }
        Mock Stop-Process {} -RemoveParameterType Id -RemoveParameterValidation Id

        Stop-StackComponent -Task $script:task -ExecutableName 'AIUsageTracker.Web.exe' -Port 5100 -OwnedDirectories @('C:\r\prev')

        Should -Invoke Stop-Process -Times 1 -Exactly -ParameterFilter { $Id -eq 111 }
        Should -Invoke Stop-Process -Times 0 -Exactly -ParameterFilter { $Id -eq 222 }
    }

    It 'refuses the cutover when the port never becomes free' {
        Mock Wait-StackCondition { $false }
        Mock Get-CimInstance { $script:processes }
        Mock Stop-Process {} -RemoveParameterType Id -RemoveParameterValidation Id

        { Stop-StackComponent -Task $script:task -ExecutableName 'AIUsageTracker.Web.exe' -Port 5100 -OwnedDirectories @('C:\r\prev') } | Should -Throw '*port 5100*'
    }
}

Describe 'Partial cutover rollback' {
    BeforeEach {
        Mock Write-Step {}
        Mock Write-Detail {}
        Mock Write-Warning {}
        Mock Write-Host {}
        Mock Get-StackGitState { [pscustomobject]@{ Sha = 'abcdef1234567890'; Branch = 'fixture'; Dirty = $false } }
        Mock Get-Command { [pscustomobject]@{} } -ParameterFilter { $Name -eq 'dotnet' }
        Mock Invoke-StackIdentityGate {}
        Mock Test-Path { $true }
        Mock Get-ChildItem { @() } -ParameterFilter { $Path -eq $null -or $Path -is [string] }
        Mock Get-StackExecutableVersion { '2.4.7' }
        Mock Publish-StackRelease {}
        Mock Stop-StackComponent {}
        Mock Get-StackTasks {
            [pscustomobject]@{
                Monitor = [pscustomobject]@{
                    TaskPath = '\SevGrp\AIUsageTracker\'; TaskName = 'Monitor'
                    Actions  = @([pscustomobject]@{ Execute = 'C:\r\prev\AIUsageTracker.Monitor.exe'; Arguments = ''; WorkingDirectory = 'C:\r\prev' })
                }
                Web     = [pscustomobject]@{
                    TaskPath = '\SevGrp\AIUsageTracker\'; TaskName = 'Web'
                    Actions  = @([pscustomobject]@{ Execute = 'C:\r\prev\AIUsageTracker.Web.exe'; Arguments = '--urls http://localhost:5100'; WorkingDirectory = 'C:\r\prev' })
                }
            }
        }
        Mock New-ScheduledTaskAction { [pscustomobject]@{ Execute = $Execute; Arguments = [string]$Arguments; WorkingDirectory = $WorkingDirectory } } -RemoveParameterType Execute, WorkingDirectory, Argument -RemoveParameterValidation Execute, WorkingDirectory, Argument
        Mock Set-ScheduledTask {} -RemoveParameterType TaskPath, TaskName, Action -RemoveParameterValidation TaskPath, TaskName, Action
    }

    It 'restores the already-repointed task and prints rollback when the second action update fails' {
        Mock Set-ScheduledTask { if ($TaskName -eq 'Web') { throw 'fixture: web update failed' } } -RemoveParameterType TaskPath, TaskName, Action -RemoveParameterValidation TaskPath, TaskName, Action

        { & { $TaskFolder = '\SevGrp\AIUsageTracker\'; Invoke-StackDeploy } } | Should -Throw '*fixture: web update failed*'

        Should -Invoke Set-ScheduledTask -Times 1 -Exactly -ParameterFilter { $TaskName -eq 'Monitor' -and $Action.Execute -like '*abcdef12-*\AIUsageTracker.Monitor.exe' }
        Should -Invoke Set-ScheduledTask -Times 1 -Exactly -ParameterFilter { $TaskName -eq 'Monitor' -and $Action.Execute -eq 'C:\r\prev\AIUsageTracker.Monitor.exe' }
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { ((@($Message) + @($Object)) -join ' ') -like '*Rollback*' }
    }

    It 'surfaces the original failure when automatic restore also fails' {
        Mock Set-ScheduledTask {
            if ($TaskName -eq 'Web' -or ($null -ne $Action -and $Action.Execute -like 'C:\r\prev*')) { throw 'fixture: scheduler unavailable' }
        } -RemoveParameterType TaskPath, TaskName, Action -RemoveParameterValidation TaskPath, TaskName, Action

        { & { $TaskFolder = '\SevGrp\AIUsageTracker\'; Invoke-StackDeploy } } | Should -Throw '*fixture: scheduler unavailable*'

        Should -Invoke Write-Warning -Times 1 -Exactly -ParameterFilter { $Message -like '*could not restore*' }
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { ((@($Message) + @($Object)) -join ' ') -like '*Rollback*' }
    }

    It 'keeps rollback instructions without automatic restore for a startup failure after a clean cutover' {
        Mock Start-ScheduledTask { throw 'fixture: start rejected' } -RemoveParameterType TaskPath, TaskName -RemoveParameterValidation TaskPath, TaskName

        { & { $TaskFolder = '\SevGrp\AIUsageTracker\'; Invoke-StackDeploy } } | Should -Throw '*fixture: start rejected*'

        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { ((@($Message) + @($Object)) -join ' ') -like '*Rollback*' }
        Should -Invoke Set-ScheduledTask -Times 0 -Exactly -ParameterFilter { $null -ne $Action -and $Action.Execute -like 'C:\r\prev*' }
    }
}
