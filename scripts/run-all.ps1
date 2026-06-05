param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$HostDriveRoot = 'C:\',
    [string]$HostDriveMount = '/host/c',
    [string]$MssqlConnectionString = '',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$servers = @(
    @{ Name = 'mcp-office'; Port = 8080 },
    @{ Name = 'mcp-filesystem'; Port = 8081 },
    @{ Name = 'mcp-git'; Port = 8082 },
    @{ Name = 'mcp-shell'; Port = 8083 },
    @{ Name = 'mcp-dotnet'; Port = 8084 },
    @{ Name = 'mcp-mssql'; Port = 8085 },
    @{ Name = 'mcp-hwp'; Port = 8086 }
)

if ($Build) {
    foreach ($server in $servers.Name) {
        docker build -t "local/$server" (Join-Path $Workspace $server)
    }
}

$existing = docker ps -a --filter 'name=mcp-' --format '{{.Names}}'
if ($existing) {
    docker stop $existing *> $null
    docker rm $existing *> $null
}

$pathMappings = @("${Workspace}=/workspace")
$mounts = @('-v', "${Workspace}:/workspace")

if (Test-Path -LiteralPath $HostDriveRoot) {
    $mounts += @('-v', "$($HostDriveRoot):$HostDriveMount")
    $pathMappings += "$HostDriveRoot=$HostDriveMount"
}

foreach ($server in $servers) {
    $args = @('run', '-d', '--name', $server.Name, '-p', "$($server.Port):8080")
    $args += $mounts
    $args += @('-e', "MCP_PATH_MAPPINGS=$($pathMappings -join ';')")
    if ($server.Name -eq 'mcp-mssql' -and -not [string]::IsNullOrWhiteSpace($MssqlConnectionString)) {
        $args += @('-e', "MSSQL_CONNECTION_STRING=$MssqlConnectionString")
    }
    $args += "local/$($server.Name)"
    docker @args | Out-Null
}

Start-Sleep -Seconds 3
docker ps --format 'table {{.Names}}\t{{.Ports}}\t{{.Status}}' | Select-String 'mcp-'
