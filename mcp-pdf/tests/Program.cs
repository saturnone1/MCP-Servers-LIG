using System.Text.Json;

var temp = Path.Combine(Path.GetTempPath(), "mcp-pdf-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var raw = """
    {
      "name":"한국어 시험 문서",
      "pages":{"1":{"size":{"width":100,"height":100}},"2":{"size":{"width":100,"height":100}}},
      "body":{"children":[{"$ref":"#/texts/0"},{"$ref":"#/texts/2"},{"$ref":"#/texts/1"},{"$ref":"#/tables/0"},{"$ref":"#/pictures/0"}]},
      "texts":[
        {"label":"section_header","text":"개요","level":1,"prov":[{"page_no":1,"confidence":0.99}]},
        {"label":"text","text":"첫 페이지에는 검색 가능한 핵심 용어 알파가 포함됩니다.","prov":[{"page_no":1,"confidence":0.95}]},
        {"label":"text","text":"두 번째 페이지에는 베타와 감마가 포함됩니다.","prov":[{"page_no":2,"confidence":0.91}]}
      ],
      "tables":[{"label":"table","prov":[{"page_no":2}],"data":{"table_cells":[
        {"start_row_offset_idx":0,"start_col_offset_idx":0,"text":"항목"},
        {"start_row_offset_idx":0,"start_col_offset_idx":1,"text":"값"},
        {"start_row_offset_idx":1,"start_col_offset_idx":0,"text":"A"},
        {"start_row_offset_idx":1,"start_col_offset_idx":1,"text":"10"}
      ]}}],
      "pictures":[{"label":"picture","caption":"pixel","image":{"uri":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="},"prov":[{"page_no":2}]}]
    }
    """;
    var parsed = DoclingNormalizer.Normalize(raw, "", "fallback", temp, "success", 0.1, forcedOcr: true);
    Assert(parsed.PageCount == 2, "Docling page count");
    Assert(parsed.Elements.Any(e => e.Type == "table" && e.Text.Contains("| A | 10 |")), "table normalization");
    Assert(parsed.Elements.ToList().FindIndex(e => e.Text.Contains("알파")) < parsed.Elements.ToList().FindIndex(e => e.Text.Contains("베타")), "page-major Docling reading order");
    Assert(parsed.Pages[0].Text.Contains("알파"), "page reconstruction");
    Assert(parsed.Pages.All(p => p.OcrApplied), "forced OCR metadata");
    Assert(parsed.Artifacts.Count == 1 && File.Exists(parsed.Artifacts[0].Path), "embedded image extraction");

    var now = DateTimeOffset.UtcNow;
    var bundledServerDirectory = Path.Combine(temp, "bundle", "mcp-pdf-win-x64");
    var bundledRenderer = Path.Combine(temp, "bundle", "dependencies", "poppler", "Library", "bin", "pdftoppm.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(bundledRenderer)!);
    await File.WriteAllBytesAsync(bundledRenderer, []);
    Assert(PdfSettings.ResolvePdfRenderCommand(null, bundledServerDirectory) == bundledRenderer, "bundled Poppler discovery");
    Assert(PdfSettings.ResolvePdfRenderCommand("pdftoppm", bundledServerDirectory) == bundledRenderer, "legacy renderer default upgrades to bundled Poppler");
    Assert(PdfSettings.ResolvePdfRenderCommand("D:\\custom\\pdftoppm.exe", bundledServerDirectory) == "D:\\custom\\pdftoppm.exe", "configured renderer precedence");

    var document = new DocumentRecord("doc_test", Path.Combine(temp, "sample.pdf"), "sample.pdf", "sha256", 100, parsed.Title, parsed.PageCount, 1, "completed", now, now);
    var parserProfile = new PdfProfile("balanced-ko", "auto", ["kor", "eng"], "accurate", true, true, false, false);
    var chunkProfile = new ChunkProfile("test", 12, 24, 1, true, true, true, 2, 2);
    var chunks = new PdfChunker().Chunk(document, parsed, parserProfile, chunkProfile);
    Assert(chunks.Count >= 2, "structure-aware chunks");
    Assert(chunks.Select((c, i) => c.ChunkIndex == i).All(x => x), "chunk ordering");
    Assert(chunks.Zip(chunks.Skip(1)).All(pair => pair.First.NextChunkId == pair.Second.ChunkId && pair.Second.PreviousChunkId == pair.First.ChunkId), "chunk links");

    var store = new PdfStore(Path.Combine(temp, "test.db"));
    await store.InitializeAsync();
    await store.SaveDocumentAsync(document, parsed, parserProfile.Name, chunkProfile.Name, chunks, Path.Combine(temp, "manifest.json"));
    Assert((await store.GetDocumentAsync(document.DocumentId))?.PageCount == 2, "document persistence");
    Assert((await store.ListChunksAsync(document.DocumentId, 0, 100)).Length == chunks.Count, "chunk persistence");
    var storedElements = await store.GetElementsAsync(document.DocumentId);
    var storedPages = await store.ReadPagesAsync(document.DocumentId, 1, document.PageCount);
    var storedParsed = parsed with { Elements = storedElements, Pages = storedPages };
    var recomputedChunks = new PdfChunker().Chunk(document, storedParsed, parserProfile, chunkProfile);
    Assert(storedElements.Select(e => e.ElementId).SequenceEqual(parsed.Elements.Select(e => e.ElementId)), "stored element order reproducibility");
    Assert(recomputedChunks.Count == chunks.Count, "rechunk count reproducibility");
    var found = await store.KeywordSearchAsync("알파", document.DocumentId, 10);
    Assert(found.Length > 0 && found[0].Text.Contains("알파"), "FTS keyword search");
    await store.UpdateChunkTextAsync(chunks[0].ChunkId, "수정된 알파 텍스트", "수정된 알파 텍스트", TokenCounter.Count("수정된 알파 텍스트"));
    Assert((await store.GetChunkAsync(chunks[0].ChunkId))?.Text.StartsWith("수정된") == true, "chunk update transaction");
    await store.ReplaceChunksAsync(document.DocumentId, chunks);
    Assert((await store.ListChunksAsync(document.DocumentId, 0, 100)).Length == chunks.Count, "rechunk replacement transaction");
    var exporter = new DatasetExporter();
    var jsonlPath = Path.Combine(temp, "chunks.jsonl");
    var parquetPath = Path.Combine(temp, "chunks.parquet");
    await exporter.ExportJsonlAsync(jsonlPath, chunks, CancellationToken.None);
    await exporter.ExportParquetAsync(parquetPath, chunks, CancellationToken.None);
    Assert(File.Exists(jsonlPath) && File.ReadLines(jsonlPath).Count() == chunks.Count, "JSONL export");
    Assert(File.Exists(parquetPath) && new FileInfo(parquetPath).Length > 0, "Parquet export");
    var documentV2 = document with { CurrentVersion = 2, Sha256 = "sha256-v2", UpdatedAt = DateTimeOffset.UtcNow };
    var chunksV2 = new PdfChunker().Chunk(documentV2, parsed, parserProfile, chunkProfile);
    await store.SaveDocumentAsync(documentV2, parsed, parserProfile.Name, chunkProfile.Name, chunksV2, Path.Combine(temp, "manifest-v2.json"));
    var currentChunks = await store.ListChunksAsync(document.DocumentId, 0, 100);
    Assert(currentChunks.Length == chunksV2.Count && currentChunks.All(c => c.DocumentVersion == 2), "current-version chunk isolation");
    var removed = currentChunks[2];
    await store.DeleteChunkAsync(removed.ChunkId);
    var previous = await store.GetChunkAsync(removed.PreviousChunkId!);
    var next = await store.GetChunkAsync(removed.NextChunkId!);
    Assert(previous?.NextChunkId == next?.ChunkId && next?.PreviousChunkId == previous?.ChunkId, "chunk neighbor relinking");
    await store.DeleteDocumentAsync(document.DocumentId);
    Assert(await store.GetDocumentAsync(document.DocumentId) is null, "document cascade delete");

    Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", parsed.PageCount, elements = parsed.Elements.Count, chunks = chunks.Count }));
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Assertion failed: " + name);
}
