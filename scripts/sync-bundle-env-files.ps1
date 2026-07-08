param(
    [string]$BundleRoot = (Join-Path $PSScriptRoot '..\mcp-bundle')
)

$ErrorActionPreference = 'Stop'
$BundleRoot = (Resolve-Path $BundleRoot).Path
$configPath = Join-Path $BundleRoot 'servers.json'

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Bundle config not found: $configPath"
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json

$defaultEnv = @{
    'mcp-mssql' = [ordered]@{
        MSSQL_CONNECTION_STRING = ''
        MCP_ENABLE_SQL_WRITES = 'true'
    }
    'mcp-postgresql' = [ordered]@{
        POSTGRES_CONNECTION_STRING = ''
        MCP_ENABLE_POSTGRES_WRITES = 'true'
    }
    'mcp-gitlab' = [ordered]@{
        GITLAB_BASE_URL = 'http://localhost'
        GITLAB_TOKEN = ''
        MCP_ENABLE_GITLAB_WRITES = 'true'
    }
    'mcp-jira' = [ordered]@{
        JIRA_BASE_URL = 'http://localhost'
        JIRA_BEARER_TOKEN = ''
        JIRA_EMAIL = ''
        JIRA_API_TOKEN = ''
        MCP_ENABLE_JIRA_WRITES = 'true'
    }
    'mcp-loki' = [ordered]@{
        LOKI_BASE_URL = 'http://localhost:3100'
        LOKI_BEARER_TOKEN = ''
        LOKI_USERNAME = ''
        LOKI_PASSWORD = ''
        LOKI_TENANT_ID = ''
    }
    'mcp-confluence' = [ordered]@{
        CONFLUENCE_BASE_URL = 'http://localhost'
        CONFLUENCE_BEARER_TOKEN = ''
        CONFLUENCE_PAT = ''
        CONFLUENCE_USERNAME = ''
        CONFLUENCE_API_TOKEN = ''
        CONFLUENCE_COOKIE = ''
        MCP_ENABLE_CONFLUENCE_WRITES = 'true'
    }
    'mcp-prometheus' = [ordered]@{
        PROMETHEUS_BASE_URL = 'http://localhost:9090'
        PROMETHEUS_BEARER_TOKEN = ''
    }
}

foreach ($server in $config.servers) {
    $workingDirectory = $server.workingDirectory.Replace('{manager}', $BundleRoot)
    New-Item -ItemType Directory -Force -Path $workingDirectory | Out-Null

    $envPath = Join-Path $workingDirectory "$($server.name).env"
    $envLines = @(
        "# Editable environment variables for $($server.name).",
        "# Restart the server through McpManager.exe after changing this file."
    )

    $envValues = [ordered]@{}
    if ($server.env) {
        foreach ($property in @($server.env.PSObject.Properties)) {
            $envValues[$property.Name] = $property.Value
        }
        $server.env = [pscustomobject]@{}
    }
    if ($defaultEnv.ContainsKey($server.name)) {
        foreach ($key in $defaultEnv[$server.name].Keys) {
            if (-not $envValues.Contains($key)) {
                $envValues[$key] = $defaultEnv[$server.name][$key]
            }
        }
    }
    foreach ($key in $envValues.Keys) {
        $envLines += "$key=$($envValues[$key])"
    }

    if (Test-Path -LiteralPath $envPath) {
        $existingKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($line in Get-Content -LiteralPath $envPath) {
            $trimmed = $line.Trim()
            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
                continue
            }
            $equals = $trimmed.IndexOf('=')
            if ($equals -gt 0) {
                [void]$existingKeys.Add($trimmed.Substring(0, $equals).Trim())
            }
        }
        $missingLines = @()
        foreach ($key in $envValues.Keys) {
            if (-not $existingKeys.Contains($key)) {
                $missingLines += "$key=$($envValues[$key])"
            }
        }
        if ($missingLines.Count -gt 0) {
            Add-Content -LiteralPath $envPath -Value @("", "# Added by sync-env-files.ps1") -Encoding UTF8
            Add-Content -LiteralPath $envPath -Value $missingLines -Encoding UTF8
        }
    }
    else {
        Set-Content -LiteralPath $envPath -Value $envLines -Encoding UTF8
    }

@"
@echo off
setlocal
if not exist "%~dp0$($server.name)-win-x64\$($server.name).env" (
  echo # Editable environment variables for $($server.name).>"%~dp0$($server.name)-win-x64\$($server.name).env"
  echo # Restart the server through McpManager.exe after changing this file.>>"%~dp0$($server.name)-win-x64\$($server.name).env"
)
notepad.exe "%~dp0$($server.name)-win-x64\$($server.name).env"
"@ | Set-Content -LiteralPath (Join-Path $BundleRoot "edit-env-$($server.name).cmd") -Encoding ASCII
}

@'
@echo off
setlocal
explorer.exe "%~dp0"
'@ | Set-Content -LiteralPath (Join-Path $BundleRoot 'edit-envs.cmd') -Encoding ASCII

$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8
Write-Host "Synced editable env files in $BundleRoot"
