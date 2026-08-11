param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'mcp-matlab\src\McpMatlab.csproj'
$serverDll = Join-Path $repo 'mcp-matlab\src\bin\Release\net10.0\McpMatlab.dll'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("lig-ai-mcp-matlab-reuse-" + [guid]::NewGuid().ToString('N'))
$fakeServer = Join-Path $testRoot 'fake-matlab-mcp.ps1'
$startsFile = Join-Path $testRoot 'starts.txt'
$port = 42995
$process = $null

. (Join-Path $PSScriptRoot 'mcp-http.ps1')

function ConvertFrom-TestMcpResponse([string]$Content) {
    $dataLine = $Content -split "`r?`n" | Where-Object { $_ -like 'data: *' } | Select-Object -First 1
    return (($dataLine ? $dataLine.Substring(6) : $Content) | ConvertFrom-Json)
}

function New-TestMcpBody([int]$Id, [string]$Method, [hashtable]$Params) {
    return @{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 20 -Compress
}

function New-TestMcpSession([string]$BaseUrl) {
    $init = Invoke-McpHttpPost -Uri "$BaseUrl/mcp" -Body (New-TestMcpBody 1 'initialize' @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'matlab-reuse-smoke'; version = '1.0' }
    })
    $sessionId = [string]$init.Headers['Mcp-Session-Id']
    if ([string]::IsNullOrWhiteSpace($sessionId)) { throw 'MCP initialize did not return a session id.' }
    $notification = @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } | ConvertTo-Json -Compress
    Invoke-McpHttpPost -Uri "$BaseUrl/mcp" -Body $notification -SessionId $sessionId | Out-Null
    return $sessionId
}

function Invoke-TestMcpTool([string]$BaseUrl, [string]$SessionId, [int]$Id) {
    $response = Invoke-McpHttpPost -Uri "$BaseUrl/mcp" -Body (New-TestMcpBody $Id 'tools/call' @{
        name = 'official_mcp_tools_list'
        arguments = @{ timeoutMs = 10000 }
    }) -SessionId $SessionId
    return ConvertFrom-TestMcpResponse $response.Content
}

try {
    if (-not $SkipBuild) {
        dotnet build $project -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "MATLAB MCP build failed with exit code $LASTEXITCODE." }
    }

    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    @'
param([string]$StartsFile)
Add-Content -LiteralPath $StartsFile -Value $PID
while (($line = [Console]::In.ReadLine()) -ne $null) {
    try { $request = $line | ConvertFrom-Json } catch { continue }
    if ($null -eq $request.id) { continue }
    if ($request.method -eq 'initialize') {
        $result = @{ protocolVersion = '2025-06-18'; capabilities = @{}; serverInfo = @{ name = 'fake-matlab'; version = '1.0' } }
    }
    elseif ($request.method -eq 'tools/list') {
        $result = @{ tools = @() }
    }
    else {
        $result = @{ ok = $true; method = $request.method }
    }
    [Console]::Out.WriteLine((@{ jsonrpc = '2.0'; id = $request.id; result = $result } | ConvertTo-Json -Compress -Depth 30))
    [Console]::Out.Flush()
}
'@ | Set-Content -LiteralPath $fakeServer -Encoding UTF8

    $startInfo = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -ErrorAction Stop).Source)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add($serverDll)
    $startInfo.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$port"
    $startInfo.Environment['MATLAB_MCP_CORE_SERVER_PATH'] = $fakeServer
    $startInfo.Environment['MATLAB_MCP_CORE_SERVER_ARGS'] = $startsFile
    $startInfo.Environment['POWERSHELL_EXE_PATH'] = (Get-Command powershell.exe -ErrorAction Stop).Source
    $process = [Diagnostics.Process]::Start($startInfo)

    $healthy = $false
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/healthz" -TimeoutSec 1
            if ($health.server -eq 'mcp-matlab') { $healthy = $true; break }
        }
        catch {
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
    }
    if (-not $healthy) {
        throw "MATLAB MCP did not become healthy. stderr: $($process.StandardError.ReadToEnd())"
    }

    $baseUrl = "http://127.0.0.1:$port"
    $session = New-TestMcpSession $baseUrl
    $first = Invoke-TestMcpTool $baseUrl $session 2
    $second = Invoke-TestMcpTool $baseUrl $session 3
    if ($first.error -or $second.error) { throw 'Official MATLAB MCP bridge call returned an MCP error.' }

    $starts = @(Get-Content -LiteralPath $startsFile -ErrorAction Stop)
    if ($starts.Count -ne 1) {
        throw "Official MATLAB MCP process was started $($starts.Count) times for two calls; expected one reused process."
    }

    Write-Host 'Official MATLAB MCP stdio process reuse smoke passed.' -ForegroundColor Green
}
finally {
    if ($process) {
        try { if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit(5000) | Out-Null } } catch {}
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
