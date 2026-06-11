param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ManagerArgs
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\publish\mcp-manager-win-x64\McpManager.exe'
$dll = Join-Path $PSScriptRoot '..\src\bin\Release\net10.0\McpManager.dll'

if (Test-Path -LiteralPath $exe) {
    & $exe @ManagerArgs
}
elseif (Test-Path -LiteralPath $dll) {
    dotnet $dll @ManagerArgs
}
else {
    dotnet run --project (Join-Path $PSScriptRoot '..\src\McpManager.csproj') -- @ManagerArgs
}
