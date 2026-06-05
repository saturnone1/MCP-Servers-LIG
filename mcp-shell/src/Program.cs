using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<ShellTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-shell", enabled = Guard.ShellEnabled }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class ShellTools
{
    [McpServerTool]
    [Description("Run a command with arguments.")]
    public static Task<CommandResult> RunCommand(
        string command,
        string[]? args = null,
        string workingDirectory = ".",
        int timeoutMs = 30000,
        int maxOutputBytes = 1048576,
        Dictionary<string, string>? environment = null)
    {
        Guard.RequireShell();
        Guard.RequireAllowedCommand(command);
        var directory = Guard.RequireAllowedDirectory(workingDirectory);
        return CommandRunner.Run(command, args ?? [], directory, Math.Clamp(timeoutMs, 1000, 300000), Math.Clamp(maxOutputBytes, 1024, 4194304), environment);
    }
}

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes, Dictionary<string, string>? environment)
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
        foreach (var pair in Guard.FilterEnvironment(environment))
            startInfo.Environment[pair.Key] = pair.Value;
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
    private static readonly string[] AllowedCommands = ParseList("MCP_SHELL_ALLOWED_COMMANDS");
    private static readonly string[] AllowedEnv = ParseList("MCP_SHELL_ALLOWED_ENV");

    public static bool ShellEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_SHELL"), "false", StringComparison.OrdinalIgnoreCase);

    public static void RequireShell()
    {
        if (!ShellEnabled)
            throw new UnauthorizedAccessException("Shell execution is disabled because MCP_ENABLE_SHELL=false.");
    }

    public static void RequireAllowedCommand(string command)
    {
        if (AllowedCommands.Length > 0 && !AllowedCommands.Contains(Path.GetFileName(command), StringComparer.OrdinalIgnoreCase) && !AllowedCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Command is not allowed by MCP_SHELL_ALLOWED_COMMANDS: {command}");
    }

    public static string RequireAllowedDirectory(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        if (!AllowedRoots.Any(root => root == Path.GetPathRoot(root) || fullPath.Equals(root, StringComparison.Ordinal) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    public static IEnumerable<KeyValuePair<string, string>> FilterEnvironment(Dictionary<string, string>? environment)
    {
        if (environment is null || AllowedEnv.Length == 0)
            yield break;
        foreach (var pair in environment)
            if (AllowedEnv.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                yield return pair;
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        var values = string.IsNullOrWhiteSpace(raw)
            ? ["/"]
            : raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(NormalizeRoot).ToArray();
    }

    private static string[] ParseList(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
