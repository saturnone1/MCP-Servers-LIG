param([int]$Port = 42199)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpPdf.csproj'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
if ([string]::IsNullOrWhiteSpace($env:MCP_ALLOWED_DIRS)) { $env:MCP_ALLOWED_DIRS = '*' }
dotnet run --project $project
