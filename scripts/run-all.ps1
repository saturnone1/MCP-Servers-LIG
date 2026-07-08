param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$HostDriveRoot = 'C:\',
    [string]$HostDriveMount = '/host/c',
    [string]$MssqlConnectionString = '',
    [string]$PostgresConnectionString = '',
    [string]$PrometheusBaseUrl = '',
    [string]$PrometheusBearerToken = '',
    [string]$GitLabBaseUrl = '',
    [string]$GitLabToken = '',
    [string]$JiraBaseUrl = '',
    [string]$JiraBearerToken = '',
    [string]$JiraEmail = '',
    [string]$JiraApiToken = '',
    [string]$ConfluenceBaseUrl = '',
    [string]$ConfluenceBearerToken = '',
    [string]$ConfluencePat = '',
    [string]$ConfluenceUsername = '',
    [string]$ConfluenceApiToken = '',
    [string]$ConfluenceCookie = '',
    [string]$LokiBaseUrl = '',
    [string]$LokiBearerToken = '',
    [string]$LokiUsername = '',
    [string]$LokiPassword = '',
    [string]$LokiTenantId = '',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$servers = @(
    @{ Name = 'mcp-office'; Port = 42180 },
    @{ Name = 'mcp-filesystem'; Port = 42181 },
    @{ Name = 'mcp-git'; Port = 42182 },
    @{ Name = 'mcp-shell'; Port = 42183 },
    @{ Name = 'mcp-dotnet'; Port = 42184 },
    @{ Name = 'mcp-mssql'; Port = 42185 },
    @{ Name = 'mcp-hwp'; Port = 42186 },
    @{ Name = 'mcp-kubernetes'; Port = 42187 },
    @{ Name = 'mcp-docker'; Port = 42188 },
    @{ Name = 'mcp-prometheus'; Port = 42189 },
    @{ Name = 'mcp-postgresql'; Port = 42190 },
    @{ Name = 'mcp-gitlab'; Port = 42191 },
    @{ Name = 'mcp-jira'; Port = 42192 },
    @{ Name = 'mcp-loki'; Port = 42193 },
    @{ Name = 'mcp-confluence'; Port = 42198 }
)

if ($Build) {
    foreach ($server in $servers.Name) {
        docker build -t "local/$server" (Join-Path $Workspace $server)
    }
}

$existing = docker ps -a --filter 'name=mcp-' --format '{{.Names}}'
if ($existing) {
    docker stop $existing *> $null
    docker rm $existing *> $null
}

$pathMappings = @("${Workspace}=/workspace")
$mounts = @('-v', "${Workspace}:/workspace")

if (Test-Path -LiteralPath $HostDriveRoot) {
    $mounts += @('-v', "$($HostDriveRoot):$HostDriveMount")
    $pathMappings += "$HostDriveRoot=$HostDriveMount"
}

