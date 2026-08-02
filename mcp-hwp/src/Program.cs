using ModelContextProtocol.Server;
using OpenMcdf;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<HwpTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-hwp" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class HwpTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Extract readable text from .hwp or .hwpx files.")]
    public static async Task<string> ExtractText(string path, int maxChars = 1000000)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        if (!File.Exists(fullPath))
            return $"File not found after path mapping: {fullPath}";

        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".hwpx", StringComparison.OrdinalIgnoreCase))
            return Trim(ExtractHwpxText(fullPath), maxChars);
        if (string.Equals(extension, ".hwp", StringComparison.OrdinalIgnoreCase))
            return Trim(await ExtractHwpText(fullPath), maxChars);
        throw new NotSupportedException("Only .hwp and .hwpx files are supported.");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return basic metadata for a .hwp or .hwpx file.")]
    public static object Inspect(string path)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        if (!File.Exists(fullPath))
        {
            return new
            {
                exists = false,
                requestedPath = path,
                mappedPath = fullPath,
                error = $"File not found after path mapping: {fullPath}"
            };
        }

        var info = new FileInfo(fullPath);
        return new
        {
            exists = true,
            path = fullPath,
            extension = info.Extension,
            info.Length,
            info.LastWriteTimeUtc,
            format = string.Equals(info.Extension, ".hwpx", StringComparison.OrdinalIgnoreCase) ? "HWPX zip/xml" : "HWP binary"
        };
    }

    [McpServerTool]
    [Description("Convert .hwp or .hwpx to txt, docx, pdf, or odt. Text output uses the extractor; other formats use LibreOffice.")]
    public static async Task<CommandResult> Convert(string path, string outputDirectory = "", string format = "txt", int timeoutMs = 600000)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedPath(path);
        if (!File.Exists(fullPath))
            return new CommandResult(2, "", $"File not found after path mapping: {fullPath}");

        var resolvedOutput = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "hwp-output")
            : outputDirectory;
        var output = Guard.RequireAllowedPath(resolvedOutput);
        Directory.CreateDirectory(output);
        var normalizedFormat = Guard.RequireSupportedOutput(format);

        if (string.Equals(normalizedFormat, "txt", StringComparison.OrdinalIgnoreCase))
        {
            var text = await ExtractText(fullPath, 1_000_000);
            var txtPath = Path.Combine(output, Path.GetFileNameWithoutExtension(fullPath) + ".txt");
            await File.WriteAllTextAsync(txtPath, text, Encoding.UTF8);
            return new CommandResult(0, $"Wrote {txtPath}", "");
        }

        var result = await CommandRunner.Run(Guard.SofficePath, ["--headless", "--convert-to", normalizedFormat, "--outdir", output, fullPath], Path.GetTempPath(), Math.Clamp(timeoutMs, 1000, 86400000), 67108864);
        var outputPath = Path.Combine(output, Path.GetFileNameWithoutExtension(fullPath) + "." + normalizedFormat);
        var fallback = Directory.EnumerateFiles(output, Path.GetFileNameWithoutExtension(fullPath) + ".*").FirstOrDefault(file => string.Equals(Path.GetExtension(file), "." + normalizedFormat, StringComparison.OrdinalIgnoreCase));
        if (result.ExitCode != 0 || (!File.Exists(outputPath) && fallback is null) || result.Stderr.Contains("source file could not be loaded", StringComparison.OrdinalIgnoreCase))
            return new CommandResult(result.ExitCode == 0 ? 1 : result.ExitCode, result.Stdout, $"Conversion failed. Stdout={result.Stdout}; Stderr={result.Stderr}");

        return result with { Stdout = string.IsNullOrWhiteSpace(result.Stdout) ? $"Wrote {fallback ?? outputPath}" : result.Stdout };
    }

    private static async Task<string> ExtractHwpText(string path)
    {
        var internalText = ExtractHwpTextInternal(path);
        if (!string.IsNullOrWhiteSpace(internalText))
            return internalText;

        return await ExtractHwpTextWithHwp5Txt(path);
    }

    private static async Task<string> ExtractHwpTextWithHwp5Txt(string path)
    {
        if (!Guard.CommandExists(Guard.Hwp5TxtPath))
            return await ExtractHwpTextWithLibreOffice(path, new CommandResult(127, "", $"hwp5txt not found: {Guard.Hwp5TxtPath}"));

        var result = await CommandRunner.Run(Guard.Hwp5TxtPath, [path], Path.GetTempPath(), 3600000, 67108864);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            return result.Stdout;

        return await ExtractHwpTextWithLibreOffice(path, result);
    }

    private static async Task<string> ExtractHwpTextWithLibreOffice(string path, CommandResult hwp5Result)
    {
        if (!Guard.CommandExists(Guard.SofficePath))
            throw new InvalidOperationException($"HWP extraction failed. Internal parser found no text. hwp5txt ExitCode={hwp5Result.ExitCode}; hwp5txt Stderr={hwp5Result.Stderr}; LibreOffice not found: {Guard.SofficePath}");

        var tempDir = Path.Combine(Path.GetTempPath(), "mcp-hwp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
        var result = await CommandRunner.Run(Guard.SofficePath, ["--headless", "--convert-to", "txt:Text", "--outdir", tempDir, path], Path.GetTempPath(), 3600000, 67108864);
            var txtPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(path) + ".txt");
            if (File.Exists(txtPath))
                return await File.ReadAllTextAsync(txtPath, Encoding.UTF8);

            var fallback = Directory.EnumerateFiles(tempDir, "*.txt").FirstOrDefault();
            if (fallback is not null)
                return await File.ReadAllTextAsync(fallback, Encoding.UTF8);

            throw new InvalidOperationException($"HWP extraction failed. hwp5txt ExitCode={hwp5Result.ExitCode}; hwp5txt Stdout={hwp5Result.Stdout}; hwp5txt Stderr={hwp5Result.Stderr}; LibreOffice ExitCode={result.ExitCode}; LibreOffice Stdout={result.Stdout}; LibreOffice Stderr={result.Stderr}");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static string ExtractHwpTextInternal(string path)
    {
        try
        {
            using var file = RootStorage.OpenRead(path, StorageModeFlags.Transacted);
            var compressed = IsCompressed(file);
            var body = file.OpenStorage("BodyText");
            var sectionNames = body
                .EnumerateEntries()
                .Where(entry => entry.Type == EntryType.Stream && entry.Name.StartsWith("Section", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Name)
                .OrderBy(SectionNumber)
                .ToArray();

            var builder = new StringBuilder();
            foreach (var name in sectionNames)
            {
                var data = ReadAllBytes(body.OpenStream(name));
                var bytes = compressed ? TryDeflate(data) ?? data : data;
                var text = ExtractUtf16Runs(bytes);
                if (!string.IsNullOrWhiteSpace(text))
                    builder.AppendLine(text);
            }
            return builder.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static bool IsCompressed(RootStorage file)
    {
        try
        {
            var header = ReadAllBytes(file.OpenStream("FileHeader"));
            if (header.Length < 40)
                return false;
            var flags = BitConverter.ToUInt32(header, 36);
            return (flags & 0x01) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (stream)
        using (var output = new MemoryStream())
        {
            stream.CopyTo(output);
            return output.ToArray();
        }
    }

    private static byte[]? TryDeflate(byte[] bytes)
    {
        try
        {
            using var source = new MemoryStream(bytes);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractUtf16Runs(byte[] bytes)
    {
        var text = Encoding.Unicode.GetString(bytes);
        var matches = Regex.Matches(text, @"[\p{L}\p{N}\p{P}\p{S} \t가-힣]{2,}");
        var lines = matches
            .Select(match => NormalizeTextRun(match.Value))
            .Where(value => value.Length > 1 && value.Any(char.IsLetterOrDigit))
            .Distinct()
            .ToArray();
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeTextRun(string value) =>
        Regex.Replace(value.Replace('\0', ' ').Trim(), @"\s+", " ");

    private static int SectionNumber(string name) =>
        int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out var value) ? value : int.MaxValue;

    private static string ExtractHwpxText(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries
            .Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (entry.FullName.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("version", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreProcessingInstructions = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Text && reader.NodeType != XmlNodeType.CDATA)
                    continue;
                var text = reader.Value.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    builder.AppendLine(text);
            }
        }
        return builder.ToString();
    }

    private static string Trim(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..Math.Clamp(maxChars, 1, 10_000_000)] + "\n[truncated]";
}

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        using var process = StartProcess(fileName, startInfo);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return new CommandResult(process.ExitCode, Trim(await stdoutTask, maxOutputBytes), Trim(await stderrTask, maxOutputBytes));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new CommandResult(124, "", $"Command timed out after {timeoutMs}ms.");
        }
    }

    private static string Trim(string value, int maxBytes) => value.Length <= maxBytes ? value : value[..maxBytes] + "\n[truncated]";
    private static Process StartProcess(string fileName, ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start external command: {fileName}.");
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"External command not found or not executable: {fileName}. Install it or set PATH/configuration for this MCP server. {ex.Message}", fileName, ex);
        }
    }

    private static string ResolveWorkingDirectory(string workingDirectory) =>
        Directory.Exists(workingDirectory)
            ? workingDirectory
            : Path.GetTempPath();

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
}

