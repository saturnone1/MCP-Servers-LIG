param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'mcp-http.ps1')
$port = 42199
$data = Join-Path ([System.IO.Path]::GetTempPath()) ('mcp-pdf-smoke-' + [guid]::NewGuid().ToString('N'))
$process = $null
$docling = $null

function ConvertFrom-McpResponse([string]$Content) {
    $line = $Content -split "`r?`n" | Where-Object { $_ -like 'data: *' } | Select-Object -First 1
    if ($line) { return $line.Substring(6) | ConvertFrom-Json }
    return $Content | ConvertFrom-Json
}

function Invoke-PdfTool([int]$Id, [string]$Name, [hashtable]$Arguments) {
    $response = Invoke-McpHttpPost -Uri "http://127.0.0.1:$port/mcp" -Body (@{ jsonrpc = '2.0'; id = $Id; method = 'tools/call'; params = @{ name = $Name; arguments = $Arguments } } | ConvertTo-Json -Depth 20) -SessionId $script:session
    $payload = ConvertFrom-McpResponse $response.Content
    if ($payload.error -or $payload.result.isError) { throw "$Name failed: $($payload | ConvertTo-Json -Depth 10 -Compress)" }
    $text = [string]($payload.result.content | Where-Object type -eq 'text' | Select-Object -First 1).text
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

function ConvertTo-FlatArray($Value) {
    $items = @()
    foreach ($item in $Value) { $items += $item }
    return $items
}

try {
    if (-not $SkipBuild) { dotnet build (Join-Path $repo 'mcp-pdf\src\McpPdf.csproj') -c Release }
    New-Item -ItemType Directory -Path $data | Out-Null
    $fakePdf = Join-Path $data 'sample.pdf'
    [System.IO.File]::WriteAllBytes($fakePdf, [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n%%EOF"))
    $docling = Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'mock-docling.ps1'), '-Port', '42209') -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try { if ((Invoke-RestMethod 'http://127.0.0.1:42209/health' -TimeoutSec 1).status -eq 'ok') { break } }
        catch { if ($docling.HasExited) { throw 'mock Docling exited early' }; Start-Sleep -Milliseconds 100 }
    }
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Arguments = 'run --project "mcp-pdf/src/McpPdf.csproj" -c Release --no-build'
    $start.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$port"
    $start.Environment['PDF_DATA_DIR'] = $data
    $start.Environment['MCP_ALLOWED_DIRS'] = '*'
    $start.Environment['DOCLING_MODE'] = 'remote'
    $start.Environment['DOCLING_SERVICE_URL'] = 'http://127.0.0.1:42209'
    $process = [System.Diagnostics.Process]::Start($start)

    $health = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$port/healthz" -TimeoutSec 2
            if ($health.status -eq 'healthy') { break }
        }
        catch {
            if ($process.HasExited) { throw "mcp-pdf exited with code $($process.ExitCode)" }
            Start-Sleep -Milliseconds 250
        }
    }
    if ($health.status -ne 'healthy') { throw 'mcp-pdf health check timed out' }

    $initialize = @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'pdf-smoke'; version = '1.0' } } } | ConvertTo-Json -Depth 10
    $response = Invoke-McpHttpPost -Uri "http://127.0.0.1:$port/mcp" -Body $initialize
    $script:session = [string]$response.Headers['Mcp-Session-Id']
    if ([string]::IsNullOrWhiteSpace($script:session)) { throw 'MCP session ID was not returned' }
    Invoke-McpHttpPost -Uri "http://127.0.0.1:$port/mcp" -Body (@{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } | ConvertTo-Json) -SessionId $script:session | Out-Null
    $toolsResponse = Invoke-McpHttpPost -Uri "http://127.0.0.1:$port/mcp" -Body (@{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} } | ConvertTo-Json -Depth 10) -SessionId $script:session
    $tools = ConvertFrom-McpResponse $toolsResponse.Content
    $names = @($tools.result.tools.name)
    foreach ($required in @('config', 'start_pdf_ingest', 'search_pdf_content', 'read_pdf_pages', 'get_pdf_tables', 'export_pdf_dataset')) {
        if ($required -notin $names) { throw "Required tool is missing: $required" }
    }
    Invoke-PdfTool 3 'config' @{} | Out-Null
    $ingest = Invoke-PdfTool 4 'start_pdf_ingest' @{ source = $fakePdf; profile = 'balanced-ko'; chunkProfile = 'rag-default' }
    $job = $null
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $job = Invoke-PdfTool (10 + $attempt) 'get_pdf_job_status' @{ jobId = $ingest.jobId }
        if ($job.status -in @('Completed', 'Partial', 'Failed', 'Canceled')) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($job.status -ne 'Completed') { throw "ingest did not complete: $($job | ConvertTo-Json -Compress)" }
    $events = @(ConvertTo-FlatArray (Invoke-PdfTool 99 'get_pdf_job_events' @{ jobId = $ingest.jobId }))
    if ($events.Count -lt 3) { throw 'job stage events were not persisted' }
    $documents = @(ConvertTo-FlatArray (Invoke-PdfTool 100 'list_pdf_documents' @{}))
    if ($documents.Count -ne 1 -or $documents[0].pageCount -ne 2) { throw 'document metadata was not persisted' }
    $unchanged = @(ConvertTo-FlatArray (Invoke-PdfTool 105 'check_pdf_changes' @{ documentId = $documents[0].documentId }))
    if ($unchanged.Count -ne 1 -or $unchanged[0].state -ne 'unchanged') { throw 'unchanged source was not recognized' }
    [System.IO.File]::AppendAllText($fakePdf, "`n%changed")
    $changed = @(ConvertTo-FlatArray (Invoke-PdfTool 106 'check_pdf_changes' @{ documentId = $documents[0].documentId }))
    if ($changed.Count -ne 1 -or $changed[0].state -ne 'changed') { throw 'source change was not detected' }
    $search = @(ConvertTo-FlatArray (Invoke-PdfTool 101 'search_pdf_content' @{ query = 'alpha'; documentId = $documents[0].documentId; mode = 'keyword'; limit = 5 }))
    if ($search.Count -lt 1) { throw 'evidence search returned no chunks' }
    $pages = @(ConvertTo-FlatArray (Invoke-PdfTool 102 'read_pdf_pages' @{ documentId = $documents[0].documentId; pages = '1-2' }))
    if ($pages.Count -ne 2) { throw "page reading did not return both pages: $($pages | ConvertTo-Json -Depth 10 -Compress)" }
    $tables = @(ConvertTo-FlatArray (Invoke-PdfTool 103 'get_pdf_tables' @{ documentId = $documents[0].documentId }))
    if ($tables.Count -ne 1) { throw 'table extraction was not persisted' }
    $export = Invoke-PdfTool 104 'export_pdf_dataset' @{ documentId = $documents[0].documentId; format = 'jsonl' }
    if (-not (Test-Path -LiteralPath $export.path)) { throw 'JSONL export file was not created' }
    Invoke-PdfTool 107 'save_pdf_dataset' @{ documentId = $documents[0].documentId; provider = 'sqlite' } | Out-Null
    $storage = @(ConvertTo-FlatArray (Invoke-PdfTool 108 'list_pdf_storage_operations' @{ documentId = $documents[0].documentId }))
    if ($storage.Count -ne 1 -or $storage[0].status -ne 'completed') { throw 'storage operation was not recorded' }
    [pscustomobject]@{ status = 'passed'; health = $health.status; server = $health.server; tools = $names.Count; pages = $pages.Count; tables = $tables.Count; evidence = $search.Count; events = $events.Count; changeDetection = $changed[0].state; storage = $storage[0].status; job = $job.status } | ConvertTo-Json -Compress
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; $process.WaitForExit() }
    if ($docling -and -not $docling.HasExited) { Stop-Process -Id $docling.Id -Force -ErrorAction SilentlyContinue; $docling.WaitForExit() }
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedData = [System.IO.Path]::GetFullPath($data)
    if ($resolvedData.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedData -Leaf) -like 'mcp-pdf-smoke-*' -and (Test-Path -LiteralPath $resolvedData)) {
        Remove-Item -LiteralPath $resolvedData -Recurse -Force
    }
}
