param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\mcp-rhapsody-win-x64'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpRhapsody.csproj'

dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $Output /p:UseAppHost=false

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run.ps1') -Destination (Join-Path $Output 'run.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\config\rhapsody.env.example') -Destination (Join-Path $Output 'rhapsody.env.example') -Force
if (-not (Test-Path -LiteralPath (Join-Path $Output 'rhapsody.env'))) {
    Copy-Item -LiteralPath (Join-Path $Output 'rhapsody.env.example') -Destination (Join-Path $Output 'rhapsody.env') -Force
}

Write-Host "Published mcp-rhapsody to $Output"
Write-Host "Airgap usage: copy this folder to the Windows Rhapsody machine and run .\run.ps1"

