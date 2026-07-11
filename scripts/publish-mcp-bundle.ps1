param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\mcp-bundle'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $false,
    [bool]$SingleFile = $false,
    [bool]$BundleDotnetRuntime = $true,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Stop-ExistingBundleProcesses {
    param(
        [Parameter(Mandatory = $true)] [string] $BundleRoot
    )

    $configPath = Join-Path $BundleRoot 'servers.json'
    if (-not (Test-Path -LiteralPath $configPath)) {
        return
    }

    Write-Host "== Stopping running bundle processes"
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    foreach ($server in $config.servers) {
        if ($server.kind -ne 'process') {
            continue
        }

        $workingDirectory = $server.workingDirectory.Replace('{manager}', $BundleRoot)
        $exe = Join-Path $workingDirectory $server.executable
        if (-not (Test-Path -LiteralPath $exe)) {
            continue
        }

        $resolvedExe = (Resolve-Path -LiteralPath $exe).Path
        $processes = Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and ([string]::Equals($_.Path, $resolvedExe, [StringComparison]::OrdinalIgnoreCase)) }
            catch { $false }
        }
        foreach ($process in $processes) {
            Write-Host ("Stopping {0} PID {1}" -f $server.name, $process.Id)
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            try { Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue } catch { }
        }
    }
}

Stop-ExistingBundleProcesses -BundleRoot $OutputRoot

function Copy-BundledDotnetRuntime {
    param(
        [Parameter(Mandatory = $true)] [string] $BundleRoot,
        [Parameter(Mandatory = $true)] [string] $Runtime
    )

    if ($Runtime -ne 'win-x64') {
        throw "Bundled shared .NET runtime copy currently supports win-x64 only. Runtime was '$Runtime'."
    }

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $dotnetRoot = Split-Path -Parent $dotnet
    $runtimeRoot = Join-Path $BundleRoot 'dotnet'
    if (Test-Path -LiteralPath $runtimeRoot) {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }

    $netCoreVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    $aspNetVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared\Microsoft.AspNetCore.App') -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    $hostFxrVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if ($null -eq $netCoreVersion -or $null -eq $aspNetVersion -or $null -eq $hostFxrVersion) {
        throw "Could not locate installed Microsoft.NETCore.App, Microsoft.AspNetCore.App, or host/fxr runtime under $dotnetRoot."
    }

    Write-Host ""
    Write-Host "== Copying shared .NET runtime once"
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -Destination (Join-Path $runtimeRoot 'dotnet.exe') -Force
    foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        $source = Join-Path $dotnetRoot $notice
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $runtimeRoot $notice) -Force
        }
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $runtimeRoot 'host\fxr') | Out-Null
    Copy-Item -LiteralPath $hostFxrVersion.FullName -Destination (Join-Path $runtimeRoot 'host\fxr') -Recurse -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $runtimeRoot 'shared\Microsoft.NETCore.App') | Out-Null
    Copy-Item -LiteralPath $netCoreVersion.FullName -Destination (Join-Path $runtimeRoot 'shared\Microsoft.NETCore.App') -Recurse -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $runtimeRoot 'shared\Microsoft.AspNetCore.App') | Out-Null
    Copy-Item -LiteralPath $aspNetVersion.FullName -Destination (Join-Path $runtimeRoot 'shared\Microsoft.AspNetCore.App') -Recurse -Force

    @"
@echo off
set "DOTNET_ROOT=%~dp0dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "PATH=%DOTNET_ROOT%;%PATH%"
"@ | Set-Content -LiteralPath (Join-Path $BundleRoot 'runtime-env.cmd') -Encoding ASCII
}

