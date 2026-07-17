param(
    [string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$PostgresImage = 'postgres:16-alpine',
    [string]$PostgresContainer = 'smoke-postgres',
    [int]$PostgresPort = 15432,
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

function Wait-Postgres([string]$Name) {
    for ($i = 0; $i -lt 60; $i++) {
        docker exec $Name pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) {
            return
        }
        Start-Sleep -Seconds 1
    }
    throw "PostgreSQL fixture did not become ready."
}

Write-Step 'Checking Docker'
docker info *> $null

if (-not $SkipImagePull) {
    Write-Step "Ensuring $PostgresImage"
    docker image inspect $PostgresImage *> $null
    if ($LASTEXITCODE -ne 0) {
        docker pull $PostgresImage
    }
}

Write-Step 'Starting disposable PostgreSQL fixture'
Remove-ContainerIfExists $PostgresContainer
docker run -d `
    --name $PostgresContainer `
    -p "127.0.0.1:${PostgresPort}:5432" `
    -e POSTGRES_PASSWORD=postgres `
    -e POSTGRES_DB=postgres `
    $PostgresImage | Out-Null

try {
    Wait-Postgres $PostgresContainer

    Write-Step 'Creating PostgreSQL smoke table'
    docker exec $PostgresContainer psql -U postgres -d postgres -c "create table if not exists mcp_smoke(id int primary key, name text); insert into mcp_smoke(id, name) values (1, 'ok') on conflict (id) do update set name = excluded.name;" | Out-Null

    $connectionString = "Host=host.docker.internal;Port=$PostgresPort;Username=postgres;Password=postgres;Database=postgres"
    Write-Step 'Running MCP smoke with PostgreSQL fixture'
    & (Join-Path $PSScriptRoot 'mcp-smoke.ps1') -Workspace $Workspace -SkipBuild -PostgresConnectionString $connectionString
}
finally {
    Write-Step 'Stopping disposable PostgreSQL fixture'
    Remove-ContainerIfExists $PostgresContainer
}
