param(
    [string]$EnvFile = (Join-Path $PSScriptRoot 'rhapsody.env'),
    [int]$Port = 42194
)

$ErrorActionPreference = 'Stop'

function Import-EnvFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { return }
        $parts = $line.Split('=', 2)
        if ($parts.Length -eq 2) {
            [Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), 'Process')
        }
    }
}

Import-EnvFile $EnvFile
if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_URLS)) {
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
}

$exe = Join-Path $PSScriptRoot 'McpRhapsody.exe'
$dll = Join-Path $PSScriptRoot 'McpRhapsody.dll'
if (-not (Test-Path -LiteralPath $exe) -and -not (Test-Path -LiteralPath $dll)) {
    $repoExe = Join-Path $PSScriptRoot '..\src\bin\Release\net10.0\win-x64\McpRhapsody.exe'
    $repoDll = Join-Path $PSScriptRoot '..\src\bin\Release\net10.0\McpRhapsody.dll'
    if (Test-Path -LiteralPath $repoExe) { $exe = $repoExe }
    elseif (Test-Path -LiteralPath $repoDll) { $dll = $repoDll }
}
if (-not (Test-Path -LiteralPath $exe) -and -not (Test-Path -LiteralPath $dll)) {
    throw "McpRhapsody.exe or McpRhapsody.dll not found. Run scripts\publish-win.ps1 first, or use scripts\run-dev.ps1 from the repository."
}

Write-Host "mcp-rhapsody server"
Write-Host "HTTP:   $env:ASPNETCORE_URLS/mcp"
Write-Host "SSE:    $($env:ASPNETCORE_URLS)/sse"
Write-Host "Health: $($env:ASPNETCORE_URLS)/healthz"

if (Test-Path -LiteralPath $exe) {
    & $exe
}
else {
    dotnet $dll
}

