param(
    [string]$BundleRoot = (Join-Path $PSScriptRoot '..\mcp-bundle')
)

$ErrorActionPreference = 'Stop'
$BundleRoot = (Resolve-Path $BundleRoot).Path
$configPath = Join-Path $BundleRoot 'servers.json'
$managerPath = Join-Path $BundleRoot 'McpManager.exe'
$launcherPath = Join-Path $BundleRoot 'LIG-AI-MCP.cmd'
$bundledDotnetPath = Join-Path $BundleRoot 'dotnet\dotnet.exe'
$managerProject = Join-Path $PSScriptRoot '..\mcp-manager\src\McpManager.csproj'
$managerDll = Join-Path $PSScriptRoot '..\mcp-manager\src\bin\Release\net10.0\McpManager.dll'

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Bundle config not found: $configPath"
}
if (-not (Test-Path -LiteralPath $managerPath)) {
    throw "McpManager.exe not found: $managerPath"
}
if (-not (Test-Path -LiteralPath $launcherPath)) {
    throw "LIG-AI-MCP.cmd not found: $launcherPath"
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$missing = @()
$releaseDebris = Get-ChildItem -LiteralPath $BundleRoot -Recurse -File |
    Where-Object { $_.Extension -in '.pdb', '.old' }

Write-Host "== Release optimization"
if ($releaseDebris) {
    $releaseDebris | ForEach-Object { Write-Host ("MISS release cleanup: {0}" -f $_.FullName) -ForegroundColor Red }
}
else {
    Write-Host "OK   no debug symbols or stale backup binaries"
}

Write-Host "== Bundled .NET runtime"
if (Test-Path -LiteralPath $bundledDotnetPath) {
    Write-Host ("OK   {0}" -f $bundledDotnetPath)
}
else {
    Write-Host ("WARN {0} not bundled; target PC must have matching .NET and ASP.NET Core runtimes installed" -f $bundledDotnetPath) -ForegroundColor Yellow
}

Write-Host "== Bundle executables"
foreach ($server in $config.servers) {
    $workingDirectory = $server.workingDirectory.Replace('{manager}', $BundleRoot)
    $exe = Join-Path $workingDirectory $server.executable
    if (Test-Path -LiteralPath $exe) {
        Write-Host ("OK   {0,-18} {1}" -f $server.name, $exe)
    }
    else {
        Write-Host ("MISS {0,-18} {1}" -f $server.name, $exe) -ForegroundColor Red
        $missing += $exe
    }
}

Write-Host ""
Write-Host "== Bundled command dependencies"
$bundledDependencies = [ordered]@{
    'mcp-office' = @('tools\officecli.exe')
}

foreach ($entry in $bundledDependencies.GetEnumerator()) {
    foreach ($relativePath in $entry.Value) {
        $server = $config.servers | Where-Object name -eq $entry.Key | Select-Object -First 1
        $workingDirectory = $server.workingDirectory.Replace('{manager}', $BundleRoot)
        $candidate = Join-Path $workingDirectory $relativePath
        if (Test-Path -LiteralPath $candidate) {
            Write-Host ("OK   {0,-18} {1}" -f $entry.Key, $candidate)
        }
        else {
            Write-Host ("WARN {0,-18} {1} not bundled" -f $entry.Key, $relativePath) -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "== Required external PATH dependencies"
$dependencies = [ordered]@{
    'mcp-git'        = @('git')
    'mcp-dotnet'     = @('dotnet')
    'mcp-kubernetes' = @('kubectl')
    'mcp-docker'     = @('docker')
}

foreach ($entry in $dependencies.GetEnumerator()) {
    foreach ($command in $entry.Value) {
        $found = Get-Command $command -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            Write-Host ("OK   {0,-18} {1,-10} {2}" -f $entry.Key, $command, $found.Source)
        }
        else {
            Write-Host ("WARN {0,-18} {1,-10} not found on PATH" -f $entry.Key, $command) -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "== Optional external PATH dependencies"
$optionalDependencies = [ordered]@{
    'mcp-office' = @('antiword')
    'mcp-hwp'    = @('hwp5txt', 'soffice')
    'mcp-pdf'    = @('docling', 'pdftoppm')
}

foreach ($entry in $optionalDependencies.GetEnumerator()) {
    foreach ($command in $entry.Value) {
        $found = Get-Command $command -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            Write-Host ("OK   {0,-18} {1,-10} {2}" -f $entry.Key, $command, $found.Source)
        }
        else {
            Write-Host ("INFO {0,-18} {1,-10} not found; fallback/limited mode will be used" -f $entry.Key, $command) -ForegroundColor Cyan
        }
    }
}

Write-Host ""
Write-Host "== Manager view"
dotnet build $managerProject -c Release --no-restore | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Manager validation build failed with exit code $LASTEXITCODE."
}

$previousConfig = [Environment]::GetEnvironmentVariable('MCP_MANAGER_CONFIG', 'Process')
$previousState = [Environment]::GetEnvironmentVariable('MCP_MANAGER_STATE_DIR', 'Process')
$validationState = Join-Path ([IO.Path]::GetTempPath()) ("lig-mcp-manager-validation-" + [Guid]::NewGuid().ToString('N'))
try {
    $env:MCP_MANAGER_CONFIG = $configPath
    $env:MCP_MANAGER_STATE_DIR = $validationState
    $managerOutput = @(& dotnet $managerDll list all 2>&1)
    $managerExitCode = $LASTEXITCODE
    $managerOutput | ForEach-Object { Write-Host $_ }
    if ($managerExitCode -ne 0) {
        throw "Manager list validation failed with exit code $managerExitCode."
    }

    $managerText = $managerOutput -join [Environment]::NewLine
    foreach ($server in $config.servers) {
        if ($managerText -notmatch [Regex]::Escape([string]$server.name)) {
            throw "Manager output did not include bundle server: $($server.name)"
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('MCP_MANAGER_CONFIG', $previousConfig, 'Process')
    [Environment]::SetEnvironmentVariable('MCP_MANAGER_STATE_DIR', $previousState, 'Process')
    if (Test-Path -LiteralPath $validationState) {
        Remove-Item -LiteralPath $validationState -Recurse -Force
    }
}

if ($missing.Count -gt 0) {
    throw "Bundle has missing server executables."
}
if ($releaseDebris) {
    throw "Bundle contains debug symbols or stale backup binaries."
}
