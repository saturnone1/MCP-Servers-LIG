using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<OfficeTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-office" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class OfficeTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return officecli --version.")]
    public static Task<CommandResult> Version() => OfficeCli(30000, "--version");

    [McpServerTool(ReadOnly = true)]
    [Description("Dump or inspect an Office document with OfficeCLI JSON output.")]
    public static Task<CommandResult> InspectDocument(string path, string mode = "text")
    {
        var fullPath = Guard.RequireAllowedPath(path);
        if (string.Equals(mode, "dump", StringComparison.OrdinalIgnoreCase))
            return OfficeCli(60000, "dump", fullPath, "/", "--json");
        return OfficeCli(60000, "view", fullPath, mode, "--json");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Extract readable text from .doc, .docx, .xlsx, or .pptx files.")]
    public static Task<CommandResult> ExtractText(string path, int maxLines = 200)
    {
        var fullPath = Guard.RequireAllowedPath(path);
        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase))
            return CommandRunner.Run(Environment.GetEnvironmentVariable("ANTIWORD_PATH") ?? "antiword", [fullPath], "/workspace", 60000, 2097152);

        return OfficeCli(60000, "view", fullPath, "text", "--max-lines", Math.Clamp(maxLines, 1, 2000).ToString(), "--json");
    }

    [McpServerTool]
    [Description("Create an Office document with OfficeCLI.")]
    public static Task<CommandResult> CreateDocument(string path)
    {
        Guard.RequireOfficeWrites();
        var fullPath = Guard.RequireAllowedPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return OfficeCli(60000, "create", fullPath, "--json");
    }

    [McpServerTool]
    [Description("Apply an OfficeCLI batch JSON file to a document.")]
    public static Task<CommandResult> ApplyBatch(string documentPath, string batchJsonPath)
    {
        Guard.RequireOfficeWrites();
        var document = Guard.RequireAllowedPath(documentPath);
        var batch = Guard.RequireAllowedPath(batchJsonPath);
        return OfficeCli(120000, "batch", document, batch, "--json");
    }

    [McpServerTool]
    [Description("Export a text snapshot of an Office document to an output path.")]
    public static async Task<CommandResult> RenderDocument(string documentPath, string outputPath)
    {
        Guard.RequireOfficeWrites();
        var document = Guard.RequireAllowedPath(documentPath);
        var output = Guard.RequireAllowedPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var result = await OfficeCli(120000, "view", document, "text", "--json");
        if (result.ExitCode != 0)
            return result;
        await File.WriteAllTextAsync(output, result.Stdout);
        return result with { Stdout = $"Wrote text snapshot to {output}\n{result.Stdout}" };
    }

    [McpServerTool]
    [Description("Run raw OfficeCLI arguments inside the container.")]
    public static Task<CommandResult> RunOfficeCli(string[] args, int timeoutMs = 120000)
    {
        Guard.RequireOfficeWrites();
        return OfficeCli(Math.Clamp(timeoutMs, 1000, 300000), args);
    }

    private static Task<CommandResult> OfficeCli(int timeoutMs, params string[] args) =>
        CommandRunner.Run(Environment.GetEnvironmentVariable("OFFICECLI_PATH") ?? "officecli", args, "/workspace", timeoutMs, 2097152);
}

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : "/",
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

    public static string RequireAllowedPath(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        if (!AllowedRoots.Any(root => root == Path.GetPathRoot(root) || fullPath.Equals(root, StringComparison.Ordinal) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    public static void RequireOfficeWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_OFFICE_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Office write tools are disabled because MCP_ENABLE_OFFICE_WRITES=false.");
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
