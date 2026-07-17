param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$MssqlImage = 'mcr.microsoft.com/mssql/server:2022-latest',
    [string]$MssqlContainer = 'smoke-mssql',
    [int]$MssqlPort = 11433,
    [string]$SaPassword = 'Sm0keSql!23456',
    [switch]$SkipImagePull
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message"
}

function Remove-ContainerIfExists([string]$Name) {
    $existing = docker ps -a --filter "name=^/$Name$" --format '{{.Names}}'
    if ($existing -eq $Name) {
        docker rm -f $Name *> $null
    }
}

function Invoke-Sqlcmd([string]$Name, [string]$Password, [string]$Sql) {
    $escaped = $Sql.Replace("'", "'\''")
    $command = @"
if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P '$Password' -Q '$escaped'
elif [ -x /opt/mssql-tools/bin/sqlcmd ]; then
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P '$Password' -Q '$escaped'
else
  echo 'sqlcmd not found in SQL Server image' >&2
  exit 127
fi
"@
    docker exec $Name bash -lc $command
}

function Wait-Mssql([string]$Name, [string]$Password) {
    for ($i = 0; $i -lt 120; $i++) {
        Invoke-Sqlcmd $Name $Password 'select 1' *> $null
        if ($LASTEXITCODE -eq 0) {
            return
        }
        Start-Sleep -Seconds 2
    }
    docker logs --tail 80 $Name
    throw "SQL Server fixture did not become ready."
}

Write-Step 'Checking Docker'
docker info *> $null

if (-not $SkipImagePull) {
    Write-Step "Ensuring $MssqlImage"
    docker image inspect $MssqlImage *> $null
    if ($LASTEXITCODE -ne 0) {
        docker pull $MssqlImage
    }
}

Write-Step 'Starting disposable SQL Server fixture'
Remove-ContainerIfExists $MssqlContainer
docker run -d `
    --name $MssqlContainer `
    -p "127.0.0.1:${MssqlPort}:1433" `
    -e ACCEPT_EULA=Y `
    -e MSSQL_SA_PASSWORD=$SaPassword `
    -e MSSQL_PID=Developer `
    $MssqlImage | Out-Null

try {
    Wait-Mssql $MssqlContainer $SaPassword

    Write-Step 'Creating SQL Server smoke table'
    Invoke-Sqlcmd $MssqlContainer $SaPassword "if db_id('mcp_smoke') is null create database mcp_smoke;" | Out-Null
    Invoke-Sqlcmd $MssqlContainer $SaPassword "use mcp_smoke; if object_id('dbo.mcp_smoke', 'U') is null create table dbo.mcp_smoke(id int primary key, name nvarchar(100)); merge dbo.mcp_smoke as target using (select 1 as id, N'ok' as name) as source on target.id = source.id when matched then update set name = source.name when not matched then insert (id, name) values (source.id, source.name);" | Out-Null

    $connectionString = "Server=host.docker.internal,$MssqlPort;User Id=sa;Password=$SaPassword;Database=mcp_smoke;Encrypt=False;TrustServerCertificate=True"
    Write-Step 'Running MCP smoke with SQL Server fixture'
    & (Join-Path $PSScriptRoot 'mcp-smoke.ps1') -Workspace $Workspace -SkipBuild -MssqlConnectionString $connectionString
}
finally {
    Write-Step 'Stopping disposable SQL Server fixture'
    Remove-ContainerIfExists $MssqlContainer
}
