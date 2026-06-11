param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$HostDriveRoot = 'C:\',
    [string]$HostDriveMount = '/host/c',
    [string]$VirtualizationHostPath = 'C:\Users\taewon\Desktop\가상화',
    [string]$PostgresConnectionString = '',
    [string]$MssqlConnectionString = '',
    [switch]$SkipBuild,
    [switch]$SkipStart
)

$ErrorActionPreference = 'Stop'

$docPath = Join-Path $VirtualizationHostPath 'ICFICE_2024_Fullpaper_template.doc'
$hwpPath = Join-Path $VirtualizationHostPath '090717_174041909.hwp'
$hwpxFixturePath = Join-Path $PSScriptRoot 'fixtures\sample.hwpx'
$officeFixtureName = 'office-smoke-' + [guid]::NewGuid().ToString('N')
$officeFixturePath = Join-Path $PSScriptRoot "fixtures\$officeFixtureName.docx"
$officeSnapshotPath = Join-Path $PSScriptRoot "fixtures\$officeFixtureName.txt"
$officeContainerPath = "/workspace/tests/fixtures/$officeFixtureName.docx"
$mockApiBase = 'http://host.docker.internal'
$mockApiProcesses = @()

$servers = @(
    @{ Name = 'mcp-office'; Port = 8080; Tool = 'create_document'; Args = @{ path = $officeFixturePath }; ExtraCalls = @(
        @{ Tool = 'run_office_cli'; Args = @{ args = @('add', $officeContainerPath, '/body', '--type', 'paragraph', '--prop', 'text=Office smoke', '--json'); timeoutMs = 60000 } },
        @{ Tool = 'render_document'; Args = @{ documentPath = $officeFixturePath; outputPath = $officeSnapshotPath } },
        @{ Tool = 'extract_text'; Args = @{ path = $officeFixturePath; maxLines = 20 } },
        @{ Tool = 'extract_text'; Args = @{ path = $docPath; maxLines = 20 }; PathMustExist = $docPath; Label = 'extract_text legacy .doc fixture' }
    ) },
    @{ Name = 'mcp-filesystem'; Port = 8081; Tool = 'list_directory'; Args = @{ path = $Workspace; limit = 20 } },
    @{ Name = 'mcp-git'; Port = 8082; Tool = 'status'; Args = @{ repositoryPath = $Workspace } },
    @{ Name = 'mcp-shell'; Port = 8083; Tool = 'run_command'; Args = @{ command = 'pwd'; args = @(); workingDirectory = $Workspace; timeoutMs = 10000 } },
    @{ Name = 'mcp-dotnet'; Port = 8084; Tool = 'sdk_info'; Args = @{} },
    @{ Name = 'mcp-mssql'; Port = 8085; Tool = 'execute_read_query'; Args = @{ sql = 'select 1 as ok'; maxRows = 5 } },
    @{ Name = 'mcp-hwp'; Port = 8086; Tool = 'extract_text'; Args = @{ path = $hwpxFixturePath; maxChars = 4000 }; ExtraCalls = @(
        @{ Tool = 'extract_text'; Args = @{ path = $hwpPath; maxChars = 4000 }; PathMustExist = $hwpPath; Label = 'extract_text legacy .hwp fixture' },
        @{ Tool = 'convert'; Args = @{ path = $hwpPath; outputDirectory = '/tmp/hwp-output'; format = 'txt' }; PathMustExist = $hwpPath; Label = 'convert legacy .hwp fixture' }
    ) },
    @{ Name = 'mcp-kubernetes'; Port = 8087; Tool = 'version'; Args = @{ clientOnly = $true } },
    @{ Name = 'mcp-docker'; Port = 8088; Tool = 'version'; Args = @{} },
    @{ Name = 'mcp-prometheus'; Port = 8089; Tool = 'query'; Args = @{ query = 'up'; timeoutSeconds = 5 }; ExtraCalls = @(
        @{ Tool = 'labels'; Args = @{} }
    ) },
    @{ Name = 'mcp-postgresql'; Port = 8090; Tool = 'execute_read_query'; Args = @{ sql = 'select 1 as ok'; maxRows = 5 } },
    @{ Name = 'mcp-gitlab'; Port = 8091; Tool = 'list_projects'; Args = @{ perPage = 5 } },
    @{ Name = 'mcp-jira'; Port = 8092; Tool = 'list_projects'; Args = @{} },
    @{ Name = 'mcp-loki'; Port = 8093; Tool = 'labels'; Args = @{}; ExtraCalls = @(
        @{ Tool = 'recent_logs'; Args = @{ selector = '{job="smoke"}'; sinceMinutes = 5; limit = 5 } }
    ) }
)

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message"
}

