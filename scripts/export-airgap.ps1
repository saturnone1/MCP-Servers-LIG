param(
    [switch]$Build,
    [string[]]$Servers = @('mcp-office', 'mcp-filesystem', 'mcp-git', 'mcp-shell', 'mcp-dotnet', 'mcp-mssql', 'mcp-hwp', 'mcp-kubernetes', 'mcp-docker', 'mcp-prometheus', 'mcp-postgresql', 'mcp-gitlab', 'mcp-jira', 'mcp-loki', 'mcp-confluence')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

foreach ($server in $Servers) {
    $image = "local/$server`:latest"
    $serverDir = Join-Path $root $server
    $airgapDir = Join-Path $serverDir 'airgap'
    $tarPath = Join-Path $airgapDir "local-$server.tar"

    if ($Build) {
        docker build -t "local/$server" $serverDir
    }

    New-Item -ItemType Directory -Force -Path $airgapDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-docker-mcp.ps1') -Destination (Join-Path $airgapDir 'run-docker-mcp.ps1') -Force
    docker image inspect $image *> $null
    docker save -o $tarPath $image

    $size = [math]::Round((Get-Item -LiteralPath $tarPath).Length / 1MB, 1)
    Write-Host "$server -> $tarPath ($size MB)"
}
