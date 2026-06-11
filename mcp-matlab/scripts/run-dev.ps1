param(
    [string]$EnvFile = (Join-Path $PSScriptRoot '..\config\matlab.env'),
    [int]$Port = 8095
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
if ([string]::IsNullOrWhiteSpace($env:MATLAB_MCP_CORE_SERVER_PATH)) {
    $officialDir = Join-Path $PSScriptRoot '..\vendor\official'
    if (Test-Path -LiteralPath $officialDir) {
        $candidate = Get-ChildItem -LiteralPath $officialDir -File |
            Where-Object { $_.Name -like 'matlab-mcp*-windows-x64.exe' -or $_.Name -like 'matlab-mcp*-win64.exe' -or $_.Name -like 'matlab-mcp*.exe' } |
            Select-Object -First 1
        if ($candidate) {
            $env:MATLAB_MCP_CORE_SERVER_PATH = $candidate.FullName
        }
    }
}

Write-Host "mcp-matlab dev server"
Write-Host "HTTP:   $env:ASPNETCORE_URLS/mcp"
Write-Host "SSE:    $($env:ASPNETCORE_URLS)/sse"
Write-Host "Health: $($env:ASPNETCORE_URLS)/healthz"
if (-not [string]::IsNullOrWhiteSpace($env:MATLAB_MCP_CORE_SERVER_PATH)) {
    Write-Host "Official MATLAB MCP: $env:MATLAB_MCP_CORE_SERVER_PATH"
}

dotnet run --project (Join-Path $PSScriptRoot '..\src\McpMatlab.csproj')