function Assert-Docker {
    docker info *> $null
}

function Start-MockApiServer([string]$Kind, [int]$Port) {
    $scriptPath = Join-Path $PSScriptRoot 'mock-external-api.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if ([string]::IsNullOrWhiteSpace($pwsh)) {
        $pwsh = (Get-Command powershell -ErrorAction Stop).Source
    }
    $process = Start-Process -FilePath $pwsh -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $scriptPath,
        '-Kind',
        $Kind,
        '-Port',
        $Port
    ) -WindowStyle Hidden -PassThru

    for ($i = 0; $i -lt 30; $i++) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -TimeoutSec 1 | Out-Null
            return $process
        }
        catch {
            if ($process.HasExited) {
                throw "Mock API '$Kind' exited early."
            }
            Start-Sleep -Milliseconds 200
        }
    }

    throw "Mock API '$Kind' did not become ready on port $Port."
}

function Start-MockApiServers {
    try {
        $script:mockApiProcesses += Start-MockApiServer 'prometheus' 19100
        $script:mockApiProcesses += Start-MockApiServer 'gitlab' 19101
        $script:mockApiProcesses += Start-MockApiServer 'jira' 19102
        $script:mockApiProcesses += Start-MockApiServer 'loki' 19103
    }
    catch {
        Stop-MockApiServers
        throw
    }
}

function Stop-MockApiServers {
    foreach ($process in @($script:mockApiProcesses)) {
        if ($null -eq $process) { continue }
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    $script:mockApiProcesses = @()
}

function New-McpBody([int]$Id, [string]$Method, [hashtable]$Params) {
    return @{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 20
}

function ConvertFrom-SseJson([string]$Content) {
    $dataLine = ($Content -split "`r?`n" | Where-Object { $_ -like 'data: *' } | Select-Object -First 1)
    if (-not $dataLine) {
        return $Content | ConvertFrom-Json
    }
    return $dataLine.Substring(6) | ConvertFrom-Json
}

function Invoke-Mcp([string]$BaseUrl, [string]$SessionId, [int]$Id, [string]$Method, [hashtable]$Params = @{}) {
    $headers = @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
        'Mcp-Session-Id' = $SessionId
    }
    $response = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body (New-McpBody $Id $Method $Params) -TimeoutSec 60
    return ConvertFrom-SseJson $response.Content
}

function New-McpSession([string]$BaseUrl) {
    $headers = @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
    }
    $body = New-McpBody 1 'initialize' @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'mcp-smoke'; version = '1.0' }
    }
    $init = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body $body -TimeoutSec 60
    $sessionId = [string]$init.Headers['Mcp-Session-Id']
    if ([string]::IsNullOrWhiteSpace($sessionId)) {
        throw "No Mcp-Session-Id returned from $BaseUrl"
    }

    $initialized = @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } | ConvertTo-Json -Depth 10
    Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
        'Mcp-Session-Id' = $sessionId
    } -Body $initialized -TimeoutSec 60 | Out-Null

    return $sessionId
}

function Test-Sse([int]$Port) {
    $output = & curl.exe -i -N --max-time 2 "http://localhost:$Port/sse" 2>&1
    $text = $output -join "`n"
    if ($text -notmatch 'HTTP/1\.1 200 OK' -or $text -notmatch 'Content-Type: text/event-stream') {
        throw "SSE check failed for port $Port. Output: $text"
    }
}

