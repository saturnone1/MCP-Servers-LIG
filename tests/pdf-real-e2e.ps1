param(
    [Parameter(Mandatory = $true)][string]$PdfPath,
    [Parameter(Mandatory = $true)][string]$DataDirectory,
    [string]$Profile = 'balanced-ko',
    [string]$ChunkProfile = 'rag-default',
    [string]$DoclingUrl = 'http://127.0.0.1:5001',
    [Parameter(Mandatory = $true)][string]$PdfRenderCommand,
    [string]$ServerExecutable = '',
    [int]$Port = 42199,
    [int]$TimeoutMinutes = 60,
    [switch]$SkipBuild,
    [switch]$ForceIngest,
    [switch]$UseOriginalSource,
    [switch]$CancellationCheck,
    [switch]$DestructiveManagementChecks
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$PdfPath = (Resolve-Path -LiteralPath $PdfPath).Path
$DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
. (Join-Path $PSScriptRoot 'mcp-http.ps1')
$process = $null
$stdoutTask = $null
$stderrTask = $null
$session = $null
$serverLog = Join-Path $DataDirectory 'mcp-pdf-server.log'

function ConvertFrom-McpResponse([string]$Content) {
    $line = $Content -split "`r?`n" | Where-Object { $_ -like 'data: *' } | Select-Object -First 1
    if ($line) { return $line.Substring(6) | ConvertFrom-Json }
    return $Content | ConvertFrom-Json
}

function Invoke-PdfTool([int]$Id, [string]$Name, [hashtable]$Arguments, [switch]$AllowError) {
    $body = @{ jsonrpc = '2.0'; id = $Id; method = 'tools/call'; params = @{ name = $Name; arguments = $Arguments } } | ConvertTo-Json -Depth 30
    $response = Invoke-McpHttpPost -Uri "http://127.0.0.1:$Port/mcp" -Body $body -SessionId $script:session
    $payload = ConvertFrom-McpResponse $response.Content
    if (-not $AllowError -and ($payload.error -or $payload.result.isError)) {
        throw "$Name failed: $($payload | ConvertTo-Json -Depth 20 -Compress)"
    }
    if ($payload.error -or $payload.result.isError) { return $payload }
    $text = [string]($payload.result.content | Where-Object type -eq 'text' | Select-Object -First 1).text
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

function ConvertTo-FlatArray($Value) {
    $items = @()
    foreach ($item in $Value) { $items += $item }
    return $items
}

function Wait-PdfJob([string]$JobId, [int]$FirstId) {
    $deadline = [DateTimeOffset]::Now.AddMinutes($TimeoutMinutes)
    $counter = 0
    $lastState = $null
    do {
        $job = Invoke-PdfTool ($FirstId + $counter) 'get_pdf_job_status' @{ jobId = $JobId }
        $state = "$($job.status)|$($job.currentStage)|$($job.progress)|$($job.processedPages)|$($job.totalPages)|$($job.chunksCreated)"
        if ($state -ne $lastState) {
            Write-Host ("job={0} status={1} stage={2} progress={3}% pages={4}/{5} chunks={6}" -f $JobId, $job.status, $job.currentStage, $job.progress, $job.processedPages, $job.totalPages, $job.chunksCreated)
            $lastState = $state
        }
        if ($job.status -in @('Completed', 'Partial', 'Failed', 'Canceled')) { return $job }
        if ([DateTimeOffset]::Now -ge $deadline) { throw "Job timed out after $TimeoutMinutes minutes: $JobId" }
        Start-Sleep -Seconds 3
        $counter++
    } while ($true)
}

try {
    if (-not $SkipBuild) { dotnet build (Join-Path $repo 'mcp-pdf\src\McpPdf.csproj') -c Release }
    New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null
    if ($UseOriginalSource) {
        $workingPdf = $PdfPath
    }
    else {
        $inputDirectory = Join-Path $DataDirectory 'input'
        New-Item -ItemType Directory -Force -Path $inputDirectory | Out-Null
        $workingPdf = Join-Path $inputDirectory 'siso-real-test.pdf'
        Copy-Item -LiteralPath $PdfPath -Destination $workingPdf -Force
    }

    if ([string]::IsNullOrWhiteSpace($ServerExecutable)) {
        $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $start.WorkingDirectory = $repo
        $start.Arguments = 'run --project "mcp-pdf/src/McpPdf.csproj" -c Release --no-build'
    }
    else {
        $ServerExecutable = (Resolve-Path -LiteralPath $ServerExecutable).Path
        $start = [Diagnostics.ProcessStartInfo]::new($ServerExecutable)
        $start.WorkingDirectory = Split-Path -Parent $ServerExecutable
    }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$Port"
    $start.Environment['PDF_DATA_DIR'] = $DataDirectory
    $start.Environment['MCP_ALLOWED_DIRS'] = '*'
    $start.Environment['DOCLING_MODE'] = 'remote'
    $start.Environment['DOCLING_SERVICE_URL'] = $DoclingUrl
    $start.Environment['DOCLING_USE_ASYNC'] = 'true'
    $start.Environment['DOCLING_POLL_INTERVAL_SECONDS'] = '2'
    $start.Environment['PDF_MAX_CONCURRENT_JOBS'] = '1'
    $start.Environment['PDF_RENDER_COMMAND'] = $PdfRenderCommand
    $process = [Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $health = $null
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try { $health = Invoke-RestMethod "http://127.0.0.1:$Port/healthz" -TimeoutSec 2; if ($health.status -eq 'healthy') { break } }
        catch { if ($process.HasExited) { throw "mcp-pdf exited with code $($process.ExitCode)" }; Start-Sleep -Milliseconds 250 }
    }
    if ($health.status -ne 'healthy') { throw 'mcp-pdf health check timed out' }

    $initialize = @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'pdf-real-e2e'; version = '1.0' } } } | ConvertTo-Json -Depth 10
    $response = Invoke-McpHttpPost -Uri "http://127.0.0.1:$Port/mcp" -Body $initialize
    $script:session = [string]$response.Headers['Mcp-Session-Id']
    if ([string]::IsNullOrWhiteSpace($script:session)) { throw 'MCP session ID was not returned' }
    Invoke-McpHttpPost -Uri "http://127.0.0.1:$Port/mcp" -Body (@{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } | ConvertTo-Json) -SessionId $script:session | Out-Null

    $config = Invoke-PdfTool 2 'config' @{}
    $ingest = Invoke-PdfTool 3 'start_pdf_ingest' @{ source = $workingPdf; profile = $Profile; chunkProfile = $ChunkProfile; force = [bool]$ForceIngest; generateEmbeddings = $false }
    $job = Wait-PdfJob $ingest.jobId 100
    $events = @(ConvertTo-FlatArray (Invoke-PdfTool 10000 'get_pdf_job_events' @{ jobId = $ingest.jobId; limit = 5000 }))
    if ($job.status -notin @('Completed', 'Partial')) { throw "Ingest failed: $($job | ConvertTo-Json -Compress)" }
    if ($job.status -eq 'Partial') { Write-Warning 'Ingest completed partially; validation details follow.' }

    $document = Invoke-PdfTool 10001 'get_pdf_document' @{ documentId = $job.documentId }
    $validation = Invoke-PdfTool 10002 'validate_pdf_dataset' @{ documentId = $document.documentId }
    $warnings = @(ConvertTo-FlatArray (Invoke-PdfTool 10003 'get_pdf_processing_warnings' @{ documentId = $document.documentId }))
    $toc = @(ConvertTo-FlatArray (Invoke-PdfTool 10004 'get_pdf_toc' @{ documentId = $document.documentId }))
    $tables = @(ConvertTo-FlatArray (Invoke-PdfTool 10005 'get_pdf_tables' @{ documentId = $document.documentId }))
    $images = @(ConvertTo-FlatArray (Invoke-PdfTool 10006 'get_pdf_images' @{ documentId = $document.documentId }))
    $chunks = @(ConvertTo-FlatArray (Invoke-PdfTool 10007 'list_pdf_chunks' @{ documentId = $document.documentId; offset = 0; limit = 1000000 }))
    if ($chunks.Count -eq 0) { throw 'No chunks were created.' }

    $middlePage = [Math]::Max(1, [Math]::Ceiling($document.pageCount / 2))
    $sampleRange = "1,$middlePage,$($document.pageCount)"
    $pages = @(ConvertTo-FlatArray (Invoke-PdfTool 10008 'read_pdf_pages' @{ documentId = $document.documentId; pages = $sampleRange }))
    if ($pages.Count -ne 3) { throw "Expected 3 sampled pages, received $($pages.Count)." }
    $render = Invoke-PdfTool 10009 'render_pdf_pages' @{ documentId = $document.documentId; pages = $sampleRange; dpi = 120 }
    foreach ($path in @($render.images)) { if (-not (Test-Path -LiteralPath $path)) { throw "Rendered image missing: $path" } }

    $sampleText = ($pages.text -join ' ')
    $terms = @([regex]::Matches($sampleText, '[\uAC00-\uD7A3]{3,}') | ForEach-Object Value | Group-Object | Sort-Object Count -Descending | Select-Object -ExpandProperty Name -First 3)
    if ($terms.Count -lt 3) { $terms = @([regex]::Matches($sampleText, '[A-Za-z]{5,}') | ForEach-Object Value | Group-Object | Sort-Object Count -Descending | Select-Object -ExpandProperty Name -First 3) }
    if ($terms.Count -lt 3) { throw 'Could not derive three search terms from sampled pages.' }
    $searches = @()
    for ($index = 0; $index -lt 3; $index++) {
        $hits = @(ConvertTo-FlatArray (Invoke-PdfTool (10020 + $index) 'search_pdf_content' @{ query = $terms[$index]; documentId = $document.documentId; mode = 'keyword'; limit = 5 }))
        if ($hits.Count -eq 0) { throw "No keyword hits for derived term: $($terms[$index])" }
        $searches += [pscustomobject]@{ term = $terms[$index]; hits = $hits.Count; firstChunk = $hits[0].chunkId; pages = "$($hits[0].pageStart)-$($hits[0].pageEnd)" }
    }

    $exportDirectory = Join-Path $DataDirectory 'exports'
    $jsonl = Invoke-PdfTool 10030 'export_pdf_dataset' @{ documentId = $document.documentId; format = 'jsonl'; destination = $exportDirectory }
    $parquet = Invoke-PdfTool 10031 'export_pdf_dataset' @{ documentId = $document.documentId; format = 'parquet'; destination = $exportDirectory }
    if ((Get-Content -LiteralPath $jsonl.path).Count -ne $chunks.Count) { throw 'JSONL line count does not match chunk count.' }
    if (-not (Test-Path -LiteralPath $parquet.path)) { throw 'Parquet export is missing.' }
    Invoke-PdfTool 10032 'save_pdf_dataset' @{ documentId = $document.documentId; provider = 'sqlite' } | Out-Null
    $storage = @(ConvertTo-FlatArray (Invoke-PdfTool 10033 'list_pdf_storage_operations' @{ documentId = $document.documentId }))
    if ($storage.Count -eq 0 -or $storage[0].status -ne 'completed') { throw 'SQLite storage operation was not recorded.' }

    $unchanged = @(ConvertTo-FlatArray (Invoke-PdfTool 10034 'check_pdf_changes' @{ documentId = $document.documentId }))
    if ($unchanged[0].state -ne 'unchanged') { throw 'Initial source change state is not unchanged.' }
    $changeDetection = 'unchanged'
    if (-not $UseOriginalSource) {
        [IO.File]::AppendAllText($workingPdf, "`n% mcp-pdf real e2e change marker")
        $changed = @(ConvertTo-FlatArray (Invoke-PdfTool 10035 'check_pdf_changes' @{ documentId = $document.documentId }))
        if ($changed[0].state -ne 'changed') { throw 'Source mutation was not detected.' }
        $changeDetection = $changed[0].state
        Copy-Item -LiteralPath $PdfPath -Destination $workingPdf -Force
    }

    if ($DestructiveManagementChecks) {
        $target = $chunks[[Math]::Floor($chunks.Count / 2)]
        $originalText = $target.text
        $originalEmbeddingText = $target.embeddingText
        Invoke-PdfTool 10040 'update_pdf_chunk' @{ chunkId = $target.chunkId; text = '__mcp_pdf_real_e2e_marker__'; embeddingText = '__mcp_pdf_real_e2e_marker__' } | Out-Null
        $updated = Invoke-PdfTool 10041 'get_pdf_chunk' @{ chunkId = $target.chunkId }
        if ($updated.text -ne '__mcp_pdf_real_e2e_marker__') { throw 'Chunk update was not persisted.' }
        Invoke-PdfTool 10042 'update_pdf_chunk' @{ chunkId = $target.chunkId; text = $originalText; embeddingText = $originalEmbeddingText } | Out-Null
        Invoke-PdfTool 10043 'delete_pdf_chunk' @{ chunkId = $target.chunkId } | Out-Null
        $deleted = Invoke-PdfTool 10044 'get_pdf_chunk' @{ chunkId = $target.chunkId } -AllowError
        if (-not $deleted.result.isError) { throw 'Deleted chunk is still readable.' }
        Invoke-PdfTool 10045 'rechunk_pdf' @{ documentId = $document.documentId; chunkProfile = $ChunkProfile } | Out-Null
        $restoredChunks = @(ConvertTo-FlatArray (Invoke-PdfTool 10046 'list_pdf_chunks' @{ documentId = $document.documentId; offset = 0; limit = 1000000 }))
        if ($restoredChunks.Count -ne $chunks.Count) { throw "Rechunk count mismatch: before=$($chunks.Count), after=$($restoredChunks.Count)." }
    }

    $duplicate = Invoke-PdfTool 10050 'start_pdf_ingest' @{ source = $workingPdf; profile = $Profile; chunkProfile = $ChunkProfile; force = $false; generateEmbeddings = $false }
    $duplicateJob = Wait-PdfJob $duplicate.jobId 10100
    if ($duplicateJob.status -ne 'Completed' -or $duplicateJob.currentStage -ne 'deduplicated' -or $duplicateJob.documentId -ne $document.documentId) { throw 'Duplicate ingest did not reuse the document.' }

    $cancellationStatus = 'not-requested'
    if ($CancellationCheck) {
        $cancelIngest = Invoke-PdfTool 11000 'start_pdf_ingest' @{ source = $workingPdf; profile = $Profile; chunkProfile = $ChunkProfile; force = $true; generateEmbeddings = $false }
        Invoke-PdfTool 11001 'cancel_pdf_job' @{ jobId = $cancelIngest.jobId } | Out-Null
        $canceledJob = Wait-PdfJob $cancelIngest.jobId 11100
        if ($canceledJob.status -ne 'Canceled') { throw "Cancellation did not reach Canceled: $($canceledJob.status)" }
        $cancellationStatus = $canceledJob.status
    }

    [pscustomobject]@{
        status = $job.status; profile = $Profile; documentId = $document.documentId; title = $document.title; pages = $document.pageCount
        chunks = $chunks.Count; tables = $tables.Count; images = $images.Count; tocEntries = $toc.Count; warnings = $warnings.Count
        emptyPages = $validation.emptyPages; oversizedChunks = $validation.oversizedChunks; duplicateRatio = $validation.duplicateRatio
        ocrPages = $validation.ocrPages; processingEvents = $events.Count; sampledPages = $sampleRange; renderedImages = @($render.images).Count
        searches = $searches; jsonl = $jsonl; parquet = $parquet; storageOperation = $storage[0].status
        duplicateStage = $duplicateJob.currentStage; changeDetection = $changeDetection; cancellation = $cancellationStatus; dataDirectory = $DataDirectory; serverLog = $serverLog
    } | ConvertTo-Json -Depth 20
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; $process.WaitForExit() }
    if ($stdoutTask -and $stderrTask) {
        $combinedLog = $stdoutTask.GetAwaiter().GetResult() + [Environment]::NewLine + $stderrTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($serverLog, $combinedLog)
    }
}