Write-Host "== Publishing manager"
& (Join-Path $repoRoot 'mcp-manager\scripts\publish-win.ps1') `
    -Output $OutputRoot `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -SelfContained $SelfContained `
    -SingleFile $SingleFile

Copy-Item -LiteralPath (Join-Path $repoRoot 'mcp-manager\config\servers.bundle.json') -Destination (Join-Path $OutputRoot 'servers.json') -Force

$servers = @(
    @{ Name = 'mcp-office'; Project = 'mcp-office\src\McpOffice.csproj'; Folder = 'mcp-office-win-x64'; Env = 'mcp-office\config\office.env.example' },
    @{ Name = 'mcp-filesystem'; Project = 'mcp-filesystem\src\McpFilesystem.csproj'; Folder = 'mcp-filesystem-win-x64' },
    @{ Name = 'mcp-git'; Project = 'mcp-git\src\McpGit.csproj'; Folder = 'mcp-git-win-x64' },
    @{ Name = 'mcp-shell'; Project = 'mcp-shell\src\McpShell.csproj'; Folder = 'mcp-shell-win-x64' },
    @{ Name = 'mcp-dotnet'; Project = 'mcp-dotnet\src\McpDotnet.csproj'; Folder = 'mcp-dotnet-win-x64' },
    @{ Name = 'mcp-mssql'; Project = 'mcp-mssql\src\McpMssql.csproj'; Folder = 'mcp-mssql-win-x64' },
    @{ Name = 'mcp-hwp'; Project = 'mcp-hwp\src\McpHwp.csproj'; Folder = 'mcp-hwp-win-x64' },
    @{ Name = 'mcp-kubernetes'; Project = 'mcp-kubernetes\src\McpKubernetes.csproj'; Folder = 'mcp-kubernetes-win-x64' },
    @{ Name = 'mcp-docker'; Project = 'mcp-docker\src\McpDocker.csproj'; Folder = 'mcp-docker-win-x64' },
    @{ Name = 'mcp-prometheus'; Project = 'mcp-prometheus\src\McpPrometheus.csproj'; Folder = 'mcp-prometheus-win-x64' },
    @{ Name = 'mcp-postgresql'; Project = 'mcp-postgresql\src\McpPostgresql.csproj'; Folder = 'mcp-postgresql-win-x64' },
    @{ Name = 'mcp-gitlab'; Project = 'mcp-gitlab\src\McpGitLab.csproj'; Folder = 'mcp-gitlab-win-x64' },
    @{ Name = 'mcp-jira'; Project = 'mcp-jira\src\McpJira.csproj'; Folder = 'mcp-jira-win-x64' },
    @{ Name = 'mcp-loki'; Project = 'mcp-loki\src\McpLoki.csproj'; Folder = 'mcp-loki-win-x64' },
    @{ Name = 'mcp-confluence'; Project = 'mcp-confluence\src\McpConfluence.csproj'; Folder = 'mcp-confluence-win-x64' },
    @{ Name = 'mcp-rhapsody'; Script = 'mcp-rhapsody\scripts\publish-win.ps1'; Folder = 'mcp-rhapsody-win-x64' },
    @{ Name = 'mcp-matlab'; Script = 'mcp-matlab\scripts\publish-win.ps1'; Folder = 'mcp-matlab-win-x64' },
    @{ Name = 'mcp-autocad'; Script = 'mcp-autocad\scripts\publish-win.ps1'; Folder = 'mcp-autocad-win-x64' },
    @{ Name = 'mcp-solidworks'; Script = 'mcp-solidworks\scripts\publish-win.ps1'; Folder = 'mcp-solidworks-win-x64' }
)

foreach ($server in $servers) {
    Write-Host ""
    Write-Host "== Publishing $($server.Name)"
    $output = Join-Path $OutputRoot $server.Folder
    if ($server.Script) {
        & (Join-Path $repoRoot $server.Script) `
            -Output $output `
            -Configuration $Configuration `
            -Runtime $Runtime `
            -SelfContained $SelfContained `
            -SingleFile $SingleFile
    }
    else {
        $project = Join-Path $repoRoot $server.Project
        $publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $output, '/p:UseAppHost=true')
        if ($SingleFile) {
            $publishArgs += '/p:PublishSingleFile=true'
            $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
        }
        dotnet publish @publishArgs
    }

    $exe = Get-ChildItem -LiteralPath $output -Filter '*.exe' | Where-Object { $_.Name -ne 'createdump.exe' } | Select-Object -First 1
    $dll = if ($null -ne $exe) { Get-ChildItem -LiteralPath $output -Filter "$($exe.BaseName).dll" | Select-Object -First 1 } else { $null }
    if ($null -ne $exe -and $null -ne $dll -and $BundleDotnetRuntime -and -not $SelfContained) {
        @"
@echo off
setlocal
if exist "%~dp0..\runtime-env.cmd" call "%~dp0..\runtime-env.cmd"
"%~dp0$($exe.Name)" %*
"@ | Set-Content -LiteralPath (Join-Path $output 'start.cmd') -Encoding ASCII
    }
    elseif ($null -ne $exe) {
        @"
@echo off
setlocal
"%~dp0$($exe.Name)" %*
"@ | Set-Content -LiteralPath (Join-Path $output 'start.cmd') -Encoding ASCII
    }

    if ($server.Name -eq 'mcp-office') {
        $officeCliVendor = Join-Path $repoRoot 'mcp-office\vendor\officecli\officecli.exe'
        if (-not (Test-Path -LiteralPath $officeCliVendor)) {
            Write-Host "Downloading OfficeCLI for Windows bundle"
            & (Join-Path $repoRoot 'mcp-office\scripts\download-officecli.ps1')
        }

        $toolsDir = Join-Path $output 'tools'
        New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
        Copy-Item -LiteralPath $officeCliVendor -Destination (Join-Path $toolsDir 'officecli.exe') -Force
        foreach ($sidecar in @('officecli.sha256', 'README.txt')) {
            $source = Join-Path $repoRoot "mcp-office\vendor\officecli\$sidecar"
            if (Test-Path -LiteralPath $source) {
                Copy-Item -LiteralPath $source -Destination (Join-Path $toolsDir $sidecar) -Force
            }
        }
    }
}

