param(
    [int]$Port = 8094,
    [string]$Project = (Join-Path $PSScriptRoot '..\mcp-rhapsody\src\McpRhapsody.csproj'),
    [string]$RhapsodyProjectPath = '',
    [switch]$RunWriteSmoke
)

$ErrorActionPreference = 'Stop'

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
    $response = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body (New-McpBody $Id $Method $Params) -TimeoutSec 120
    return ConvertFrom-SseJson $response.Content
}

function Invoke-McpTool([string]$BaseUrl, [string]$SessionId, [int]$Id, [string]$Name, [hashtable]$Arguments = @{}) {
    $result = Invoke-Mcp $BaseUrl $SessionId $Id 'tools/call' @{ name = $Name; arguments = $Arguments }
    if ($result.error) {
        throw "$Name failed: $($result.error | ConvertTo-Json -Compress -Depth 20)"
    }
    if ($result.result.isError) {
        throw "$Name failed: $($result.result.content | ConvertTo-Json -Compress -Depth 20)"
    }
    return $result
}

$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$process = Start-Process dotnet -ArgumentList @('run', '--project', $Project) -PassThru -WindowStyle Hidden

try {
    $baseUrl = "http://127.0.0.1:$Port"
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/healthz" -TimeoutSec 2
            if ($health.status -eq 'healthy') {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        throw 'mcp-rhapsody did not become healthy.'
    }

    $headers = @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
    }
    $init = Invoke-WebRequest -Uri "$baseUrl/mcp" -Method Post -Headers $headers -Body (New-McpBody 1 'initialize' @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'rhapsody-smoke'; version = '1.0' }
    }) -TimeoutSec 20
    $sessionId = [string]$init.Headers['Mcp-Session-Id']
    if ([string]::IsNullOrWhiteSpace($sessionId)) {
        throw 'No Mcp-Session-Id returned.'
    }

    $initialized = @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } | ConvertTo-Json -Depth 10
    Invoke-WebRequest -Uri "$baseUrl/mcp" -Method Post -Headers @{
        Accept = 'application/json, text/event-stream'
        'Content-Type' = 'application/json'
        'Mcp-Session-Id' = $sessionId
    } -Body $initialized -TimeoutSec 20 | Out-Null

    $toolsJson = Invoke-Mcp $baseUrl $sessionId 2 'tools/list'
    $toolNames = @($toolsJson.result.tools | ForEach-Object name)
    foreach ($expectedTool in @('config', 'open_project', 'current_project', 'list_classes', 'create_class', 'save_project')) {
        if ($toolNames -notcontains $expectedTool) {
            throw "Expected Rhapsody tool was not registered: $expectedTool"
        }
    }

    Invoke-McpTool $baseUrl $sessionId 3 'config' | Out-Null
    Write-Host 'PASS mcp-rhapsody: healthz + config'

    if (-not [string]::IsNullOrWhiteSpace($RhapsodyProjectPath)) {
        if (-not (Test-Path -LiteralPath $RhapsodyProjectPath)) {
            throw "Rhapsody project path does not exist: $RhapsodyProjectPath"
        }

        Invoke-McpTool $baseUrl $sessionId 4 'inspect_project_file' @{ path = $RhapsodyProjectPath; maxBytes = 262144 } | Out-Null
        Invoke-McpTool $baseUrl $sessionId 5 'open_project' @{ path = $RhapsodyProjectPath } | Out-Null
        Invoke-McpTool $baseUrl $sessionId 6 'current_project' | Out-Null
        Invoke-McpTool $baseUrl $sessionId 7 'list_packages' @{ limit = 50 } | Out-Null
        Invoke-McpTool $baseUrl $sessionId 8 'list_classes' @{ limit = 50 } | Out-Null
        Invoke-McpTool $baseUrl $sessionId 9 'list_interfaces' @{ limit = 50 } | Out-Null
        Invoke-McpTool $baseUrl $sessionId 10 'list_statecharts' @{ limit = 50 } | Out-Null
        Write-Host 'PASS mcp-rhapsody: COM read smoke'

        if ($RunWriteSmoke) {
            $suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            $packageName = "McpSmoke_$suffix"
            $className = "McpSmokeClass_$suffix"
            Invoke-McpTool $baseUrl $sessionId 11 'create_package' @{ parentNameOrPath = '__active_project__'; name = $packageName } | Out-Null
            Invoke-McpTool $baseUrl $sessionId 12 'create_class' @{ packageNameOrPath = $packageName; name = $className } | Out-Null
            Invoke-McpTool $baseUrl $sessionId 13 'save_project' | Out-Null
            Write-Host 'PASS mcp-rhapsody: COM write smoke'
        }
    }
    else {
        Write-Host 'SKIP mcp-rhapsody: COM smoke requires -RhapsodyProjectPath'
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
    Remove-Item Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
}
