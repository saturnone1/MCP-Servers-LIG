using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
builder.Services.AddHttpClient();
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<PlantUmlTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-plantuml", renderer = Guard.DescribeRenderer() }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class PlantUmlTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return the renderer this MCP server resolved and the PlantUML configuration it will use.")]
    public static object Config() => new
    {
        renderer = Guard.DescribeRenderer(),
        jarPath = Guard.JarPath,
        javaPath = Guard.JavaPath,
        cliPath = Guard.CliPath,
        serverUrl = Guard.ServerUrl,
        includePath = Guard.IncludePath,
        offlineCapable = Guard.ResolveRenderer() is RendererKind.Jar or RendererKind.Cli,
        writesEnabled = Guard.WritesEnabled,
        formats = Guard.SupportedFormats
    };

    [McpServerTool(ReadOnly = true)]
    [Description("List the output formats this server accepts and whether each one is returned as text or base64.")]
    public static object ListFormats() => Guard.SupportedFormats
        .Select(format => new { format, encoding = Guard.IsTextFormat(format) ? "text" : "base64" })
        .ToArray();

    [McpServerTool(ReadOnly = true)]
    [Description("Render PlantUML source and return the diagram. Text formats such as svg and txt come back as text, binary formats such as png come back base64 encoded.")]
    public static async Task<RenderResult> RenderDiagram(string source, string format = "svg", int timeoutMs = 120000)
    {
        var normalized = Guard.RequireSupportedFormat(format);
        var rendered = await Render(source, normalized, timeoutMs);
        return Describe(rendered, normalized);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Read a PlantUML source file and render it without writing anything to disk.")]
    public static async Task<RenderResult> RenderSourceFile(string path, string format = "svg", int timeoutMs = 120000)
    {
        var normalized = Guard.RequireSupportedFormat(format);
        var source = await ReadSourceInternal(path);
        var rendered = await Render(source, normalized, timeoutMs);
        return Describe(rendered, normalized);
    }

    [McpServerTool]
    [Description("Render PlantUML source and write the diagram to a file inside MCP_ALLOWED_DIRS.")]
    public static async Task<WriteResult> RenderToFile(string source, string outputPath, string format = "svg", int timeoutMs = 120000)
    {
        Guard.RequireWrites();
        var normalized = Guard.RequireSupportedFormat(format);
        var fullPath = Guard.RequireAllowedPath(outputPath);
        var rendered = await Render(source, normalized, timeoutMs);
        return await WriteRendered(rendered, fullPath, normalized);
    }

    [McpServerTool]
    [Description("Render a PlantUML source file and write the diagram next to it or into an output directory inside MCP_ALLOWED_DIRS.")]
    public static async Task<WriteResult> RenderFileToDirectory(string path, string outputDirectory = "", string format = "svg", int timeoutMs = 120000)
    {
        Guard.RequireWrites();
        var normalized = Guard.RequireSupportedFormat(format);
        var sourcePath = Guard.RequireAllowedPath(path);
        var source = await ReadSourceInternal(path);

        var resolvedDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(sourcePath) ?? Path.GetTempPath()
            : outputDirectory;
        var directory = Guard.RequireAllowedPath(resolvedDirectory);
        Directory.CreateDirectory(directory);

        var outputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(sourcePath) + "." + normalized);
        var rendered = await Render(source, normalized, timeoutMs);
        return await WriteRendered(rendered, outputPath, normalized);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Check PlantUML source for syntax errors without producing a diagram.")]
    public static async Task<SyntaxResult> CheckSyntax(string source, int timeoutMs = 60000)
    {
        var renderer = Guard.ResolveRenderer();
        if (renderer is RendererKind.Jar or RendererKind.Cli)
        {
            var result = await RunLocal(Guard.BuildLocalArguments(["-syntax"]), source, timeoutMs);
            var report = Encoding.UTF8.GetString(result.Stdout).Trim();
            var failed = result.ExitCode != 0 || report.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
            return new SyntaxResult(!failed, report, result.Stderr);
        }

        // The remote server has no syntax endpoint, so a throwaway render is the only signal available.
        var rendered = await Render(source, "svg", timeoutMs);
        var text = Encoding.UTF8.GetString(rendered.Content);
        var hasError = text.Contains("syntax error", StringComparison.OrdinalIgnoreCase);
        return new SyntaxResult(!hasError, hasError ? text : "No syntax error reported by the PlantUML server.", "");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Read a PlantUML source file from inside MCP_ALLOWED_DIRS.")]
    public static Task<string> ReadSource(string path) => ReadSourceInternal(path);

    [McpServerTool(ReadOnly = true)]
    [Description("Encode PlantUML source into the compressed form PlantUML servers accept, and return the shareable URL when a server is configured.")]
    public static object EncodeUrl(string source, string format = "svg")
    {
        var normalized = Guard.RequireSupportedFormat(format);
        var encoded = PlantUmlEncoding.Encode(source);
        var server = Guard.ServerUrl;
        return new
        {
            encoded,
            url = string.IsNullOrWhiteSpace(server) ? null : $"{server.TrimEnd('/')}/{normalized}/{encoded}",
            serverConfigured = !string.IsNullOrWhiteSpace(server)
        };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Decode a PlantUML encoded string back into diagram source. Accepts a bare encoding or a full PlantUML server URL.")]
    public static string DecodeUrl(string encoded) => PlantUmlEncoding.Decode(ExtractEncoding(encoded));

    private static RenderResult Describe(RenderedDiagram rendered, string format) =>
        Guard.IsTextFormat(format)
            ? new RenderResult(format, "text", Encoding.UTF8.GetString(rendered.Content), rendered.Content.Length, rendered.Renderer, rendered.Diagnostics)
            : new RenderResult(format, "base64", Convert.ToBase64String(rendered.Content), rendered.Content.Length, rendered.Renderer, rendered.Diagnostics);

    private static async Task<WriteResult> WriteRendered(RenderedDiagram rendered, string outputPath, string format)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(outputPath, rendered.Content);
        return new WriteResult(outputPath, format, rendered.Content.Length, rendered.Renderer, rendered.Diagnostics);
    }

    private static async Task<string> ReadSourceInternal(string path)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"PlantUML source not found after path mapping: {fullPath}", fullPath);
        return await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
    }

    private static async Task<RenderedDiagram> Render(string source, string format, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ValidationException("PlantUML source is empty.");

        var clampedTimeout = Math.Clamp(timeoutMs, 1000, 3600000);
        var renderer = Guard.ResolveRenderer();
        switch (renderer)
        {
            case RendererKind.Jar:
            case RendererKind.Cli:
            {
                var result = await RunLocal(Guard.BuildLocalArguments(["-pipe", "-t" + format, "-charset", "UTF-8"]), source, clampedTimeout);
                if (result.ExitCode != 0 || result.Stdout.Length == 0)
                    throw new InvalidOperationException($"PlantUML rendering failed with exit code {result.ExitCode}. {result.Stderr}");
                return new RenderedDiagram(result.Stdout, renderer.ToString().ToLowerInvariant(), result.Stderr);
            }
            case RendererKind.Server:
            {
                var encoded = PlantUmlEncoding.Encode(source);
                var url = $"{Guard.ServerUrl!.TrimEnd('/')}/{format}/{encoded}";
                try
                {
                    using var response = await Client.GetAsync(url);
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"PlantUML server returned {(int)response.StatusCode} for {url}.");
                    return new RenderedDiagram(bytes, "server", "");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
                {
                    throw new InvalidOperationException($"PlantUML server request failed for {url}: {ex.Message}", ex);
                }
            }
            default:
                throw new InvalidOperationException(
                    "No PlantUML renderer is configured. Set PLANTUML_JAR_PATH to a plantuml.jar, put the plantuml CLI on PATH, or set PLANTUML_SERVER_URL to a reachable PlantUML server.");
        }
    }

    private static async Task<LocalResult> RunLocal(LocalCommand command, string source, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = StartProcess(command.FileName, startInfo);
        using var stdout = new MemoryStream();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(source), cts.Token);
            await process.StandardInput.BaseStream.FlushAsync(cts.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cts.Token);
            await stdoutTask;
            return new LocalResult(process.ExitCode, stdout.ToArray(), await stderrTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException($"PlantUML rendering timed out after {timeoutMs}ms.");
        }
    }

    private static Process StartProcess(string fileName, ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start PlantUML renderer: {fileName}.");
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"PlantUML renderer not found or not executable: {fileName}. Install Java and set PLANTUML_JAR_PATH, or put the plantuml CLI on PATH. {ex.Message}", fileName, ex);
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static string ExtractEncoding(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.Contains('/')) return trimmed;
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments[^1];
    }
}