$configPath = Join-Path $OutputRoot 'servers.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
foreach ($server in $config.servers) {
    if (-not $server.env) {
        continue
    }

    $envProperties = @($server.env.PSObject.Properties)
    if ($envProperties.Count -eq 0) {
        continue
    }

    $workingDirectory = $server.workingDirectory.Replace('{manager}', $OutputRoot)
    New-Item -ItemType Directory -Force -Path $workingDirectory | Out-Null
    $envPath = Join-Path $workingDirectory "$($server.name).env"
    $envLines = @(
        "# Editable environment variables for $($server.name).",
        "# Restart the server through McpManager.exe after changing this file."
    )
    foreach ($property in $envProperties) {
        $envLines += "$($property.Name)=$($property.Value)"
    }
    Set-Content -LiteralPath $envPath -Value $envLines -Encoding UTF8
@"
@echo off
setlocal
notepad.exe "%~dp0$($server.name)-win-x64\$($server.name).env"
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot "edit-env-$($server.name).cmd") -Encoding ASCII
    $server.env = [pscustomobject]@{}
}
$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8

# Installable bundles do not need debug symbols or stale backup binaries.
foreach ($pattern in '*.pdb', '*.old') {
    Get-ChildItem -LiteralPath $OutputRoot -Recurse -File -Filter $pattern | Remove-Item -Force
}

if ($BundleDotnetRuntime -and -not $SelfContained) {
    Copy-BundledDotnetRuntime -BundleRoot $OutputRoot -Runtime $Runtime
}

@'
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" %*
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'mcp-manager.cmd') -Encoding ASCII

@'
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" %*
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'LIG-AI-MCP.cmd') -Encoding ASCII

@'
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" start all
pause
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'start-all.cmd') -Encoding ASCII
@'
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" stop all
pause
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'stop-all.cmd') -Encoding ASCII
@'
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" status all
pause
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'status.cmd') -Encoding ASCII
@'
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" urls all
pause
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'urls.cmd') -Encoding ASCII
@'
@echo off
setlocal
explorer.exe "%~dp0"
'@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'edit-envs.cmd') -Encoding ASCII

$config = Get-Content -LiteralPath (Join-Path $OutputRoot 'servers.json') -Raw | ConvertFrom-Json
foreach ($server in $config.servers) {
    $workingDirectory = $server.workingDirectory.Replace('{manager}', $OutputRoot)
    $envPath = Join-Path $workingDirectory "$($server.name).env"
@"
@echo off
setlocal
if not exist "%~dp0$($server.name)-win-x64\$($server.name).env" (
  echo # Editable environment variables for $($server.name).>"%~dp0$($server.name)-win-x64\$($server.name).env"
  echo # Restart the server through McpManager.exe after changing this file.>>"%~dp0$($server.name)-win-x64\$($server.name).env"
)
notepad.exe "%~dp0$($server.name)-win-x64\$($server.name).env"
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot "edit-env-$($server.name).cmd") -Encoding ASCII
@"
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" start $($server.name)
pause
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot "start-$($server.name).cmd") -Encoding ASCII
@"
@echo off
setlocal
if exist "%~dp0runtime-env.cmd" call "%~dp0runtime-env.cmd"
"%~dp0McpManager.exe" stop $($server.name)
pause
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot "stop-$($server.name).cmd") -Encoding ASCII
}

if ($Zip) {
    $zipPath = "$OutputRoot.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $OutputRoot '*') -DestinationPath $zipPath -Force
    Write-Host "Created $zipPath"
}

Write-Host ""
Write-Host "MCP bundle is ready in $OutputRoot"
