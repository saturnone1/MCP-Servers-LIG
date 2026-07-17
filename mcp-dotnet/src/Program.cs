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
    [Description("Return dotnet --info for the external .NET SDK selected for project operations.")]
    public static async Task<CommandResult> SdkInfo()
    {
        var executable = DotnetCli.ResolveSdkExecutable();
        var result = await CommandRunner.RunDotnet(executable, ["--info"], "/workspace", 300000, 67108864);
        return result with { Stdout = $"dotnet executable: {executable}{Environment.NewLine}{result.Stdout}" };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Find .NET project and solution files under a workspace path.")]
    public static object[] ListProjects(string path = ".", int limit = 2000)
    {
        var root = Guard.RequireAllowedDirectory(path);
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 100000))
            .Select(p => new { path = p })
            .ToArray();
    }

    [McpServerTool]
    [Description("Run dotnet restore. This can populate package caches inside the container.")]
    public static Task<CommandResult> Restore(string projectOrSolutionPath, int timeoutMs = 600000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "restore", Guard.RequireAllowedPath(projectOrSolutionPath));
    }

    [McpServerTool]
    [Description("Run dotnet build, including restore when needed.")]
    public static Task<CommandResult> Build(string projectOrSolutionPath, string configuration = "Debug", int timeoutMs = 600000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "build", Guard.RequireAllowedPath(projectOrSolutionPath), "-c", configuration);
    }

    [McpServerTool]
    [Description("Run dotnet test, including restore and build when needed.")]
    public static Task<CommandResult> Test(string projectOrSolutionPath, string configuration = "Debug", int timeoutMs = 900000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "test", Guard.RequireAllowedPath(projectOrSolutionPath), "-c", configuration);
    }

    [McpServerTool]
    [Description("Run dotnet add package.")]
    public static Task<CommandResult> AddPackage(string projectPath, string packageName, string? version = null)
    {
        Guard.RequireDotnetWrites();
        var args = new List<string> { "add", Guard.RequireAllowedPath(projectPath), "package", packageName };
        if (!string.IsNullOrWhiteSpace(version))
            args.AddRange(["--version", version]);
        return CommandRunner.RunDotnet(args.ToArray(), Guard.WorkingDirectoryFor(projectPath), 600000, 67108864);
    }

    [McpServerTool]
    [Description("Run dotnet format.")]
    public static Task<CommandResult> Format(string projectOrSolutionPath, int timeoutMs = 600000)
    {
        Guard.RequireDotnetWrites();
        return Dotnet(projectOrSolutionPath, timeoutMs, "format", Guard.RequireAllowedPath(projectOrSolutionPath));
    }

    private static Task<CommandResult> Dotnet(string path, int timeoutMs, params string[] args) =>
        CommandRunner.RunDotnet(args, Guard.WorkingDirectoryFor(path), Math.Clamp(timeoutMs, 1000, 86400000), 67108864);
}

internal static class CommandRunner
{
    public static Task<CommandResult> RunDotnet(string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes) =>
        RunDotnet(DotnetCli.ResolveSdkExecutable(), args, workingDirectory, timeoutMs, maxOutputBytes);

    public static Task<CommandResult> RunDotnet(string executable, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes) =>
        Run(executable, args, workingDirectory, timeoutMs, maxOutputBytes);

    private static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        DotnetCli.ConfigureSdkProcess(startInfo, fileName);
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

internal static class DotnetCli
{
    private const string OverrideVariable = "MCP_DOTNET_CLI_PATH";

    public static string ResolveSdkExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return RequireSdkExecutable(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')), $"{OverrideVariable} is set");

        // Docker and Kubernetes run this server in an SDK image. The Windows MSI is the
        // special case because its DOTNET_ROOT intentionally contains a runtime only.
        if (!OperatingSystem.IsWindows())
            return "dotnet";

        var bundledDotnet = BundledDotnetPath();
        foreach (var candidate in WindowsCandidates())
        {
            if (PathsEqual(candidate, bundledDotnet) || !HasSdk(candidate))
                continue;
            return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException(
            "No external .NET SDK was found. Install a .NET 8, 9, or 10 SDK, add its dotnet.exe to PATH, " +
            $"or set {OverrideVariable} to the full path of an SDK-enabled dotnet.exe. The runtime-only dotnet bundled with LIG AI MCP is intentionally not used for project operations.");
    }

    public static void ConfigureSdkProcess(ProcessStartInfo startInfo, string executable)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathRooted(executable))
            return;

        startInfo.Environment.Remove("DOTNET_ROOT");
        startInfo.Environment.Remove("DOTNET_ROOT_X64");
        startInfo.Environment.Remove("DOTNET_ROOT_X86");
        startInfo.Environment.Remove("DOTNET_MULTILEVEL_LOOKUP");

        var sdkDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var bundledDirectory = Path.GetDirectoryName(BundledDotnetPath());
        var currentPath = startInfo.Environment.TryGetValue("PATH", out var value) ? value : Environment.GetEnvironmentVariable("PATH");
        var entries = (currentPath ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"'))
            .Where(entry => !PathsEqual(entry, bundledDirectory) && !PathsEqual(entry, sdkDirectory));
        startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, new[] { sdkDirectory }.Concat(entries));
    }

    private static IEnumerable<string> WindowsCandidates()
    {
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(dotnetHostPath))
            yield return dotnetHostPath.Trim().Trim('"');

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
            if (!string.IsNullOrWhiteSpace(directory))
                yield return Path.Combine(directory, "dotnet.exe");
        }

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            yield return Path.Combine(programFiles, "dotnet", "dotnet.exe");
        }
    }

    private static string RequireSdkExecutable(string candidate, string reason)
    {
        if (!Path.IsPathRooted(candidate))
            throw new FileNotFoundException($"{reason}, but the value is not an absolute path: {candidate}", candidate);
        if (!File.Exists(candidate))
            throw new FileNotFoundException($"{reason}, but dotnet.exe was not found: {candidate}", candidate);
        if (!HasSdk(candidate))
            throw new FileNotFoundException($"{reason}, but no SDK is installed beside this dotnet executable: {candidate}", candidate);
        return Path.GetFullPath(candidate);
    }

    private static bool HasSdk(string candidate)
    {
        if (!File.Exists(candidate))
            return false;
        var root = Path.GetDirectoryName(Path.GetFullPath(candidate));
        var sdkDirectory = root is null ? null : Path.Combine(root, "sdk");
        return sdkDirectory is not null && Directory.Exists(sdkDirectory) && Directory.EnumerateDirectories(sdkDirectory).Any();
    }

    private static string BundledDotnetPath()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        return string.IsNullOrWhiteSpace(root) ? "" : Path.Combine(root, "dotnet.exe");
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(left.Trim().Trim('"'))).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(right.Trim().Trim('"'))).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
