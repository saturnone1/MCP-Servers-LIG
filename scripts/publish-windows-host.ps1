param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\windows-host-publish'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$servers = @(
    @{ Name = 'mcp-rhapsody'; Script = 'mcp-rhapsody\scripts\publish-win.ps1'; Folder = 'mcp-rhapsody-win-x64' },
    @{ Name = 'mcp-matlab'; Script = 'mcp-matlab\scripts\publish-win.ps1'; Folder = 'mcp-matlab-win-x64' },
    @{ Name = 'mcp-autocad'; Script = 'mcp-autocad\scripts\publish-win.ps1'; Folder = 'mcp-autocad-win-x64' },
    @{ Name = 'mcp-solidworks'; Script = 'mcp-solidworks\scripts\publish-win.ps1'; Folder = 'mcp-solidworks-win-x64' },
    @{ Name = 'mcp-pdf'; Script = 'mcp-pdf\scripts\publish-win.ps1'; Folder = 'mcp-pdf-win-x64' }
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

foreach ($server in $servers) {
    $publishScript = Join-Path $repoRoot $server.Script
    $output = Join-Path $OutputRoot $server.Folder
    Write-Host ""
    Write-Host "== Publishing $($server.Name) to $output"
    & $publishScript -Output $output -Configuration $Configuration -Runtime $Runtime -SelfContained $SelfContained -SingleFile $SingleFile
}

if ($Zip) {
    foreach ($server in $servers) {
        $folder = Join-Path $OutputRoot $server.Folder
        $zipPath = Join-Path $OutputRoot "$($server.Folder).zip"
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zipPath -Force
        Write-Host "Created $zipPath"
    }
}

Write-Host ""
Write-Host "Windows-host MCP packages are ready in $OutputRoot"
