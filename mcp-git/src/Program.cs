using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<GitTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-git" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class GitTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Run git status --short --branch in a repository.")]
    public static Task<CommandResult> Status(string repositoryPath = ".") =>
        Git(repositoryPath, "status", "--short", "--branch");

    [McpServerTool(ReadOnly = true)]
    [Description("Return recent git commits.")]
    public static Task<CommandResult> Log(string repositoryPath = ".", int maxCount = 20) =>
        Git(repositoryPath, "log", $"--max-count={Math.Clamp(maxCount, 1, 200)}", "--oneline", "--decorate");

    [McpServerTool(ReadOnly = true)]
    [Description("Show git diff output.")]
    public static Task<CommandResult> Diff(string repositoryPath = ".", string? refspec = null, bool staged = false)
    {
        var args = new List<string> { "diff" };
        if (staged) args.Add("--staged");
        if (!string.IsNullOrWhiteSpace(refspec)) args.Add(refspec);
        return Git(repositoryPath, args.ToArray());
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Show a git object or commit.")]
    public static Task<CommandResult> Show(string repositoryPath, string revision) =>
        Git(repositoryPath, "show", "--stat", "--patch", revision);

    [McpServerTool(ReadOnly = true)]
    [Description("List local and remote git branches.")]
    public static Task<CommandResult> BranchList(string repositoryPath = ".") =>
        Git(repositoryPath, "branch", "--all", "--verbose");

    [McpServerTool(ReadOnly = true)]
    [Description("Run git blame on a file.")]
    public static Task<CommandResult> Blame(string repositoryPath, string filePath, int? startLine = null, int? endLine = null)
    {
        var args = new List<string> { "blame" };
        if (startLine is not null && endLine is not null)
            args.Add($"-L{startLine},{endLine}");
        args.Add("--");
        args.Add(filePath);
        return Git(repositoryPath, args.ToArray());
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search tracked content using git grep.")]
    public static Task<CommandResult> Grep(string repositoryPath, string pattern, int maxMatches = 100) =>
        Git(repositoryPath, "grep", "-n", "-I", $"-m{Math.Clamp(maxMatches, 1, 1000)}", pattern);

    [McpServerTool]
    [Description("Run git init.")]
    public static Task<CommandResult> Init(string repositoryPath) => GitWrite(repositoryPath, "init");

    [McpServerTool]
    [Description("Run git add for paths.")]
    public static Task<CommandResult> Add(string repositoryPath, string[] paths)
    {
        var args = new List<string> { "add", "--" };
        args.AddRange(paths);
        return GitWrite(repositoryPath, args.ToArray());
    }

    [McpServerTool]
    [Description("Run git commit.")]
    public static Task<CommandResult> Commit(string repositoryPath, string message) =>
        GitWrite(repositoryPath, "commit", "-m", message);

    [McpServerTool]
    [Description("Run git checkout.")]
    public static Task<CommandResult> Checkout(string repositoryPath, string target, bool createBranch = false) =>
        createBranch ? GitWrite(repositoryPath, "checkout", "-b", target) : GitWrite(repositoryPath, "checkout", target);

    private static Task<CommandResult> Git(string repositoryPath, params string[] args) =>
        CommandRunner.Run("git", args, Guard.RequireAllowedDirectory(repositoryPath), 30000, 1048576);

    private static Task<CommandResult> GitWrite(string repositoryPath, params string[] args)
    {
        Guard.RequireGitWrites();
        return Git(repositoryPath, args);
    }
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
            var stdout = Trim(await stdoutTask, maxOutputBytes);
            var stderr = Trim(await stderrTask, maxOutputBytes);
            return new CommandResult(process.ExitCode, stdout, stderr);
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

    public static string RequireAllowedDirectory(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        if (!AllowedRoots.Any(root => root == Path.GetPathRoot(root) || fullPath.Equals(root, StringComparison.Ordinal) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    public static void RequireGitWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_GIT_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Git write tools are disabled because MCP_ENABLE_GIT_WRITES=false.");
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
