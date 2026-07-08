param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$servers = @(
    @{ Name = 'mcp-matlab'; ProcessName = 'McpMatlab'; Port = 42195; Script = 'mcp-matlab\scripts\run-dev.ps1'; ExpectedTools = @('config', 'official_mcp_tools_list', 'official_mcp_tool_call', 'simulink_find_system', 'simulink_simulate'); ExtraCalls = @(
        @{ Tool = 'official_mcp_tools_list'; Args = @{} },
        @{ Tool = 'official_mcp_tool_call'; Args = @{ name = 'mock_echo'; arguments = @{ text = 'bridge-ok' } } }
    ) },
    @{ Name = 'mcp-autocad'; ProcessName = 'McpAutoCad'; Port = 42196; Script = 'mcp-autocad\scripts\run-dev.ps1'; ExpectedTools = @('config', 'list_blocks', 'list_texts', 'list_dimensions', 'export_drawing') },
    @{ Name = 'mcp-solidworks'; ProcessName = 'McpSolidWorks'; Port = 42197; Script = 'mcp-solidworks\scripts\run-dev.ps1'; ExpectedTools = @('config', 'list_configurations', 'list_custom_properties', 'export_step', 'close_active_document') }
)

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message"
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

function New-McpSession([string]$BaseUrl) {
    $headers = @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
    }
    $body = New-McpBody 1 'initialize' @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'desktop-host-smoke'; version = '1.0' }
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

function Invoke-Mcp([string]$BaseUrl, [string]$SessionId, [int]$Id, [string]$Method, [hashtable]$Params = @{}) {
    $headers = @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
        'Mcp-Session-Id' = $SessionId
    }
    $response = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body (New-McpBody $Id $Method $Params) -TimeoutSec 60
    return ConvertFrom-SseJson $response.Content
}

function Wait-Health([int]$Port, [System.Diagnostics.Process]$Process) {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/healthz" -TimeoutSec 2
            if ($health.status -eq 'healthy') { return }
        }
        catch {
            if ($Process.HasExited) {
                throw "Process exited early with code $($Process.ExitCode)."
            }
            Start-Sleep -Milliseconds 500
        }
    }
    throw "Server on port $Port did not become healthy."
}

function Stop-DesktopServerProcesses {
    foreach ($server in $servers) {
        Get-Process -Name $server.ProcessName -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -like "$workspace*" } |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

if (-not $SkipBuild) {
    Write-Step 'Building desktop host MCP projects'
    dotnet build (Join-Path $workspace 'mcp-matlab\src\McpMatlab.csproj')
    dotnet build (Join-Path $workspace 'mcp-autocad\src\McpAutoCad.csproj')
    dotnet build (Join-Path $workspace 'mcp-solidworks\src\McpSolidWorks.csproj')
}

$processes = @()

try {
    Stop-DesktopServerProcesses

    foreach ($server in $servers) {
        Write-Step "Starting $($server.Name)"
        $script = Join-Path $workspace $server.Script
        if ($server.Name -eq 'mcp-matlab') {
            [Environment]::SetEnvironmentVariable('MATLAB_MCP_CORE_SERVER_PATH', (Join-Path $workspace 'tests\mock-stdio-mcp.ps1'), 'Process')
        }
        $process = Start-Process -FilePath powershell -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $script,
            '-Port',
            $server.Port
        ) -WindowStyle Hidden -PassThru
        $processes += $process
        Wait-Health $server.Port $process

        $baseUrl = "http://127.0.0.1:$($server.Port)"
        $session = New-McpSession $baseUrl
        $tools = Invoke-Mcp $baseUrl $session 2 'tools/list'
        foreach ($expectedTool in $server.ExpectedTools) {
            if (-not ($tools.result.tools | Where-Object name -eq $expectedTool)) {
                throw "$($server.Name) did not expose $expectedTool tool."
            }
        }
        $config = Invoke-Mcp $baseUrl $session 3 'tools/call' @{ name = 'config'; arguments = @{} }
        if ($config.error -or $config.result.isError) {
            throw "$($server.Name) config call failed."
        }
        foreach ($extra in @($server.ExtraCalls)) {
            if ($null -eq $extra) { continue }
            $extraCall = Invoke-Mcp $baseUrl $session 4 'tools/call' @{ name = $extra.Tool; arguments = $extra.Args }
            if ($extraCall.error -or $extraCall.result.isError) {
                throw "$($server.Name) $($extra.Tool) call failed."
            }
        }
        Write-Host "PASS $($server.Name): healthz + tools/list + config"
    }

    Write-Host ""
    Write-Host "Desktop host MCP smoke tests passed."
}
finally {
    foreach ($process in $processes) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Stop-DesktopServerProcesses
}
