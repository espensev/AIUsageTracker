# Pester tests for the pure helpers in local-stack.ps1.
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
