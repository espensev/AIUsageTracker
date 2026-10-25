BeforeAll {
    . (Join-Path $PSScriptRoot 'local-stack.ps1')
    $script:originalStackDataRoot = $env:AIUSAGETRACKER_LOCAL_APP_DATA_ROOT
    $env:AIUSAGETRACKER_LOCAL_APP_DATA_ROOT = $null
}

AfterAll {
    $env:AIUSAGETRACKER_LOCAL_APP_DATA_ROOT = $script:originalStackDataRoot
}

Describe 'Release source provenance' {
    It 'marks untracked source dirty while retaining ignored build outputs' {
        $repo = Join-Path $TestDrive 'source'
        [void][IO.Directory]::CreateDirectory($repo)
        & git -C $repo init --quiet
        [IO.File]::WriteAllText((Join-Path $repo '.gitignore'), "bin/`n")
        & git -C $repo add -- .gitignore
        & git -C $repo -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
        $LASTEXITCODE | Should -Be 0
        [void][IO.Directory]::CreateDirectory((Join-Path $repo 'bin'))
        [IO.File]::WriteAllText((Join-Path $repo 'bin/output.dll'), 'ignored fixture')
        (Get-StackGitState -RepoRoot $repo).Dirty | Should -BeFalse

        [IO.File]::WriteAllText((Join-Path $repo 'Uncommitted.cs'), 'class Uncommitted {}')

        (Get-StackGitState -RepoRoot $repo).Dirty | Should -BeTrue
    }
}

Describe 'Executable rollback instructions' {
    It 'round-trips apostrophes in every scheduler argument' {
        Mock New-ScheduledTaskAction {
            [pscustomobject]@{ Execute = $Execute; WorkingDirectory = $WorkingDirectory; Arguments = $Argument }
        } -RemoveParameterType Execute, WorkingDirectory, Argument -RemoveParameterValidation Execute, WorkingDirectory, Argument
        Mock Set-ScheduledTask {
            [pscustomobject]@{ TaskPath = $TaskPath; TaskName = $TaskName; Action = $Action }
        } -RemoveParameterType TaskPath, TaskName, Action -RemoveParameterValidation TaskPath, TaskName, Action
        $release = Join-Path $TestDrive "operator's release"
        $line = ConvertTo-StackRollbackCommand -TaskPath "\SevGrp\Operator's Stack\" -TaskName "Web's task" `
            -ReleaseDirectory $release -ExecutableName 'AIUsageTracker.Web.exe' -Arguments "--label O'Brien"

        $actual = & ([scriptblock]::Create($line))

        $actual.TaskPath | Should -Be "\SevGrp\Operator's Stack\"
        $actual.TaskName | Should -Be "Web's task"
        $actual.Action.Execute | Should -Be (Join-Path $release 'AIUsageTracker.Web.exe')
        $actual.Action.WorkingDirectory | Should -Be $release
        $actual.Action.Arguments | Should -Be "--label O'Brien"
    }
}

