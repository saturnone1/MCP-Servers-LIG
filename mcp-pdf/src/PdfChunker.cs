using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed class PdfChunker
{
    public IReadOnlyList<RagChunk> Chunk(DocumentRecord document, ParsedDocument parsed, PdfProfile parserProfile, ChunkProfile profile)
    {
        var chunks = new List<RagChunk>();
        var candidates = new List<List<ParsedElement>>();
        var current = new List<ParsedElement>();
        var currentTokens = 0;
        string[] currentHeading = [];

        foreach (var element in parsed.Elements.Where(e => !string.IsNullOrWhiteSpace(e.Text) && e.Type is not "header" and not "footer"))
        {
            var elementTokens = TokenCounter.Count(element.Text);
            var headingChanged = profile.PreserveHeadingBoundary && current.Count > 0 && !currentHeading.SequenceEqual(element.HeadingPath);
            var boundary = profile.PreserveTableBoundary && (element.Type == "table" || current.Any(e => e.Type == "table"));
            if (current.Count > 0 && (headingChanged || boundary || currentTokens + elementTokens > profile.TargetTokens))
            {
                candidates.Add(current);
                current = [];
                currentTokens = 0;
            }
            currentHeading = element.HeadingPath;
            if (elementTokens > profile.MaxTokens)
            {
                if (current.Count > 0) { candidates.Add(current); current = []; currentTokens = 0; }
                foreach (var split in SplitElement(element, profile.MaxTokens)) candidates.Add([split]);
                continue;
            }
            current.Add(element);
            currentTokens += elementTokens;
        }
        if (current.Count > 0) candidates.Add(current);
        if (profile.MergePeers) candidates = MergeSmallPeers(candidates, profile);

        for (var index = 0; index < candidates.Count; index++)
        {
            var group = candidates[index];
            var text = string.Join("\n\n", group.Select(e => e.Text.Trim()));
            var headingPath = group.Select(e => e.HeadingPath).FirstOrDefault(h => h.Length > 0) ?? [];
            var headingText = string.Join("\n", headingPath.Where(h => !string.IsNullOrWhiteSpace(h)));
            var embeddingText = string.IsNullOrWhiteSpace(headingText) ? text : headingText + "\n\n" + text;
            var contentType = group.Select(e => e.Type).Distinct().Count() == 1 ? group[0].Type : "mixed";
            var idMaterial = $"{document.DocumentId}|{document.CurrentVersion}|{profile.Name}|{string.Join(',', group.Select(e => e.ElementId))}|{text}";
            chunks.Add(new RagChunk
            {
                SchemaVersion = "1.0", DocumentId = document.DocumentId, DocumentVersion = document.CurrentVersion,
                ChunkId = "chk_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idMaterial))).ToLowerInvariant()[..24], ChunkIndex = index,
                Text = text, EmbeddingText = embeddingText, Title = headingPath.LastOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? parsed.Title,
                HeadingPath = headingPath, ContentType = contentType, PageStart = group.Min(e => e.PageStart), PageEnd = group.Max(e => e.PageEnd),
                SourceElements = group.Select(e => e.ElementId).ToArray(), TokenCount = TokenCounter.Count(embeddingText), Language = DetectLanguage(text),
                OcrApplied = parsed.Pages.Any(p => p.OcrApplied && p.PageNumber >= group.Min(e => e.PageStart) && p.PageNumber <= group.Max(e => e.PageEnd)),
                Confidence = group.Where(e => e.Confidence.HasValue).Select(e => e.Confidence!.Value).DefaultIfEmpty().Average() is var average && average > 0 ? average : null,
                SourcePath = document.SourcePath, SourceSha256 = document.Sha256, Parser = parsed.Parser, ParserVersion = parsed.ParserVersion,
                ParserProfile = parserProfile.Name, ChunkProfile = profile.Name, CreatedAt = DateTimeOffset.UtcNow
            });
        }
        for (var index = 0; index < chunks.Count; index++)
        {
            chunks[index].PreviousChunkId = index > 0 ? chunks[index - 1].ChunkId : null;
            chunks[index].NextChunkId = index + 1 < chunks.Count ? chunks[index + 1].ChunkId : null;
            chunks[index] = chunks[index] with { EmbeddingText = Contextualize(chunks, index, profile) };
        }
        return chunks;
    }

    private static string Contextualize(List<RagChunk> chunks, int index, ChunkProfile profile)
    {
        var current = chunks[index];
        var prefix = index > 0 ? TokenCounter.TakeLast(chunks[index - 1].Text, profile.ContextBeforeTokens) : "";
        var suffix = index + 1 < chunks.Count ? TokenCounter.TakeFirst(chunks[index + 1].Text, profile.ContextAfterTokens) : "";
        var heading = string.Join(" > ", current.HeadingPath.Where(h => !string.IsNullOrWhiteSpace(h)));
        return string.Join("\n\n", new[] { heading, prefix, current.Text, suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static IEnumerable<ParsedElement> SplitElement(ParsedElement element, int maxTokens)
    {
        var paragraphs = Regex.Split(element.Text, @"(?<=[.!?。！？])\s+|\r?\n+").Where(s => !string.IsNullOrWhiteSpace(s));
        var current = new StringBuilder();
        var part = 0;
        foreach (var sentence in paragraphs)
        {
            if (current.Length > 0 && TokenCounter.Count(current + " " + sentence) > maxTokens)
            {
                yield return element with { ElementId = $"{element.ElementId}-part-{part++}", Text = current.ToString().Trim() };
                current.Clear();
            }
            if (TokenCounter.Count(sentence) > maxTokens)
            {
                foreach (var slice in TokenCounter.Slices(sentence, maxTokens))
                    yield return element with { ElementId = $"{element.ElementId}-part-{part++}", Text = slice };
            }
            else current.Append(current.Length == 0 ? sentence : " " + sentence);
        }
        if (current.Length > 0) yield return element with { ElementId = $"{element.ElementId}-part-{part}", Text = current.ToString().Trim() };
    }

    private static List<List<ParsedElement>> MergeSmallPeers(List<List<ParsedElement>> candidates, ChunkProfile profile)
    {
        var result = new List<List<ParsedElement>>();
        foreach (var candidate in candidates)
        {
            if (result.Count > 0 && TokenCounter.Count(string.Join(" ", candidate.Select(e => e.Text))) < profile.MinTokens &&
                result[^1][0].HeadingPath.SequenceEqual(candidate[0].HeadingPath) &&
                !result[^1].Any(e => e.Type == "table") && !candidate.Any(e => e.Type == "table") &&
                TokenCounter.Count(string.Join(" ", result[^1].Concat(candidate).Select(e => e.Text))) <= profile.MaxTokens)
                result[^1].AddRange(candidate);
            else result.Add(candidate);
        }
        return result;
    }

    private static string DetectLanguage(string text)
    {
        var korean = text.Count(c => c is >= '\uac00' and <= '\ud7a3');
        return korean > Math.Max(3, text.Length / 20) ? "ko" : "und";
    }
}

public static class TokenCounter
{
    private static readonly Regex Tokens = new(@"[\p{IsHangulSyllables}\p{IsCJKUnifiedIdeographs}]|[\p{L}\p{N}_]+|[^\s]", RegexOptions.Compiled);
    public static int Count(string text) => Tokens.Matches(text).Sum(match => IsWord(match.Value) ? Math.Max(1, (match.Value.Length + 3) / 4) : 1);
    public static string TakeFirst(string text, int tokens) => string.Join(" ", Tokenize(text).Take(tokens));
    public static string TakeLast(string text, int tokens) => string.Join(" ", Tokenize(text).TakeLast(tokens));
    public static IEnumerable<string> Slices(string text, int maxTokens)
    {
        var values = Tokenize(text).ToArray();
        for (var i = 0; i < values.Length; i += maxTokens) yield return string.Join(" ", values.Skip(i).Take(maxTokens));
    }
    private static IEnumerable<string> Tokenize(string text) => Tokens.Matches(text).Select(m => m.Value);
    private static bool IsWord(string value) => value.Length > 1 && value.All(c => char.IsLetterOrDigit(c) || c == '_');
}
