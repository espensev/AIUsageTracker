param(
    [ValidateSet("win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Version = "",
    [ValidateSet("balanced", "max", "compat")]
    [string]$InstallerCompression = "balanced"
)

# AI Usage Tracker - Distribution Packaging Script
# Usage: .\scripts\publish-app.ps1 -Runtime win-x64 -Version 2.4.7-beta.3 -InstallerCompression balanced

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/generated-path-safety.ps1"
Push-Location $repoRoot
try {
$isWinPlatform = $Runtime.StartsWith("win-")
$projectName = if ($isWinPlatform) { "AIUsageTracker" } else { "AIUsageTracker.CLI" }
$projectPath = if ($isWinPlatform) { ".\AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj" } else { ".\AIUsageTracker.CLI\AIUsageTracker.CLI.csproj" }
$publishDir = Join-Path $repoRoot "artifacts/publish/$Runtime"

# If Version passed, synchronize it across all files
if (-not [string]::IsNullOrEmpty($Version)) {
    Write-Host "Synchronizing version $Version across all files..." -ForegroundColor Cyan
    
    # Create a clean version (x.y.z) for AssemblyVersion/FileVersion which don't support semantic suffixes
    $cleanVersion = $Version.Split('-')[0]
    
    # 1. Update shared version source
    if (Test-Path "Directory.Build.props") {
        $propsContent = Get-Content "Directory.Build.props" -Raw
        $newProps = $propsContent -replace "<TrackerVersion>.*?</TrackerVersion>", "<TrackerVersion>$Version</TrackerVersion>"
        $newProps = $newProps -replace "<TrackerAssemblyVersion>.*?</TrackerAssemblyVersion>", "<TrackerAssemblyVersion>$cleanVersion</TrackerAssemblyVersion>"
        Set-Content "Directory.Build.props" $newProps -NoNewline
        Write-Host "  Updated Directory.Build.props" -ForegroundColor Gray
    }

    # 2. Update README.md badge
    if (Test-Path "README.md") {
        $escapedVersion = $Version -replace "-", "--"
        $readmeContent = Get-Content "README.md" -Raw
        $newReadme = [regex]::Replace($readmeContent, "!\[Version\]\(https://img\.shields\.io/badge/version-[^)]+\)", "![Version](https://img.shields.io/badge/version-$escapedVersion-orange)")
        # Update installation instructions version as well
        $newReadme = $newReadme -replace "AIUsageTracker_Setup_v[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.]+)?\.exe", "AIUsageTracker_Setup_v$($Version).exe"
        Set-Content "README.md" $newReadme -NoNewline
        Write-Host "  Updated README.md" -ForegroundColor Gray
    }

    # 3. Update scripts/setup.iss
    if (Test-Path "scripts\setup.iss") {
        $issContent = Get-Content "scripts\setup.iss" -Raw
        $newIss = $issContent -replace '#define MyAppVersion ".*"', "#define MyAppVersion `"$Version`""
        Set-Content "scripts\setup.iss" $newIss -NoNewline
        Write-Host "  Updated scripts/setup.iss" -ForegroundColor Gray
    }

    # 4. Update scripts/publish-app.ps1 (self)
    if (Test-Path "scripts\publish-app.ps1") {
        $selfContent = Get-Content "scripts\publish-app.ps1" -Raw
        $newSelf = $selfContent -replace "-Version [0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.]+)?", "-Version $Version"
        Set-Content "scripts\publish-app.ps1" $newSelf -NoNewline
        Write-Host "  Updated scripts/publish-app.ps1" -ForegroundColor Gray
    }
} else {
    # If Version not passed, extract from shared version source
    if (Test-Path "Directory.Build.props") {
        $propsContent = Get-Content "Directory.Build.props" -Raw
        if ($propsContent -match "<TrackerVersion>(.*?)</TrackerVersion>") {
            $Version = $matches[1]
        }
    }

    # Fallback to project file if shared source is missing
    if ([string]::IsNullOrEmpty($Version)) {
        $projectContent = Get-Content $projectPath -Raw
        if ($projectContent -match "<Version>(.*?)</Version>") {
            $Version = $matches[1]
        }
    }

    if ([string]::IsNullOrEmpty($Version)) {
        $Version = "1.5.1"
    }

    if ($Version -match "^\$\((.*?)\)$") {
        $propertyName = $matches[1]
        if (Test-Path "Directory.Build.props") {
            $propsContent = Get-Content "Directory.Build.props" -Raw
            if ($propsContent -match "<$propertyName>(.*?)</$propertyName>") {
                $Version = $matches[1]
            }
        }
    }
}

$cleanVersion = $Version.Split('-')[0]

$zipPath = ".\dist\AIUsageTracker_v$Version`_$Runtime.zip"

Write-Host "Preparing publish staging for $Runtime..." -ForegroundColor Cyan
Assert-GeneratedPath -RepositoryRoot $repoRoot -Path $publishDir
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $repoRoot "dist") -Force | Out-Null

if ($isWinPlatform) {
    $windowsProjects = @(
        @{ Name = "Tracker"; ProjectPath = ".\AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj"; ExeName = "AIUsageTracker.exe" },
        @{ Name = "Monitor"; ProjectPath = ".\AIUsageTracker.Monitor\AIUsageTracker.Monitor.csproj"; ExeName = "AIUsageTracker.Monitor.exe" },
        @{ Name = "Web"; ProjectPath = ".\AIUsageTracker.Web\AIUsageTracker.Web.csproj"; ExeName = "AIUsageTracker.Web.exe" },
        @{ Name = "CLI"; ProjectPath = ".\AIUsageTracker.CLI\AIUsageTracker.CLI.csproj"; ExeName = "AIUsageTracker.CLI.exe" }
    )

    foreach ($app in $windowsProjects) {
        $componentDir = Join-Path $publishDir $app.Name
        New-Item -ItemType Directory -Path $componentDir -Force | Out-Null

        Write-Host "Publishing $($app.Name) for $Runtime (Version: $Version)..." -ForegroundColor Cyan
        dotnet publish $app.ProjectPath `
            -c Release `
            -r $Runtime `
            --self-contained false `
            -o $componentDir `
            -p:PublishReadyToRun=false `
            -p:DebugType=None `
            -p:Version=$Version `
            -p:AssemblyVersion=$cleanVersion `
            -p:FileVersion=$cleanVersion

        if ($LASTEXITCODE -ne 0) { throw "Publishing $($app.Name) failed with exit code $LASTEXITCODE." }

        $outputExe = Join-Path $componentDir $app.ExeName
        if (Test-Path $outputExe) {
            Write-Host "Build Successful: $($app.Name) ($($app.ExeName)) created." -ForegroundColor Green
        } else {
            Write-Host "Build Failed: $($app.ExeName) not found in $componentDir." -ForegroundColor Red
            exit 1
        }
    }
} else {
    Write-Host "Publishing $projectName for $Runtime (Version: $Version)..." -ForegroundColor Cyan

    # For Linux/Mac (CLI), we want SingleFile but NOT AOT (cross-OS native compilation not supported)
    $singleFileParam = "-p:PublishSingleFile=true"
    $selfContained = "true"
    $aotParam = "-p:PublishAot=false"

    dotnet publish $projectPath `
        -c Release `
        -r $Runtime `
        --self-contained $selfContained `
        -o $publishDir `
        $singleFileParam `
        $aotParam `
        -p:PublishReadyToRun=true `
        -p:DebugType=None `
        -p:Version=$Version `
        -p:AssemblyVersion=$cleanVersion `
        -p:FileVersion=$cleanVersion
    if ($LASTEXITCODE -ne 0) { throw "Publishing $projectName failed with exit code $LASTEXITCODE." }
}

Write-Host "Copying documentation..." -ForegroundColor Cyan
Copy-Item ".\README.md" -Destination $publishDir
if (Test-Path ".\LICENSE") { Copy-Item ".\LICENSE" -Destination $publishDir }

Write-Host "Verifying output..." -ForegroundColor Cyan
if (-not $isWinPlatform) {
    $exeName = $projectName
    if (Test-Path "$publishDir\$exeName") {
        Write-Host "Build Successful: $exeName created." -ForegroundColor Green
    } else {
        Write-Host "Build Failed: $exeName not found in $publishDir." -ForegroundColor Red
        exit 1
    }
}

Write-Host "Creating Distribution ZIP..." -ForegroundColor Cyan
# We compress the whole publish directory
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Inno Setup Installer (only available on Windows hosts).
if ($isWinPlatform -and [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    $isccLocal = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    $isccX86 = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    $iscc = if (Test-Path $isccLocal) { $isccLocal } else { $isccX86 }

    if ($iscc -and (Test-Path $iscc)) {
        Write-Host "Compiling Inno Setup Installer using $iscc..." -ForegroundColor Cyan
        Write-Host "Installer compression profile: $InstallerCompression" -ForegroundColor Cyan
        
        $archDef = if ($Runtime -like "*x64") { "x64" } elseif ($Runtime -like "*arm64") { "arm64" } else { "x86" }
        
        $installerName = "AIUsageTracker_Setup_v$($Version)_$Runtime"
        & $iscc "scripts\setup.iss" "/DSourcePath=$publishDir" "/DMyAppVersion=$Version" "/DMyAppArch=$archDef" "/DInstallerCompression=$InstallerCompression" "/F$installerName"
        if ($LASTEXITCODE -eq 0) {
            $installerPath = Join-Path $repoRoot "dist/$installerName.exe"
            if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw "Installer not found: $installerPath" }
            Write-Host "Installer created: $installerName.exe" -ForegroundColor Green
        } else {
            Write-Host "Inno Setup compilation failed." -ForegroundColor Yellow
            exit 1
        }
    } else {
        Write-Host "Error: Inno Setup (ISCC.exe) not found. This is required for Windows builds." -ForegroundColor Red
        exit 1
    }
}
elseif ($isWinPlatform) {
    Write-Host "Skipping Windows installer creation on this non-Windows host; ZIP packaging is complete." -ForegroundColor Yellow
}

Write-Host "--------------------------------------------------" -ForegroundColor Yellow
Write-Host "Distribution ready at: $zipPath" -ForegroundColor Green
Write-Host "Size: $((Get-Item $zipPath).Length / 1MB) MB" -ForegroundColor Gray
Write-Host "--------------------------------------------------" -ForegroundColor Yellow
}
finally {
    Pop-Location
}