internal static class Guard
{
    private static readonly string[] AllowedRoots = ParseAllowedRoots();
    public static string SofficePath => Environment.GetEnvironmentVariable("SOFFICE_PATH") ?? "soffice";
    public static string Hwp5TxtPath => Environment.GetEnvironmentVariable("HWP5TXT_PATH") ?? "hwp5txt";

    public static bool CommandExists(string fileName)
    {
        if (Path.IsPathRooted(fileName) || fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(fileName);

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + extension);
                if (File.Exists(candidate))
                    return true;
            }
        }
        return false;
    }

    public static string RequireAllowedPath(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        if (!AllowedRoots.Any(root => IsInside(fullPath, root)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    public static void RequireWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_HWP_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("HWP write tools are disabled because MCP_ENABLE_HWP_WRITES=false.");
    }

    public static string RequireSupportedOutput(string format)
    {
        var allowed = new[] { "txt", "docx", "pdf", "odt" };
        var normalized = format.Trim().TrimStart('.').ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unsupported output format: {format}. Allowed: {string.Join(", ", allowed)}");
        return normalized;
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
            return AllFilesystemRoots();
        var values = raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(NormalizeRoot).ToArray();
    }

    private static string[] AllFilesystemRoots() => OperatingSystem.IsWindows()
        ? DriveInfo.GetDrives().Select(drive => NormalizeRoot(drive.RootDirectory.FullName)).ToArray()
        : ["/"];

    private static bool IsInside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (root == Path.GetPathRoot(root))
            return string.Equals(Path.GetPathRoot(path), root, comparison);
        return path.Equals(root, comparison) ||
               path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static string TranslateHostPath(string path)
    {
        foreach (var mapping in ParsePathMappings())
        {
            if (path.Equals(mapping.HostPath, StringComparison.OrdinalIgnoreCase))
                return mapping.ContainerPath;
            if (path.StartsWith(mapping.HostPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(mapping.HostPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = path[mapping.HostPath.Length..].TrimStart('\\', '/').Replace('\\', Path.DirectorySeparatorChar);
                return Path.Combine(mapping.ContainerPath, relative);
            }
        }

        if (!OperatingSystem.IsWindows() && path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            throw new DirectoryNotFoundException($"Windows host path is not mounted in this Linux container: {path}. Mount it and set MCP_PATH_MAPPINGS, for example C:\\path=/container/path.");

        return path;
    }

    private static (string HostPath, string ContainerPath)[] ParsePathMappings() =>
        (Environment.GetEnvironmentVariable("MCP_PATH_MAPPINGS") ?? "")
            .Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('=', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            .Select(parts => (parts[0].TrimEnd('\\', '/'), parts[1]))
            .ToArray();

    private static string NormalizeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return fullPath == root ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