internal static class PlantUmlEncoding
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

    public static string Encode(string source)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            deflate.Write(bytes, 0, bytes.Length);
        }
        return ToPlantUmlBase64(buffer.ToArray());
    }

    public static string Decode(string encoded)
    {
        var compressed = FromPlantUmlBase64(encoded);
        using var input = new MemoryStream(compressed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        try
        {
            deflate.CopyTo(output);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"Value is not a PlantUML encoded diagram: {ex.Message}", ex);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string ToPlantUmlBase64(byte[] data)
    {
        var builder = new StringBuilder(((data.Length + 2) / 3) * 4);
        for (var index = 0; index < data.Length; index += 3)
        {
            var b1 = data[index];
            var b2 = index + 1 < data.Length ? data[index + 1] : (byte)0;
            var b3 = index + 2 < data.Length ? data[index + 2] : (byte)0;
            builder.Append(Alphabet[(b1 & 0xFC) >> 2]);
            builder.Append(Alphabet[((b1 & 0x03) << 4) | ((b2 & 0xF0) >> 4)]);
            builder.Append(Alphabet[((b2 & 0x0F) << 2) | ((b3 & 0xC0) >> 6)]);
            builder.Append(Alphabet[b3 & 0x3F]);
        }
        return builder.ToString();
    }

    private static byte[] FromPlantUmlBase64(string encoded)
    {
        var values = new int[encoded.Length];
        for (var index = 0; index < encoded.Length; index++)
        {
            var position = Alphabet.IndexOf(encoded[index]);
            if (position < 0)
                throw new InvalidOperationException($"Value contains a character that is not part of the PlantUML alphabet: {encoded[index]}");
            values[index] = position;
        }

        using var output = new MemoryStream();
        for (var index = 0; index + 1 < values.Length; index += 4)
        {
            var c1 = values[index];
            var c2 = values[index + 1];
            var c3 = index + 2 < values.Length ? values[index + 2] : 0;
            var c4 = index + 3 < values.Length ? values[index + 3] : 0;
            output.WriteByte((byte)(((c1 << 2) | (c2 >> 4)) & 0xFF));
            if (index + 2 < values.Length) output.WriteByte((byte)(((c2 << 4) | (c3 >> 2)) & 0xFF));
            if (index + 3 < values.Length) output.WriteByte((byte)(((c3 << 6) | c4) & 0xFF));
        }
        return output.ToArray();
    }
}

internal enum RendererKind
{
    None,
    Jar,
    Cli,
    Server
}

internal static class Guard
{
    private static readonly string[] AllowedRoots = ParseAllowedRoots();
    private static readonly string[] TextFormats = ["svg", "txt", "utxt", "latex"];

    public static string[] SupportedFormats => ["svg", "png", "txt", "utxt", "eps", "latex"];
    public static string JarPath => Environment.GetEnvironmentVariable("PLANTUML_JAR_PATH") ?? "";
    public static string JavaPath => Environment.GetEnvironmentVariable("JAVA_PATH") ?? "java";
    public static string CliPath => Environment.GetEnvironmentVariable("PLANTUML_PATH") ?? "plantuml";
    public static string? ServerUrl => Environment.GetEnvironmentVariable("PLANTUML_SERVER_URL");
    public static string IncludePath => Environment.GetEnvironmentVariable("PLANTUML_INCLUDE_PATH") ?? "";
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_PLANTUML_WRITES"), "false", StringComparison.OrdinalIgnoreCase);

    public static RendererKind ResolveRenderer()
    {
        if (!string.IsNullOrWhiteSpace(JarPath) && File.Exists(JarPath) && CommandExists(JavaPath))
            return RendererKind.Jar;
        if (CommandExists(CliPath))
            return RendererKind.Cli;
        if (!string.IsNullOrWhiteSpace(ServerUrl))
            return RendererKind.Server;
        return RendererKind.None;
    }

    public static string DescribeRenderer() => ResolveRenderer() switch
    {
        RendererKind.Jar => $"jar:{JarPath}",
        RendererKind.Cli => $"cli:{CliPath}",
        RendererKind.Server => $"server:{ServerUrl}",
        _ => "none"
    };

    public static LocalCommand BuildLocalArguments(string[] modeArguments)
    {
        var arguments = new List<string>();
        var renderer = ResolveRenderer();
        var fileName = renderer == RendererKind.Jar ? JavaPath : CliPath;
        if (renderer == RendererKind.Jar)
        {
            arguments.Add("-Djava.awt.headless=true");
            if (!string.IsNullOrWhiteSpace(IncludePath))
                arguments.Add($"-Dplantuml.include.path={IncludePath}");
            arguments.Add("-jar");
            arguments.Add(JarPath);
        }
        else if (!string.IsNullOrWhiteSpace(IncludePath))
        {
            arguments.Add($"-Dplantuml.include.path={IncludePath}");
        }
        arguments.AddRange(modeArguments);
        return new LocalCommand(fileName, arguments.ToArray());
    }

    public static bool IsTextFormat(string format) => TextFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    public static string RequireSupportedFormat(string format)
    {
        var normalized = format.Trim().TrimStart('.').ToLowerInvariant();
        if (!SupportedFormats.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unsupported output format: {format}. Allowed: {string.Join(", ", SupportedFormats)}");
        return normalized;
    }

    public static void RequireWrites()
    {
        if (!WritesEnabled)
            throw new UnauthorizedAccessException("PlantUML write tools are disabled because MCP_ENABLE_PLANTUML_WRITES=false.");
    }

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

internal sealed record LocalCommand(string FileName, string[] Arguments);
internal sealed record LocalResult(int ExitCode, byte[] Stdout, string Stderr);
internal sealed record RenderedDiagram(byte[] Content, string Renderer, string Diagnostics);

public sealed record RenderResult(string Format, string Encoding, string Content, int ByteCount, string Renderer, string Diagnostics);
public sealed record WriteResult(string Path, string Format, int ByteCount, string Renderer, string Diagnostics);
public sealed record SyntaxResult(bool Valid, string Report, string Diagnostics);
