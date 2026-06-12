using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<DockerTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-docker" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class DockerTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return Docker client/server version information.")]
    public static Task<CommandResult> Version() => Docker(["version", "--format", "json"]);

    [McpServerTool(ReadOnly = true)]
    [Description("List Docker containers.")]
    public static Task<CommandResult> ListContainers(bool all = true, string format = "json")
    {
        var args = new List<string> { "ps" };
        if (all) args.Add("-a");
        args.AddRange(["--format", Guard.Format(format)]);
        return Docker([.. args]);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List Docker images.")]
    public static Task<CommandResult> ListImages(string format = "json") =>
        Docker(["images", "--format", Guard.Format(format)]);

    [McpServerTool(ReadOnly = true)]
    [Description("Inspect a Docker container or image.")]
    public static Task<CommandResult> Inspect(string target) => Docker(["inspect", target], maxOutputBytes: 4194304);

    [McpServerTool(ReadOnly = true)]
    [Description("Read Docker container logs.")]
    public static Task<CommandResult> Logs(string container, int tail = 200, bool timestamps = false)
    {
        var args = new List<string> { "logs", "--tail", Math.Clamp(tail, 1, 10000).ToString() };
        if (timestamps) args.Add("--timestamps");
        args.Add(container);
        return Docker([.. args], timeoutMs: 120000, maxOutputBytes: 4194304);
    }

    [McpServerTool]
    [Description("Run a Docker container.")]
    public static Task<CommandResult> RunContainer(string image, string? name = null, string[]? args = null, bool detach = true, string[]? ports = null, string[]? volumes = null, string[]? environment = null)
    {
        Guard.RequireDockerWrites();
        var cli = new List<string> { "run" };
        if (detach) cli.Add("-d");
        if (!string.IsNullOrWhiteSpace(name)) cli.AddRange(["--name", name]);
        foreach (var port in ports ?? []) cli.AddRange(["-p", port]);
        foreach (var volume in volumes ?? []) cli.AddRange(["-v", volume]);
        foreach (var env in environment ?? []) cli.AddRange(["-e", env]);
        cli.Add(image);
        cli.AddRange(args ?? []);
        return Docker([.. cli], timeoutMs: 300000, maxOutputBytes: 4194304);
    }

    [McpServerTool]
    [Description("Start a Docker container.")]
    public static Task<CommandResult> StartContainer(string container)
    {
        Guard.RequireDockerWrites();
        return Docker(["start", container]);
    }

    [McpServerTool]
    [Description("Stop a Docker container.")]
    public static Task<CommandResult> StopContainer(string container, int timeoutSeconds = 10)
    {
        Guard.RequireDockerWrites();
        return Docker(["stop", "--time", Math.Clamp(timeoutSeconds, 0, 300).ToString(), container], timeoutMs: 300000);
    }

    [McpServerTool]
    [Description("Remove a Docker container.")]
    public static Task<CommandResult> RemoveContainer(string container, bool force = false)
    {
        Guard.RequireDockerWrites();
        var args = new List<string> { "rm" };
        if (force) args.Add("-f");
        args.Add(container);
        return Docker([.. args], timeoutMs: 120000);
    }

    [McpServerTool]
    [Description("Pull a Docker image.")]
    public static Task<CommandResult> PullImage(string image)
    {
        Guard.RequireDockerWrites();
        return Docker(["pull", image], timeoutMs: 600000, maxOutputBytes: 4194304);
    }

    [McpServerTool]
    [Description("Remove a Docker image.")]
    public static Task<CommandResult> RemoveImage(string image, bool force = false)
    {
        Guard.RequireDockerWrites();
        var args = new List<string> { "rmi" };
        if (force) args.Add("-f");
        args.Add(image);
        return Docker([.. args], timeoutMs: 300000);
    }

    private static Task<CommandResult> Docker(string[] args, int timeoutMs = 60000, int maxOutputBytes = 2097152) =>
        CommandRunner.Run(Guard.DockerPath, args, "/workspace", timeoutMs, maxOutputBytes);
}

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(fileName) { WorkingDirectory = ResolveWorkingDirectory(workingDirectory), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
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

    private static string Trim(string value, int maxBytes) => Encoding.UTF8.GetByteCount(value) <= maxBytes ? value : value[..Math.Min(value.Length, maxBytes)] + "\n[truncated]";
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

    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
}

internal static class Guard
{
    public static string DockerPath => Environment.GetEnvironmentVariable("DOCKER_PATH") ?? "docker";

    public static void RequireDockerWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_DOCKER_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Docker write tools are disabled because MCP_ENABLE_DOCKER_WRITES=false.");
    }

    public static string Format(string format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();
        return normalized switch
        {
            "json" => "{{json .}}",
            "table" => "table {{.ID}}\t{{.Image}}\t{{.Names}}\t{{.Status}}",
            _ => format
        };
    }
}

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
