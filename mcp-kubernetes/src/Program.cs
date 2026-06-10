using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<KubernetesTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-kubernetes" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class KubernetesTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return kubectl client and server version information.")]
    public static Task<CommandResult> Version(bool clientOnly = true) =>
        Kubectl(clientOnly ? ["version", "--client=true", "-o", "json"] : ["version", "-o", "json"]);

    [McpServerTool(ReadOnly = true)]
    [Description("Show Kubernetes cluster information.")]
    public static Task<CommandResult> ClusterInfo() => Kubectl(["cluster-info"]);

    [McpServerTool(ReadOnly = true)]
    [Description("List namespaces.")]
    public static Task<CommandResult> ListNamespaces(string output = "json") =>
        Kubectl(["get", "namespaces", "-o", Guard.Output(output)]);

    [McpServerTool(ReadOnly = true)]
    [Description("List pods in a namespace or all namespaces.")]
    public static Task<CommandResult> ListPods(string? ns = null, bool allNamespaces = false, string output = "json")
    {
        var args = new List<string> { "get", "pods" };
        AddNamespace(args, ns, allNamespaces);
        args.AddRange(["-o", Guard.Output(output)]);
        return Kubectl([.. args]);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get logs from a pod.")]
    public static Task<CommandResult> PodLogs(string podName, string? ns = null, string? container = null, int tailLines = 200, bool previous = false)
    {
        var args = new List<string> { "logs", podName, "--tail", Math.Clamp(tailLines, 1, 10000).ToString() };
        if (!string.IsNullOrWhiteSpace(ns)) args.AddRange(["-n", ns]);
        if (!string.IsNullOrWhiteSpace(container)) args.AddRange(["-c", container]);
        if (previous) args.Add("--previous");
        return Kubectl([.. args], timeoutMs: 120000, maxOutputBytes: 4194304);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List deployments in a namespace or all namespaces.")]
    public static Task<CommandResult> ListDeployments(string? ns = null, bool allNamespaces = false, string output = "json")
    {
        var args = new List<string> { "get", "deployments" };
        AddNamespace(args, ns, allNamespaces);
        args.AddRange(["-o", Guard.Output(output)]);
        return Kubectl([.. args]);
    }

    [McpServerTool]
    [Description("Apply a Kubernetes YAML manifest file.")]
    public static Task<CommandResult> ApplyYaml(string path, string? ns = null)
    {
        Guard.RequireKubernetesWrites();
        var args = new List<string> { "apply", "-f", Guard.RequireAllowedFile(path) };
        if (!string.IsNullOrWhiteSpace(ns)) args.AddRange(["-n", ns]);
        return Kubectl([.. args], timeoutMs: 120000);
    }

    [McpServerTool]
    [Description("Delete a Kubernetes resource by kind and name.")]
    public static Task<CommandResult> DeleteResource(string kind, string name, string? ns = null)
    {
        Guard.RequireKubernetesWrites();
        var args = new List<string> { "delete", kind, name };
        if (!string.IsNullOrWhiteSpace(ns)) args.AddRange(["-n", ns]);
        return Kubectl([.. args], timeoutMs: 120000);
    }

    [McpServerTool]
    [Description("Restart a Kubernetes deployment rollout.")]
    public static Task<CommandResult> RolloutRestart(string deploymentName, string? ns = null)
    {
        Guard.RequireKubernetesWrites();
        var args = new List<string> { "rollout", "restart", "deployment/" + deploymentName };
        if (!string.IsNullOrWhiteSpace(ns)) args.AddRange(["-n", ns]);
        return Kubectl([.. args], timeoutMs: 120000);
    }

    [McpServerTool]
    [Description("Scale a Kubernetes deployment.")]
    public static Task<CommandResult> ScaleDeployment(string deploymentName, int replicas, string? ns = null)
    {
        Guard.RequireKubernetesWrites();
        var args = new List<string> { "scale", "deployment/" + deploymentName, "--replicas", Math.Max(0, replicas).ToString() };
        if (!string.IsNullOrWhiteSpace(ns)) args.AddRange(["-n", ns]);
        return Kubectl([.. args], timeoutMs: 120000);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Generate a simple Deployment YAML manifest.")]
    public static string GenerateDeploymentYaml(string name, string image, int replicas = 1, int containerPort = 80, string? ns = null)
    {
        var namespaceBlock = string.IsNullOrWhiteSpace(ns) ? "" : $"  namespace: {ns}\n";
        return $$"""
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{name}}
{{namespaceBlock}}spec:
  replicas: {{Math.Max(0, replicas)}}
  selector:
    matchLabels:
      app: {{name}}
  template:
    metadata:
      labels:
        app: {{name}}
    spec:
      containers:
        - name: {{name}}
          image: {{image}}
          ports:
            - containerPort: {{containerPort}}
""";
    }

    [McpServerTool]
    [Description("Run raw kubectl arguments.")]
    public static Task<CommandResult> RunKubectl(string[] args, int timeoutMs = 120000)
    {
        Guard.RequireRawKubectl();
        return Kubectl(args, timeoutMs: Math.Clamp(timeoutMs, 1000, 300000), maxOutputBytes: 4194304);
    }

    private static Task<CommandResult> Kubectl(string[] args, int timeoutMs = 60000, int maxOutputBytes = 2097152) =>
        CommandRunner.Run(Guard.KubectlPath, args, "/workspace", timeoutMs, maxOutputBytes);

    private static void AddNamespace(List<string> args, string? ns, bool allNamespaces)
    {
        if (allNamespaces) args.Add("--all-namespaces");
        else if (!string.IsNullOrWhiteSpace(ns)) args.AddRange(["-n", ns]);
    }
}

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var startInfo = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
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

    private static string Trim(string value, int maxBytes) => Encoding.UTF8.GetByteCount(value) <= maxBytes ? value : value[..Math.Min(value.Length, maxBytes)] + "\n[truncated]";
    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
}

internal static class Guard
{
    private static readonly string[] AllowedRoots = ParseAllowedRoots();
    public static string KubectlPath => Environment.GetEnvironmentVariable("KUBECTL_PATH") ?? "kubectl";

    public static void RequireKubernetesWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_KUBERNETES_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Kubernetes write tools are disabled because MCP_ENABLE_KUBERNETES_WRITES=false.");
    }

    public static void RequireRawKubectl()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_RAW_KUBECTL"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Raw kubectl is disabled because MCP_ENABLE_RAW_KUBECTL=false.");
    }

    public static string Output(string output)
    {
        var normalized = string.IsNullOrWhiteSpace(output) ? "json" : output.Trim().ToLowerInvariant();
        var allowed = new[] { "json", "yaml", "wide", "name" };
        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unsupported output: {output}. Allowed: {string.Join(", ", allowed)}");
        return normalized;
    }

    public static string RequireAllowedFile(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"File not found after path mapping: {fullPath}", fullPath);
        if (!AllowedRoots.Any(root => root == Path.GetPathRoot(root) || fullPath.Equals(root, StringComparison.Ordinal) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        var values = string.IsNullOrWhiteSpace(raw) ? ["/"] : raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(NormalizeRoot).ToArray();
    }

    private static string TranslateHostPath(string path)
    {
        foreach (var mapping in ParsePathMappings())
        {
            if (path.Equals(mapping.HostPath, StringComparison.OrdinalIgnoreCase)) return mapping.ContainerPath;
            if (path.StartsWith(mapping.HostPath + "\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(mapping.HostPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = path[mapping.HostPath.Length..].TrimStart('\\', '/').Replace('\\', Path.DirectorySeparatorChar);
                return Path.Combine(mapping.ContainerPath, relative);
            }
        }
        if (!OperatingSystem.IsWindows() && path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            throw new DirectoryNotFoundException($"Windows host path is not mounted in this Linux container: {path}. Mount it and set MCP_PATH_MAPPINGS.");
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
