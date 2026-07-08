using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<DotnetTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-dotnet" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class DotnetTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return dotnet --info.")]
    public static Task<CommandResult> SdkInfo() => CommandRunner.Run("dotnet", ["--info"], "/workspace", 30000, 1048576);

    [McpServerTool(ReadOnly = true)]
    [Description("Find .NET project and solution files under a workspace path.")]
    public static object[] ListProjects(string path = ".", int limit = 200)
    {
        var root = Guard.RequireAllowedDirectory(path);
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(p => new { path = p })
            .ToArray();
    }

    [McpServerTool]
    [Description("Run dotnet restore. This can populate package caches inside the container.")]
    public static Task<CommandResult> Restore(string projectOrSolutionPath, int timeoutMs = 120000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "restore", Guard.RequireAllowedPath(projectOrSolutionPath));
    }

    [McpServerTool]
    [Description("Run dotnet build --no-restore.")]
    public static Task<CommandResult> Build(string projectOrSolutionPath, string configuration = "Debug", int timeoutMs = 120000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "build", Guard.RequireAllowedPath(projectOrSolutionPath), "--no-restore", "-c", configuration);
    }

    [McpServerTool]
    [Description("Run dotnet test --no-build.")]
    public static Task<CommandResult> Test(string projectOrSolutionPath, string configuration = "Debug", int timeoutMs = 180000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "test", Guard.RequireAllowedPath(projectOrSolutionPath), "--no-build", "-c", configuration);
    }

    [McpServerTool]
    [Description("Run dotnet add package.")]
    public static Task<CommandResult> AddPackage(string projectPath, string packageName, string? version = null)
    {
        Guard.RequireDotnetWrites();
        var args = new List<string> { "add", Guard.RequireAllowedPath(projectPath), "package", packageName };
        if (!string.IsNullOrWhiteSpace(version))
            args.AddRange(["--version", version]);
        return CommandRunner.Run("dotnet", args.ToArray(), Guard.WorkingDirectoryFor(projectPath), 120000, 1048576);
    }

    [McpServerTool]
    [Description("Run dotnet format.")]
    public static Task<CommandResult> Format(string projectOrSolutionPath, int timeoutMs = 120000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "format", Guard.RequireAllowedPath(projectOrSolutionPath));
    }

    private static Task<CommandResult> Dotnet(string path, int timeoutMs, params string[] args) =>
        CommandRunner.Run("dotnet", args, Guard.WorkingDirectoryFor(path), Math.Clamp(timeoutMs, 1000, 600000), 2097152);
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
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
        if (!AllowedRoots.Any(root => IsInside(fullPath, root)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    public static string RequireAllowedDirectory(string path)
    {
        var fullPath = RequireAllowedPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    public static string WorkingDirectoryFor(string path)
    {
        var fullPath = RequireAllowedPath(path);
        if (Directory.Exists(fullPath))
            return fullPath;
        return Path.GetDirectoryName(fullPath) ?? "/workspace";
    }

    public static void RequireDotnetWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_DOTNET_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Dotnet write tools are disabled because MCP_ENABLE_DOTNET_WRITES=false.");
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        var values = string.IsNullOrWhiteSpace(raw)
            ? ["/"]
            : raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(NormalizeRoot).ToArray();
    }

    private static bool IsInside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return root == Path.GetPathRoot(root) ||
               path.Equals(root, comparison) ||
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