Describe 'Deploy configuration and preserved arguments' {
    BeforeEach {
        $script:fixtureTasks = [pscustomobject]@{}
        foreach ($component in @('Monitor', 'Web')) {
            $previous = Join-Path $TestDrive "previous-$component"
            [void][IO.Directory]::CreateDirectory($previous)
            $exe = Join-Path $previous "AIUsageTracker.$component.exe"
            [IO.File]::WriteAllText($exe, 'fixture')
            $arguments = if ($component -eq 'Monitor') { '--debug' } else { '--urls http://localhost:5100 --environment Production' }
            $script:fixtureTasks | Add-Member -NotePropertyName $component -NotePropertyValue ([pscustomobject]@{
                TaskPath = '\SevGrp\AIUsageTracker\'; TaskName = $component
                Actions = @([pscustomobject]@{ Execute = $exe; WorkingDirectory = $previous; Arguments = $arguments })
            })
        }
        Mock Write-Step {}
        Mock Write-Detail {}
        Mock Write-Host {}
        Mock Write-Warning {}
        Mock Get-StackGitState { [pscustomobject]@{ Sha = 'abcdef1234567890'; Branch = 'fixture'; Dirty = $false } }
        Mock Get-Command { [pscustomobject]@{} } -ParameterFilter { $Name -eq 'dotnet' }
        Mock Invoke-StackIdentityGate {}
        Mock Get-StackTasks { $script:fixtureTasks }
        Mock Publish-StackRelease {}
        Mock Test-StackRelease { @() }
        Mock Get-StackExecutableVersion { 'fixture' }
        Mock Stop-StackComponent {}
        Mock New-ScheduledTaskAction {
            [pscustomobject]@{ Execute = $Execute; Arguments = [string]$Argument; WorkingDirectory = $WorkingDirectory }
        } -RemoveParameterType Execute, WorkingDirectory, Argument -RemoveParameterValidation Execute, WorkingDirectory, Argument
        Mock Set-ScheduledTask {} -RemoveParameterType TaskPath, TaskName, Action -RemoveParameterValidation TaskPath, TaskName, Action
        Mock Start-ScheduledTask {} -RemoveParameterType TaskPath, TaskName -RemoveParameterValidation TaskPath, TaskName
        Mock Wait-StackCondition { $true }
        Mock Get-StackWebStatus { [pscustomobject]@{ serviceHealth = 'healthy'; failingProviders = @() } }
    }

    It 'refuses an unsupported Monitor port before preparing a deployment' {
        { & { $MonitorPort = 5200; $ReleaseRoot = Join-Path $TestDrive 'releases'; Invoke-StackDeploy } } |
            Should -Throw '*MonitorPort*5000*'
        Should -Invoke Publish-StackRelease -Times 0 -Exactly
        Should -Invoke Stop-StackComponent -Times 0 -Exactly
        Should -Invoke Set-ScheduledTask -Times 0 -Exactly
    }

    It 'refuses a data-root override that the Monitor would ignore' {
        try {
            $env:AIUSAGETRACKER_LOCAL_APP_DATA_ROOT = Join-Path $TestDrive 'different-data'
            { & { $ReleaseRoot = Join-Path $TestDrive 'releases'; Invoke-StackDeploy } } |
                Should -Throw '*AIUSAGETRACKER_LOCAL_APP_DATA_ROOT*'
        }
        finally { $env:AIUSAGETRACKER_LOCAL_APP_DATA_ROOT = $null }
        Should -Invoke Publish-StackRelease -Times 0 -Exactly
        Should -Invoke Stop-StackComponent -Times 0 -Exactly
    }

    It 'updates the requested Web URL, preserves other flags, and stops the previous listener' {
        & { $WebUrl = 'http://localhost:5200'; $ReleaseRoot = Join-Path $TestDrive 'releases'; Invoke-StackDeploy } | Should -Be 0

        Should -Invoke Set-ScheduledTask -Times 1 -Exactly -ParameterFilter {
            $TaskName -eq 'Monitor' -and $Action.Arguments -eq '--debug'
        }
        Should -Invoke Set-ScheduledTask -Times 1 -Exactly -ParameterFilter {
            $TaskName -eq 'Web' -and $Action.Arguments -eq '--urls http://localhost:5200 --environment Production'
        }
        Should -Invoke Stop-StackComponent -Times 1 -Exactly -ParameterFilter { $ExecutableName -eq 'AIUsageTracker.Web.exe' -and $Port -eq 5100 }
    }

    It 'retains Monitor flags and original Web arguments in rollback after a partial update' {
        Mock Set-ScheduledTask { if ($TaskName -eq 'Web') { throw 'fixture: web action failed' } } `
            -RemoveParameterType TaskPath, TaskName, Action -RemoveParameterValidation TaskPath, TaskName, Action

        { & { $WebUrl = 'http://localhost:5200'; $ReleaseRoot = Join-Path $TestDrive 'releases'; Invoke-StackDeploy } } |
            Should -Throw '*fixture: web action failed*'

        Should -Invoke Set-ScheduledTask -Times 1 -Exactly -ParameterFilter {
            $TaskName -eq 'Monitor' -and $Action.Execute -like '*previous-Monitor*' -and $Action.Arguments -eq '--debug'
        }
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter {
            ((@($Message) + @($Object)) -join ' ') -like '*Set-ScheduledTask*AIUsageTracker.Web.exe*--urls http://localhost:5100 --environment Production*'
        }
    }
}

Describe 'Web listener arguments' {
    It 'updates quoted or equals-form URLs while preserving other options: <Arguments>' -ForEach @(
        @{ Arguments = '--environment Production --urls="http://localhost:5100"'; Expected = '--environment Production --urls http://localhost:5200' }
        @{ Arguments = '--urls "http://localhost:5100" --environment Production'; Expected = '--urls http://localhost:5200 --environment Production' }
    ) {
        $configuration = Resolve-StackWebConfiguration -Arguments $Arguments -Url 'http://localhost:5200'
        $configuration.Arguments | Should -Be $Expected
        $configuration.PreviousPort | Should -Be 5100
    }

    It 'adds an explicit listener without dropping an existing environment option' {
        (Resolve-StackWebConfiguration -Arguments '--environment Production' -Url 'http://localhost:5200').Arguments |
            Should -Be '--environment Production --urls http://localhost:5200'
    }

    It 'refuses ambiguous or incomplete existing listeners: <Arguments>' -ForEach @(
        @{ Arguments = '--urls http://localhost:5100 --urls http://localhost:5200' }
        @{ Arguments = '--urls' }
        @{ Arguments = '--urls http://localhost:5100;http://localhost:5200' }
    ) {
        { Resolve-StackWebConfiguration -Arguments $Arguments -Url 'http://localhost:5200' } | Should -Throw
    }

    It 'rejects a non-loopback or non-origin URL: <Url>' -ForEach @(
        @{ Url = 'http://example.invalid:5200' }
        @{ Url = 'http://localhost:5200/dashboard' }
        @{ Url = 'http://localhost:5200/?option=value' }
    ) {
        { ConvertTo-StackWebUri -Url $Url } | Should -Throw '*loopback*origin*'
    }
}

Describe 'First registration rollback' {
    BeforeEach {
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{} }
        Mock New-ScheduledTaskTrigger { [pscustomobject]@{} }
        Mock New-StackTaskAction { [pscustomobject]@{} }
        Mock Unregister-ScheduledTask {} -RemoveParameterType TaskPath, TaskName -RemoveParameterValidation TaskPath, TaskName
        Mock Register-ScheduledTask { if ($TaskName -eq 'Web') { throw 'fixture: Web registration failed' } } `
            -RemoveParameterType Action, Principal, Settings, Trigger -RemoveParameterValidation Action, Principal, Settings, Trigger
    }

    It 'removes only the Monitor it just created when Web registration fails' {
        { Register-StackTasks -Folder '\SevGrp\AIUsageTracker\' -ReleaseDirectory $TestDrive -WebArguments '--urls http://localhost:5100' } |
            Should -Throw '*fixture: Web registration failed*'

        Should -Invoke Unregister-ScheduledTask -Times 1 -Exactly -ParameterFilter {
            $TaskPath -eq '\SevGrp\AIUsageTracker\' -and $TaskName -eq 'Monitor' -and $Confirm -eq $false
        }
        Should -Invoke Unregister-ScheduledTask -Times 0 -Exactly -ParameterFilter { $TaskName -eq 'Web' }
    }

    It 'does not remove an existing task when the first registration is refused' {
        Mock Register-ScheduledTask { throw 'fixture: task already exists' } `
            -RemoveParameterType Action, Principal, Settings, Trigger -RemoveParameterValidation Action, Principal, Settings, Trigger

        { Register-StackTasks -Folder '\SevGrp\AIUsageTracker\' -ReleaseDirectory $TestDrive -WebArguments '--urls http://localhost:5100' } |
            Should -Throw '*fixture: task already exists*'
        Should -Invoke Unregister-ScheduledTask -Times 0 -Exactly
    }
}