function Restart-Containers {
    $existing = docker ps -a --filter 'name=mcp-' --format '{{.Names}}'
    if ($existing) {
        docker stop $existing *> $null
        docker rm $existing *> $null
    }

    foreach ($server in $servers) {
        $args = @('run', '-d', '--name', $server.Name, '-p', "$($server.Port):8080", '-v', "${Workspace}:/workspace")
        $pathMappings = @("${Workspace}=/workspace")
        if (Test-Path -LiteralPath $HostDriveRoot) {
            $args += @('-v', "$($HostDriveRoot):$HostDriveMount")
            $pathMappings += "$HostDriveRoot=$HostDriveMount"
        }
        if (Test-Path -LiteralPath $VirtualizationHostPath) {
            $args += @('-v', "${VirtualizationHostPath}:/virtualization")
            $pathMappings += "${VirtualizationHostPath}=/virtualization"
        }
        $args += @('-e', "MCP_PATH_MAPPINGS=$($pathMappings -join ';')")
        if ($server.Name -eq 'mcp-mssql' -and -not [string]::IsNullOrWhiteSpace($MssqlConnectionString)) {
            $args += @('-e', "MSSQL_CONNECTION_STRING=$MssqlConnectionString")
        }
        if ($server.Name -eq 'mcp-postgresql' -and -not [string]::IsNullOrWhiteSpace($PostgresConnectionString)) {
            $args += @('-e', "POSTGRES_CONNECTION_STRING=$PostgresConnectionString")
        }
        if ($server.Name -eq 'mcp-prometheus') {
            $args += @('-e', "PROMETHEUS_BASE_URL=$mockApiBase`:19100")
        }
        if ($server.Name -eq 'mcp-gitlab') {
            $args += @('-e', "GITLAB_BASE_URL=$mockApiBase`:19101", '-e', 'GITLAB_TOKEN=smoke-token')
        }
        if ($server.Name -eq 'mcp-jira') {
            $args += @('-e', "JIRA_BASE_URL=$mockApiBase`:19102", '-e', 'JIRA_BEARER_TOKEN=smoke-token')
        }
        if ($server.Name -eq 'mcp-loki') {
            $args += @('-e', "LOKI_BASE_URL=$mockApiBase`:19103")
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
}

function New-HwpxFixture {
    $fixtureDir = Split-Path -Parent $hwpxFixturePath
    New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
    if (Test-Path -LiteralPath $hwpxFixturePath) {
        return
    }

    $tempDir = Join-Path $fixtureDir ('hwpx-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $tempDir 'Contents') | Out-Null
    @'
<?xml version="1.0" encoding="UTF-8"?>
<root xmlns:hp="http://www.hancom.co.kr/hwpml/2011/paragraph">
  <hp:p><hp:run><hp:t>HWPX smoke test 한글 텍스트</hp:t></hp:run></hp:p>
</root>
'@ | Set-Content -LiteralPath (Join-Path $tempDir 'Contents\section0.xml') -Encoding UTF8
    Compress-Archive -Path (Join-Path $tempDir '*') -DestinationPath $hwpxFixturePath -Force
    Remove-Item -LiteralPath $tempDir -Recurse -Force
}

Write-Step 'Checking Docker'
Assert-Docker
New-HwpxFixture

if (-not $SkipBuild) {
    Write-Step 'Building images'
    foreach ($server in $servers.Name) {
        docker build -t "local/$server" (Join-Path $Workspace $server)
    }
}

$mockApisStarted = $false

try {
    if (-not $SkipStart) {
        Write-Step 'Starting mock external APIs'
        Start-MockApiServers
        $mockApisStarted = $true

        Write-Step 'Restarting containers'
        Restart-Containers
        Start-Sleep -Seconds 5
    }

    $failures = @()

    foreach ($server in $servers) {
        $baseUrl = "http://localhost:$($server.Port)"
        Write-Step "Testing $($server.Name)"
        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/healthz" -TimeoutSec 20
            if ($health.status -ne 'healthy') { throw "Unexpected health status: $($health | ConvertTo-Json -Compress)" }
            Test-Sse $server.Port

            $session = New-McpSession $baseUrl
            $tools = Invoke-Mcp $baseUrl $session 2 'tools/list'
            if (-not ($tools.result.tools | Where-Object name -eq $server.Tool)) {
                throw "Tool '$($server.Tool)' not found."
            }

            if ($server.Name -eq 'mcp-postgresql' -and [string]::IsNullOrWhiteSpace($PostgresConnectionString)) {
                Write-Host "PASS $($server.Name): tools/list; skipping PostgreSQL call because POSTGRES_CONNECTION_STRING is not set"
                continue
            }

            if ($server.Name -eq 'mcp-mssql' -and [string]::IsNullOrWhiteSpace($MssqlConnectionString)) {
                Write-Host "PASS $($server.Name): tools/list; skipping SQL Server call because MSSQL_CONNECTION_STRING is not set"
                continue
            }

            $callArgs = $server.Args.Clone()
            if ($server.Name -eq 'mcp-postgresql' -and -not [string]::IsNullOrWhiteSpace($PostgresConnectionString)) {
                $callArgs.connectionString = $PostgresConnectionString
            }
            if ($server.Name -eq 'mcp-mssql' -and -not [string]::IsNullOrWhiteSpace($MssqlConnectionString)) {
                $callArgs.connectionString = $MssqlConnectionString
            }

            try {
                $call = Invoke-Mcp $baseUrl $session 3 'tools/call' @{ name = $server.Tool; arguments = $callArgs }
                if ($call.error) { throw ($call.error | ConvertTo-Json -Compress) }
                if ($call.result.isError) { throw (($call.result.content | ConvertTo-Json -Compress)) }
                Write-Host "PASS $($server.Name): $($server.Tool)"
            }
            catch {
                if ($server.Name -eq 'mcp-postgresql' -and [string]::IsNullOrWhiteSpace($PostgresConnectionString) -and $_.Exception.Message -match 'POSTGRES_CONNECTION_STRING|connectionString') {
                    Write-Host "PASS $($server.Name): expected missing PostgreSQL connection string"
                }
                elseif ($server.Name -eq 'mcp-mssql' -and [string]::IsNullOrWhiteSpace($MssqlConnectionString) -and $_.Exception.Message -match 'MSSQL_CONNECTION_STRING|connectionString') {
                    Write-Host "PASS $($server.Name): expected missing SQL Server connection string"
                }
                else {
                    throw
                }
            }

            foreach ($extra in @($server.ExtraCalls)) {
                if ($null -eq $extra) { continue }
                if ($extra.ContainsKey('PathMustExist') -and -not (Test-Path -LiteralPath $extra.PathMustExist)) {
                    $label = if ($extra.ContainsKey('Label')) { $extra.Label } else { $extra.Tool }
                    Write-Host "SKIP $($server.Name): $label because '$($extra.PathMustExist)' does not exist"
                    continue
                }
                $extraCall = Invoke-Mcp $baseUrl $session 4 'tools/call' @{ name = $extra.Tool; arguments = $extra.Args }
                if ($extraCall.error) { throw ($extraCall.error | ConvertTo-Json -Compress) }
                if ($extraCall.result.isError) { throw (($extraCall.result.content | ConvertTo-Json -Compress)) }
                $label = if ($extra.ContainsKey('Label')) { $extra.Label } else { $extra.Tool }
                Write-Host "PASS $($server.Name): $label"
            }
        }
        catch {
            $failures += "$($server.Name): $($_.Exception.Message)"
            Write-Host "FAIL $($server.Name): $($_.Exception.Message)"
        }
    }

    if ($failures.Count -gt 0) {
        Write-Host ""
        Write-Host "Failures:"
        $failures | ForEach-Object { Write-Host "- $_" }
        exit 1
    }

    Write-Host ""
    Write-Host "All MCP smoke tests passed."
}
finally {
    if ($mockApisStarted) {
        Stop-MockApiServers
    }
}
