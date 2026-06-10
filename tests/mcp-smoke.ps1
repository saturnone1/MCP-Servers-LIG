param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$HostDriveRoot = 'C:\',
    [string]$HostDriveMount = '/host/c',
    [string]$VirtualizationHostPath = 'C:\Users\taewon\Desktop\가상화',
    [string]$MssqlConnectionString = '',
    [switch]$SkipBuild,
    [switch]$SkipStart
)

$ErrorActionPreference = 'Stop'

$docPath = Join-Path $VirtualizationHostPath 'ICFICE_2024_Fullpaper_template.doc'
$hwpPath = Join-Path $VirtualizationHostPath '090717_174041909.hwp'
$hwpxFixturePath = Join-Path $PSScriptRoot 'fixtures\sample.hwpx'

$servers = @(
    @{ Name = 'mcp-office'; Port = 8080; Tool = 'extract_text'; Args = @{ path = $docPath; maxLines = 20 } },
    @{ Name = 'mcp-filesystem'; Port = 8081; Tool = 'list_directory'; Args = @{ path = $VirtualizationHostPath; limit = 20 } },
    @{ Name = 'mcp-git'; Port = 8082; Tool = 'status'; Args = @{ repositoryPath = 'C:\Users\taewon\source\repos\mcp_servers' } },
    @{ Name = 'mcp-shell'; Port = 8083; Tool = 'run_command'; Args = @{ command = 'pwd'; args = @(); workingDirectory = 'C:\Users\taewon\source\repos\mcp_servers'; timeoutMs = 10000 } },
    @{ Name = 'mcp-dotnet'; Port = 8084; Tool = 'sdk_info'; Args = @{} },
    @{ Name = 'mcp-mssql'; Port = 8085; Tool = 'execute_read_query'; Args = @{ sql = 'select 1 as ok'; maxRows = 5 } },
    @{ Name = 'mcp-hwp'; Port = 8086; Tool = 'extract_text'; Args = @{ path = $hwpPath; maxChars = 4000 }; ExtraCalls = @(
        @{ Tool = 'extract_text'; Args = @{ path = $hwpxFixturePath; maxChars = 4000 } },
        @{ Tool = 'convert'; Args = @{ path = $hwpPath; outputDirectory = '/tmp/hwp-output'; format = 'txt' } }
    ) },
    @{ Name = 'mcp-kubernetes'; Port = 8087; Tool = 'version'; Args = @{ clientOnly = $true } },
    @{ Name = 'mcp-docker'; Port = 8088; Tool = 'version'; Args = @{} },
    @{ Name = 'mcp-prometheus'; Port = 8089; Tool = 'config'; Args = @{} }
)

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message"
}

function Assert-Docker {
    docker info *> $null
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

if (-not $SkipStart) {
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

        if ($server.Name -eq 'mcp-mssql' -and [string]::IsNullOrWhiteSpace($MssqlConnectionString)) {
            Write-Host "PASS $($server.Name): tools/list; skipping SQL call because MSSQL_CONNECTION_STRING is not set"
            continue
        }

        $callArgs = $server.Args.Clone()
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
            if ($server.Name -eq 'mcp-mssql' -and [string]::IsNullOrWhiteSpace($MssqlConnectionString) -and $_.Exception.Message -match 'MSSQL_CONNECTION_STRING|connectionString') {
                Write-Host "PASS $($server.Name): expected missing SQL connection string"
            }
            else {
                throw
            }
        }

        foreach ($extra in @($server.ExtraCalls)) {
            if ($null -eq $extra) { continue }
            $extraCall = Invoke-Mcp $baseUrl $session 4 'tools/call' @{ name = $extra.Tool; arguments = $extra.Args }
            if ($extraCall.error) { throw ($extraCall.error | ConvertTo-Json -Compress) }
            if ($extraCall.result.isError) { throw (($extraCall.result.content | ConvertTo-Json -Compress)) }
            Write-Host "PASS $($server.Name): $($extra.Tool)"
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
