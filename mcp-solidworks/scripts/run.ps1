param(
    [string]$EnvFile = (Join-Path $PSScriptRoot 'solidworks.env'),
    [int]$Port = 42197
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

Write-Host "mcp-solidworks"
Write-Host "HTTP:   $env:ASPNETCORE_URLS/mcp"
Write-Host "SSE:    $($env:ASPNETCORE_URLS)/sse"
Write-Host "Health: $($env:ASPNETCORE_URLS)/healthz"

$exe = Join-Path $PSScriptRoot 'McpSolidWorks.exe'
$dll = Join-Path $PSScriptRoot 'McpSolidWorks.dll'
if (Test-Path -LiteralPath $exe) {
    & $exe
}
elseif (Test-Path -LiteralPath $dll) {
    dotnet $dll
}
else {
    throw "McpSolidWorks.exe or McpSolidWorks.dll not found. Run scripts\publish-win.ps1 first, or use scripts\run-dev.ps1 from the repository."
}

