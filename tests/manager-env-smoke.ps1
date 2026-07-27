param(
    [switch]$SkipBuild,
    [string]$ConfigPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'mcp-manager\src\McpManager.csproj'
$managerDll = Join-Path $repo 'mcp-manager\src\bin\Release\net10.0\McpManager.dll'
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repo 'mcp-manager\config\servers.bundle.json'
}
$configPath = (Resolve-Path -LiteralPath $ConfigPath).Path
$stateRoot = Join-Path ([IO.Path]::GetTempPath()) ("lig-ai-mcp-env-smoke-" + [guid]::NewGuid().ToString('N'))
$previousConfig = $env:MCP_MANAGER_CONFIG
$previousState = $env:MCP_MANAGER_STATE_DIR

try {
    if (-not $SkipBuild) {
        dotnet build $project -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Manager build failed with exit code $LASTEXITCODE." }
    }
    if (-not (Test-Path -LiteralPath $managerDll)) { throw "Manager DLL not found: $managerDll" }

    $env:MCP_MANAGER_CONFIG = $configPath
    $env:MCP_MANAGER_STATE_DIR = $stateRoot
    $envDir = Join-Path $stateRoot 'env'
    New-Item -ItemType Directory -Force -Path $envDir | Out-Null
    $jiraEnv = Join-Path $envDir 'mcp-jira.env'
    @('# Existing file from 1.0.14', 'JIRA_BASE_URL=https://jira.example.internal') |
        Set-Content -LiteralPath $jiraEnv -Encoding UTF8

    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    foreach ($server in $config.servers) {
        & dotnet $managerDll env $server.name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "env command failed for $($server.name)." }

        $envPath = Join-Path $envDir "$($server.name).env"
        if (-not (Test-Path -LiteralPath $envPath)) { throw "Editable env file missing for $($server.name)." }
        $text = Get-Content -LiteralPath $envPath -Raw
        foreach ($property in $server.env.PSObject.Properties) {
            if ($text -notmatch "(?m)^$([regex]::Escape($property.Name))=") {
                throw "$($server.name) editable env is missing $($property.Name)."
            }
        }
    }

    $jiraText = Get-Content -LiteralPath $jiraEnv -Raw
    if ($jiraText -notmatch '(?m)^JIRA_BASE_URL=https://jira\.example\.internal\r?$') {
        throw 'Existing user environment value was overwritten during migration.'
    }
    if ($jiraText -notmatch '(?m)^JIRA_API_TOKEN=') {
        throw 'Missing Jira defaults were not merged into the existing user file.'
    }

    & dotnet $managerDll set-env mcp-jira JIRA_BASE_URL https://jira.changed.internal | Out-Null
    if ($LASTEXITCODE -ne 0 -or (Get-Content -LiteralPath $jiraEnv -Raw) -notmatch '(?m)^JIRA_BASE_URL=https://jira\.changed\.internal\r?$') {
        throw 'set-env did not persist the user value.'
    }
    & dotnet $managerDll remove-env mcp-jira JIRA_BASE_URL | Out-Null
    if ($LASTEXITCODE -ne 0 -or (Get-Content -LiteralPath $jiraEnv -Raw) -notmatch '(?m)^JIRA_BASE_URL=http://localhost\r?$') {
        throw 'remove-env did not restore the published default.'
    }

    Write-Host 'Manager environment defaults and upgrade migration smoke passed.' -ForegroundColor Green
}
finally {
    $env:MCP_MANAGER_CONFIG = $previousConfig
    $env:MCP_MANAGER_STATE_DIR = $previousState
    if (Test-Path -LiteralPath $stateRoot) {
        Remove-Item -LiteralPath $stateRoot -Recurse -Force
    }
}
