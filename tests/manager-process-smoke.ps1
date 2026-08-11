param(
    [switch]$SkipBuild,
    [string]$BundleRoot = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'mcp-manager\src\McpManager.csproj'
$managerDll = Join-Path $repo 'mcp-manager\src\bin\Release\net10.0\McpManager.dll'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("lig-ai-mcp-process-smoke-" + [guid]::NewGuid().ToString('N'))
$stateRoot = Join-Path $testRoot 'state'
$configPath = Join-Path $testRoot 'servers.json'
$markerPath = Join-Path $testRoot 'args-applied.txt'
$childScriptPath = Join-Path $testRoot 'test-child.ps1'
$healthScriptPath = Join-Path $testRoot 'health-child.ps1'
$previousConfig = $env:MCP_MANAGER_CONFIG
$previousState = $env:MCP_MANAGER_STATE_DIR
$childPid = $null
$bundlePid = $null
$externalPid = $null

try {
    if (-not $SkipBuild) {
        dotnet build $project -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Manager build failed with exit code $LASTEXITCODE." }
    }
    New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
    $powershell = (Get-Command powershell.exe -ErrorAction Stop).Source
    $cmd = (Get-Command cmd.exe -ErrorAction Stop).Source
    $currentExecutable = (Get-Process -Id $PID).MainModule.FileName
    @('param([string]$Marker)', 'Set-Content -LiteralPath $Marker -Value ready', 'Start-Sleep -Seconds 120') |
        Set-Content -LiteralPath $childScriptPath -Encoding UTF8
    @(
        'param([int]$Port, [string]$ServerName)',
        '$listener = [Net.HttpListener]::new()',
        '$listener.Prefixes.Add("http://127.0.0.1:$Port/")',
        '$listener.Start()',
        'try {',
        '  while ($true) {',
        '    $context = $listener.GetContext()',
        '    $body = [Text.Encoding]::UTF8.GetBytes((@{ status = "healthy"; server = $ServerName } | ConvertTo-Json -Compress))',
        '    $context.Response.StatusCode = 200',
        '    $context.Response.ContentType = "application/json"',
        '    $context.Response.OutputStream.Write($body, 0, $body.Length)',
        '    $context.Response.Close()',
        '  }',
        '} finally { $listener.Stop() }'
    ) | Set-Content -LiteralPath $healthScriptPath -Encoding UTF8
    $servers = @(
            [ordered]@{
                name = 'pid-mismatch'
                kind = 'process'
                port = 42990
                workingDirectory = $testRoot
                executable = $cmd
                env = [ordered]@{}
            },
            [ordered]@{
                name = 'detached-process'
                kind = 'process'
                port = 42991
                workingDirectory = $testRoot
                executable = $powershell
                args = @('-NoLogo', '-NoProfile', '-File', $childScriptPath, '-Marker', $markerPath)
                env = [ordered]@{}
            },
            [ordered]@{
                name = 'identity-mismatch'
                kind = 'process'
                port = 42992
                workingDirectory = $testRoot
                executable = $currentExecutable
                env = [ordered]@{}
            },
            [ordered]@{
                name = 'external-existing'
                kind = 'process'
                port = 42994
                healthUrl = 'http://127.0.0.1:42994/healthz'
                workingDirectory = $testRoot
                executable = $cmd
                env = [ordered]@{}
            }
    )
    if (-not [string]::IsNullOrWhiteSpace($BundleRoot)) {
        $resolvedBundle = (Resolve-Path -LiteralPath $BundleRoot).Path
        $filesystemDirectory = Join-Path $resolvedBundle 'mcp-filesystem-win-x64'
        $servers += [ordered]@{
            name = 'bundle-filesystem'
            kind = 'process'
            port = 42993
            workingDirectory = $filesystemDirectory
            executable = (Join-Path $filesystemDirectory 'McpFilesystem.exe')
            env = [ordered]@{ MCP_ALLOWED_DIRS = '*' }
        }
    }
    $config = [ordered]@{ servers = $servers }
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8
    $env:MCP_MANAGER_CONFIG = $configPath
    $env:MCP_MANAGER_STATE_DIR = $stateRoot

    function Invoke-TestManager([string[]]$Arguments) {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -ErrorAction Stop).Source)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add($managerDll)
        foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        try {
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) {
                throw "Manager command failed ($($Arguments -join ' ')): $($process.StandardError.ReadToEnd())"
            }
        }
        finally {
            $process.Dispose()
        }
    }

    $mismatchPidPath = Join-Path $stateRoot 'pid-mismatch.pid'
    Set-Content -LiteralPath $mismatchPidPath -Value $PID -Encoding ASCII
    Invoke-TestManager @('stop', 'pid-mismatch')
    if (-not (Get-Process -Id $PID -ErrorAction SilentlyContinue)) {
        throw 'A stale PID entry terminated an unrelated process.'
    }
    if (Test-Path -LiteralPath $mismatchPidPath) {
        throw 'The stale PID file was not removed.'
    }

    $identityPidPath = Join-Path $stateRoot 'identity-mismatch.pid'
    @{ pid = $PID; startTimeUtcTicks = 0 } | ConvertTo-Json | Set-Content -LiteralPath $identityPidPath -Encoding UTF8
    Invoke-TestManager @('stop', 'identity-mismatch')
    if (-not (Get-Process -Id $PID -ErrorAction SilentlyContinue)) {
        throw 'A reused PID with the same executable terminated an unrelated process.'
    }
    if (Test-Path -LiteralPath $identityPidPath) {
        throw 'The mismatched process identity file was not removed.'
    }

    Invoke-TestManager @('start', 'detached-process')
    $childIdentity = Get-Content -LiteralPath (Join-Path $stateRoot 'detached-process.pid') -Raw | ConvertFrom-Json
    $childPid = [int]$childIdentity.pid
    for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $markerPath); $i++) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $markerPath)) {
        throw 'Process arguments were not passed to the child process.'
    }
    if (-not (Get-Process -Id $childPid -ErrorAction SilentlyContinue)) {
        throw 'The CLI-started child process did not survive manager exit.'
    }

    Invoke-TestManager @('stop', 'detached-process')
    if (Get-Process -Id $childPid -ErrorAction SilentlyContinue) {
        throw 'The detached child process survived an explicit stop.'
    }
    $childPid = $null

    $externalProcess = Start-Process -FilePath $powershell -ArgumentList @('-NoLogo', '-NoProfile', '-File', $healthScriptPath, '-Port', '42994', '-ServerName', 'external-existing') -PassThru -WindowStyle Hidden
    $externalPid = $externalProcess.Id
    $externalProcess.Dispose()
    $externalHealthy = $false
    for ($i = 0; $i -lt 40; $i++) {
        try {
            $health = Invoke-RestMethod -Uri 'http://127.0.0.1:42994/healthz' -TimeoutSec 1
            if ($health.server -eq 'external-existing') { $externalHealthy = $true; break }
        }
        catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $externalHealthy) { throw 'External MCP fixture did not become healthy.' }
    Invoke-TestManager @('start', 'external-existing')
    if (Test-Path -LiteralPath (Join-Path $stateRoot 'external-existing.pid')) {
        throw 'Manager launched a duplicate process even though the expected MCP server was already healthy.'
    }
    if (-not (Get-Process -Id $externalPid -ErrorAction SilentlyContinue)) {
        throw 'Manager disturbed the existing external MCP process.'
    }

    if (-not [string]::IsNullOrWhiteSpace($BundleRoot)) {
        Invoke-TestManager @('start', 'bundle-filesystem')
        $bundleIdentityPath = Join-Path $stateRoot 'bundle-filesystem.pid'
        $bundlePid = [int]((Get-Content -LiteralPath $bundleIdentityPath -Raw | ConvertFrom-Json).pid)
        $healthy = $false
        for ($i = 0; $i -lt 40; $i++) {
            try {
                $health = Invoke-RestMethod -Uri 'http://127.0.0.1:42993/healthz' -TimeoutSec 1
                if ($health.status -eq 'healthy') { $healthy = $true; break }
            }
            catch {
                if (-not (Get-Process -Id $bundlePid -ErrorAction SilentlyContinue)) { break }
                Start-Sleep -Milliseconds 250
            }
        }
        if (-not $healthy) {
            throw 'The bundled MCP server did not remain healthy after the CLI manager exited.'
        }
        Invoke-TestManager @('stop', 'bundle-filesystem')
        if (Get-Process -Id $bundlePid -ErrorAction SilentlyContinue) {
            throw 'The bundled MCP server survived an explicit manager stop.'
        }
    }

    Write-Host 'Manager process identity, arguments, and detached lifecycle smoke passed.' -ForegroundColor Green
}
finally {
    if ($childPid -and (Get-Process -Id $childPid -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $childPid -Force -ErrorAction SilentlyContinue
    }
    if ($bundlePid -and (Get-Process -Id $bundlePid -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $bundlePid -Force -ErrorAction SilentlyContinue
    }
    if ($externalPid -and (Get-Process -Id $externalPid -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $externalPid -Force -ErrorAction SilentlyContinue
    }
    $env:MCP_MANAGER_CONFIG = $previousConfig
    $env:MCP_MANAGER_STATE_DIR = $previousState
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