foreach ($server in $servers) {
    $args = @('run', '-d', '--name', $server.Name, '-p', "$($server.Port):8080")
    $args += $mounts
    $args += @('-e', "MCP_PATH_MAPPINGS=$($pathMappings -join ';')")
    if ($server.Name -eq 'mcp-mssql' -and -not [string]::IsNullOrWhiteSpace($MssqlConnectionString)) {
        $args += @('-e', "MSSQL_CONNECTION_STRING=$MssqlConnectionString")
    }
    if ($server.Name -eq 'mcp-postgresql' -and -not [string]::IsNullOrWhiteSpace($PostgresConnectionString)) {
        $args += @('-e', "POSTGRES_CONNECTION_STRING=$PostgresConnectionString")
    }
    if ($server.Name -eq 'mcp-prometheus') {
        if (-not [string]::IsNullOrWhiteSpace($PrometheusBaseUrl)) {
            $args += @('-e', "PROMETHEUS_BASE_URL=$PrometheusBaseUrl")
        }
        if (-not [string]::IsNullOrWhiteSpace($PrometheusBearerToken)) {
            $args += @('-e', "PROMETHEUS_BEARER_TOKEN=$PrometheusBearerToken")
        }
    }
    if ($server.Name -eq 'mcp-gitlab') {
        if (-not [string]::IsNullOrWhiteSpace($GitLabBaseUrl)) {
            $args += @('-e', "GITLAB_BASE_URL=$GitLabBaseUrl")
        }
        if (-not [string]::IsNullOrWhiteSpace($GitLabToken)) {
            $args += @('-e', "GITLAB_TOKEN=$GitLabToken")
        }
    }
    if ($server.Name -eq 'mcp-jira') {
        if (-not [string]::IsNullOrWhiteSpace($JiraBaseUrl)) {
            $args += @('-e', "JIRA_BASE_URL=$JiraBaseUrl")
        }
        if (-not [string]::IsNullOrWhiteSpace($JiraBearerToken)) {
            $args += @('-e', "JIRA_BEARER_TOKEN=$JiraBearerToken")
        }
        if (-not [string]::IsNullOrWhiteSpace($JiraEmail)) {
            $args += @('-e', "JIRA_EMAIL=$JiraEmail")
        }
        if (-not [string]::IsNullOrWhiteSpace($JiraApiToken)) {
            $args += @('-e', "JIRA_API_TOKEN=$JiraApiToken")
        }
    }
    if ($server.Name -eq 'mcp-loki') {
        if (-not [string]::IsNullOrWhiteSpace($LokiBaseUrl)) {
            $args += @('-e', "LOKI_BASE_URL=$LokiBaseUrl")
        }
        if (-not [string]::IsNullOrWhiteSpace($LokiBearerToken)) {
            $args += @('-e', "LOKI_BEARER_TOKEN=$LokiBearerToken")
        }
        if (-not [string]::IsNullOrWhiteSpace($LokiUsername)) {
            $args += @('-e', "LOKI_USERNAME=$LokiUsername")
        }
        if (-not [string]::IsNullOrWhiteSpace($LokiPassword)) {
            $args += @('-e', "LOKI_PASSWORD=$LokiPassword")
        }
        if (-not [string]::IsNullOrWhiteSpace($LokiTenantId)) {
            $args += @('-e', "LOKI_TENANT_ID=$LokiTenantId")
        }
    }
    if ($server.Name -eq 'mcp-confluence') {
        if (-not [string]::IsNullOrWhiteSpace($ConfluenceBaseUrl)) {
            $args += @('-e', "CONFLUENCE_BASE_URL=$ConfluenceBaseUrl")
        }
        if (-not [string]::IsNullOrWhiteSpace($ConfluenceBearerToken)) {
            $args += @('-e', "CONFLUENCE_BEARER_TOKEN=$ConfluenceBearerToken")
        }
        if (-not [string]::IsNullOrWhiteSpace($ConfluencePat)) {
            $args += @('-e', "CONFLUENCE_PAT=$ConfluencePat")
        }
        if (-not [string]::IsNullOrWhiteSpace($ConfluenceUsername)) {
            $args += @('-e', "CONFLUENCE_USERNAME=$ConfluenceUsername")
        }
        if (-not [string]::IsNullOrWhiteSpace($ConfluenceApiToken)) {
            $args += @('-e', "CONFLUENCE_API_TOKEN=$ConfluenceApiToken")
        }
        if (-not [string]::IsNullOrWhiteSpace($ConfluenceCookie)) {
            $args += @('-e', "CONFLUENCE_COOKIE=$ConfluenceCookie")
        }
    }
    if ($server.Name -eq 'mcp-docker') {
        $args += @('-v', '/var/run/docker.sock:/var/run/docker.sock')
    }
    if ($server.Name -eq 'mcp-kubernetes' -and (Test-Path -LiteralPath (Join-Path $HOME '.kube'))) {
        $args += @('-v', "$HOME\.kube:/root/.kube")
    }
    $args += "local/$($server.Name)"
    docker @args | Out-Null
}

Start-Sleep -Seconds 3
docker ps --format 'table {{.Names}}\t{{.Ports}}\t{{.Status}}' | Select-String 'mcp-'
