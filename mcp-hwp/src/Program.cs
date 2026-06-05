using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
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
    public static async Task<string> ExtractText(string path, int maxChars = 20000)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".hwpx", StringComparison.OrdinalIgnoreCase))
            return Trim(ExtractHwpxText(fullPath), maxChars);
        if (string.Equals(extension, ".hwp", StringComparison.OrdinalIgnoreCase))
            return Trim(await ExtractHwpTextWithHwp5Txt(fullPath), maxChars);
        throw new NotSupportedException("Only .hwp and .hwpx files are supported.");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return basic metadata for a .hwp or .hwpx file.")]
    public static object Inspect(string path)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        var info = new FileInfo(fullPath);
        return new
        {
            path = fullPath,
            extension = info.Extension,
            info.Length,
            info.LastWriteTimeUtc,
            format = string.Equals(info.Extension, ".hwpx", StringComparison.OrdinalIgnoreCase) ? "HWPX zip/xml" : "HWP binary"
        };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Convert .hwp or .hwpx to txt, docx, pdf, or odt using LibreOffice.")]
    public static async Task<CommandResult> Convert(string path, string outputDirectory = "/tmp/hwp-output", string format = "txt", int timeoutMs = 120000)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        var output = Guard.RequireAllowedPath(outputDirectory);
        Directory.CreateDirectory(output);
        Guard.RequireSupportedOutput(format);
        return await CommandRunner.Run(Guard.SofficePath, ["--headless", "--convert-to", format, "--outdir", output, fullPath], "/tmp", Math.Clamp(timeoutMs, 1000, 600000), 2097152);
    }

    private static async Task<string> ExtractHwpTextWithHwp5Txt(string path)
    {
        var result = await CommandRunner.Run(Guard.Hwp5TxtPath, [path], "/tmp", 120000, 2097152);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            return result.Stdout;

        return await ExtractHwpTextWithLibreOffice(path, result);
    }

    private static async Task<string> ExtractHwpTextWithLibreOffice(string path, CommandResult hwp5Result)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mcp-hwp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = await CommandRunner.Run(Guard.SofficePath, ["--headless", "--convert-to", "txt:Text", "--outdir", tempDir, path], "/tmp", 120000, 2097152);
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
        text.Length <= maxChars ? text : text[..Math.Clamp(maxChars, 1, 1_000_000)] + "\n[truncated]";
}

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
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

    public static string RequireAllowedPath(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        if (!AllowedRoots.Any(root => root == Path.GetPathRoot(root) || fullPath.Equals(root, StringComparison.Ordinal) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    public static void RequireSupportedOutput(string format)
    {
        var allowed = new[] { "txt", "docx", "pdf", "odt" };
        if (!allowed.Contains(format, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unsupported output format: {format}. Allowed: {string.Join(", ", allowed)}");
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        var values = string.IsNullOrWhiteSpace(raw)
            ? ["/"]
            : raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(NormalizeRoot).ToArray();
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
