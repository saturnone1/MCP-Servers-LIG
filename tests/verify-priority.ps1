param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipBuild,
    [switch]$SkipImagePull,
    [string]$RhapsodyProjectPath = '',
    [switch]$RunRhapsodyWriteSmoke
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message"
}

function Invoke-TestScript([string]$Name, [scriptblock]$Script) {
    Write-Step $Name
    & $Script
    if (-not $?) {
        throw "$Name failed."
    }
    $global:LASTEXITCODE = 0
}

try {
    Invoke-TestScript 'Docker MCP smoke' {
        $scriptArgs = @{ Workspace = $Workspace }
        if ($SkipBuild) { $scriptArgs.SkipBuild = $true }
        & (Join-Path $PSScriptRoot 'mcp-smoke.ps1') @scriptArgs
    }

    Invoke-TestScript 'PostgreSQL fixture smoke' {
        $scriptArgs = @{ Workspace = $Workspace }
        if ($SkipImagePull) { $scriptArgs.SkipImagePull = $true }
        & (Join-Path $PSScriptRoot 'db-fixture-smoke.ps1') @scriptArgs
    }

    Invoke-TestScript 'SQL Server fixture smoke' {
        $scriptArgs = @{ Workspace = $Workspace }
        if ($SkipImagePull) { $scriptArgs.SkipImagePull = $true }
        & (Join-Path $PSScriptRoot 'mssql-fixture-smoke.ps1') @scriptArgs
    }

    Invoke-TestScript 'Rhapsody MCP smoke' {
        $scriptArgs = @{}
        if (-not [string]::IsNullOrWhiteSpace($RhapsodyProjectPath)) {
            $scriptArgs.RhapsodyProjectPath = $RhapsodyProjectPath
        }
        if ($RunRhapsodyWriteSmoke) {
            $scriptArgs.RunWriteSmoke = $true
        }
        & (Join-Path $PSScriptRoot 'rhapsody-smoke.ps1') @scriptArgs
    }

    Write-Host ""
    Write-Host "Priority verification completed."
}
finally {
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $Workspace 'mcp-rhapsody\src\bin') -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $Workspace 'mcp-rhapsody\src\obj') -ErrorAction SilentlyContinue
}
