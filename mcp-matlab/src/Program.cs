using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8095");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<MatlabTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-matlab", mode = "windows-host" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class MatlabTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return MATLAB MCP configuration, detected MATLAB executable, COM status, and official MathWorks MCP server path.")]
    public static object Config()
    {
        var progId = Environment.GetEnvironmentVariable("MATLAB_COM_PROGID") ?? "Matlab.Application";
        var comRegistered = Com.IsRegistered(progId, out var registrationError);
        var activeComAvailable = Com.TryGetActive(progId, out _, out var activeError);
        return new
        {
            server = "mcp-matlab",
            mode = "windows-host",
            lineage = "Official MathWorks MATLAB MCP Core Server plus local C# Windows host tools.",
            http = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8095",
            allowedDirs = Guard.AllowedRoots,
            writesEnabled = Guard.WritesEnabled,
            officialMcpPath = Environment.GetEnvironmentVariable("MATLAB_MCP_CORE_SERVER_PATH"),
            officialMcpArgs = OfficialMcp.ConfiguredArgs,
            matlabExe = MatlabDetection.ResolveMatlabExe(),
            matlabRoot = Environment.GetEnvironmentVariable("MATLAB_ROOT"),
            comProgId = progId,
            comAvailable = comRegistered,
            comRegistered,
            activeComAvailable,
            comError = registrationError ?? activeError
        };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Detect MATLAB executable candidates from env vars, PATH, and common install folders.")]
    public static object DetectInstallations() => MatlabDetection.Detect();

    [McpServerTool]
    [Description("Run MATLAB in batch mode with the provided MATLAB command text.")]
    public static Task<CommandResult> RunBatch(string command, int timeoutMs = 300000)
    {
        var matlab = MatlabDetection.ResolveMatlabExe() ?? throw new FileNotFoundException("MATLAB executable was not found. Set MATLAB_EXE_PATH or add matlab to PATH.");
        return CommandRunner.Run(matlab, ["-batch", command], Environment.CurrentDirectory, Math.Clamp(timeoutMs, 1000, 3600000), 8 * 1024 * 1024);
    }

    [McpServerTool]
    [Description("Run a MATLAB .m script file with matlab -batch run('path').")]
    public static Task<CommandResult> RunScript(string path, int timeoutMs = 300000)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        var escaped = fullPath.Replace("'", "''", StringComparison.Ordinal);
        return RunBatch($"run('{escaped}')", timeoutMs);
    }

    [McpServerTool]
    [Description("Evaluate MATLAB code through the Windows COM Automation server.")]
    public static object EvalCommand(string command)
    {
        var matlab = Com.GetOrCreate(Environment.GetEnvironmentVariable("MATLAB_COM_PROGID") ?? "Matlab.Application");
        var output = Com.Invoke(matlab, "Execute", command);
        return new { command, output = output?.ToString() ?? "" };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return variables from the active MATLAB COM workspace using whos.")]
    public static object ListWorkspace()
    {
        var matlab = Com.GetOrCreate(Environment.GetEnvironmentVariable("MATLAB_COM_PROGID") ?? "Matlab.Application");
        var output = Com.Invoke(matlab, "Execute", "whos")?.ToString() ?? "";
        return new { output };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Initialize the configured official MathWorks MATLAB MCP server over stdio and return its initialize result.")]
    public static Task<JsonNode?> OfficialMcpInitialize(int timeoutMs = 30000) =>
        OfficialMcp.Invoke("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "mcp-matlab-http-bridge", ["version"] = "1.0" }
        }, timeoutMs);

    [McpServerTool(ReadOnly = true)]
    [Description("List tools exposed by the official MathWorks MATLAB MCP server over stdio.")]
    public static Task<JsonNode?> OfficialMcpToolsList(int timeoutMs = 60000) =>
        OfficialMcp.InvokeAfterInitialize("tools/list", new JsonObject(), timeoutMs);

    [McpServerTool]
    [Description("Call a tool exposed by the official MathWorks MATLAB MCP server over stdio.")]
    public static Task<JsonNode?> OfficialMcpToolCall(string name, Dictionary<string, object?>? arguments = null, int timeoutMs = 300000)
    {
        var args = JsonSerializer.SerializeToNode(arguments ?? new Dictionary<string, object?>()) as JsonObject ?? new JsonObject();
        return OfficialMcp.InvokeAfterInitialize("tools/call", new JsonObject { ["name"] = name, ["arguments"] = args }, timeoutMs);
    }

    [McpServerTool]
    [Description("Send a raw JSON-RPC method and params object to the official MathWorks MATLAB MCP server over stdio.")]
    public static Task<JsonNode?> OfficialMcpRawRequest(string method, string paramsJson = "{}", int timeoutMs = 300000)
    {
        var parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson) as JsonObject ?? new JsonObject();
        return OfficialMcp.InvokeAfterInitialize(method, parsed, timeoutMs);
    }

    [McpServerTool]
    [Description("Load a Simulink model or system using MATLAB batch mode.")]
    public static Task<CommandResult> SimulinkLoadSystem(string modelOrPath, int timeoutMs = 300000) =>
        RunBatch($"load_system('{EscapeMatlab(modelOrPath)}'); disp('loaded');", timeoutMs);

    [McpServerTool(ReadOnly = true)]
    [Description("Run Simulink find_system and print JSON-encoded results.")]
    public static Task<CommandResult> SimulinkFindSystem(string system, string? blockType = null, int timeoutMs = 300000)
    {
        var args = string.IsNullOrWhiteSpace(blockType)
            ? $"find_system('{EscapeMatlab(system)}')"
            : $"find_system('{EscapeMatlab(system)}','BlockType','{EscapeMatlab(blockType)}')";
        return RunBatch($"load_system('{EscapeMatlab(system)}'); r={args}; disp(jsonencode(r));", timeoutMs);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get a MATLAB or Simulink parameter value and print it as JSON when possible.")]
    public static Task<CommandResult> GetParam(string target, string parameter, int timeoutMs = 300000) =>
        RunBatch($"v=get_param('{EscapeMatlab(target)}','{EscapeMatlab(parameter)}'); disp(jsonencode(v));", timeoutMs);

    [McpServerTool]
    [Description("Set a MATLAB or Simulink parameter value.")]
    public static Task<CommandResult> SetParam(string target, string parameter, string value, int timeoutMs = 300000)
    {
        Guard.RequireWrites();
        return RunBatch($"set_param('{EscapeMatlab(target)}','{EscapeMatlab(parameter)}','{EscapeMatlab(value)}'); disp('updated');", timeoutMs);
    }

    [McpServerTool]
    [Description("Run Simulink sim(model) with an optional stop time.")]
    public static Task<CommandResult> SimulinkSimulate(string model, double? stopTime = null, int timeoutMs = 600000)
    {
        var command = new StringBuilder();
        command.Append($"load_system('{EscapeMatlab(model)}'); ");
        if (stopTime is not null)
            command.Append($"set_param('{EscapeMatlab(model)}','StopTime','{stopTime.Value}'); ");
        command.Append($"out=sim('{EscapeMatlab(model)}'); disp('simulation complete');");
        return RunBatch(command.ToString(), timeoutMs);
    }

    [McpServerTool]
    [Description("Run slbuild for a Simulink model or subsystem.")]
    public static Task<CommandResult> SimulinkBuild(string target, int timeoutMs = 1200000)
    {
        Guard.RequireWrites();
        return RunBatch($"slbuild('{EscapeMatlab(target)}'); disp('build complete');", timeoutMs);
    }

    private static string EscapeMatlab(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

internal static class MatlabDetection
{
    public static object Detect()
    {
        var candidates = CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new { path = p, exists = File.Exists(p) })
            .ToArray();
        return new { resolved = ResolveMatlabExe(), candidates };
    }

    public static string? ResolveMatlabExe()
    {
        var configured = Environment.GetEnvironmentVariable("MATLAB_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        return CandidatePaths().FirstOrDefault(File.Exists) ?? FindOnPath("matlab.exe") ?? FindOnPath("matlab");
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var configured = Environment.GetEnvironmentVariable("MATLAB_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
        var root = Environment.GetEnvironmentVariable("MATLAB_ROOT");
        if (!string.IsNullOrWhiteSpace(root)) yield return Path.Combine(root, "bin", "matlab.exe");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var mathworks = Path.Combine(programFiles, "MATLAB");
            if (Directory.Exists(mathworks))
            {
                foreach (var dir in Directory.EnumerateDirectories(mathworks).OrderByDescending(x => x))
                    yield return Path.Combine(dir, "bin", "matlab.exe");
            }
        }
    }

    private static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(dir.Trim(), name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

internal static class Guard
{
    public static string[] AllowedRoots => (Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS") ?? "C:\\")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_MATLAB_WRITES"), "false", StringComparison.OrdinalIgnoreCase);
    public static void RequireWrites()
    {
        if (!WritesEnabled) throw new UnauthorizedAccessException("MCP_ENABLE_MATLAB_WRITES=false blocks this tool.");
    }

    public static string RequireAllowedFile(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(full)) throw new FileNotFoundException("File not found.", full);
        if (!AllowedRoots.Any(root => full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {full}");
        return full;
    }
}

internal static class OfficialMcp
{
    private static int _id;
    public static string[] ConfiguredArgs => (Environment.GetEnvironmentVariable("MATLAB_MCP_CORE_SERVER_ARGS") ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static async Task<JsonNode?> InvokeAfterInitialize(string method, JsonObject parameters, int timeoutMs)
    {
        var path = RequiredPath();
        using var process = Start(path);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var initId = NextId();
            await WriteRequest(process, initId, "initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "mcp-matlab-http-bridge", ["version"] = "1.0" }
            }, cts.Token).ConfigureAwait(false);
            _ = await ReadResponse(process, initId, cts.Token).ConfigureAwait(false);
            await WriteNotification(process, "notifications/initialized", new JsonObject(), cts.Token).ConfigureAwait(false);

            var id = NextId();
            await WriteRequest(process, id, method, parameters, cts.Token).ConfigureAwait(false);
            return await ReadResponse(process, id, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            Stop(process);
        }
    }

    public static async Task<JsonNode?> Invoke(string method, JsonObject parameters, int timeoutMs)
    {
        var path = RequiredPath();
        using var process = Start(path);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var id = NextId();
            await WriteRequest(process, id, method, parameters, cts.Token).ConfigureAwait(false);
            return await ReadResponse(process, id, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            Stop(process);
        }
    }

    private static string RequiredPath()
    {
        var path = Environment.GetEnvironmentVariable("MATLAB_MCP_CORE_SERVER_PATH");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("MATLAB_MCP_CORE_SERVER_PATH is not configured.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Official MATLAB MCP server was not found.", path);
        return path;
    }

    private static Process Start(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var fileName = path;
        var leadingArgs = new List<string>();
        if (extension == ".ps1")
        {
            fileName = Environment.GetEnvironmentVariable("POWERSHELL_EXE_PATH") ?? "powershell";
            leadingArgs.AddRange(["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path]);
        }
        else if (extension is ".cmd" or ".bat")
        {
            fileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            leadingArgs.AddRange(["/c", path]);
        }

        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        foreach (var arg in leadingArgs) psi.ArgumentList.Add(arg);
        foreach (var arg in ConfiguredArgs) psi.ArgumentList.Add(arg);
        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start official MATLAB MCP server: {path}");
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"Unable to start official MATLAB MCP server: {path}. Verify the bundled command/script exists and is executable. {ex.Message}", path, ex);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static int NextId() => Interlocked.Increment(ref _id);

    private static Task WriteRequest(Process process, int id, string method, JsonObject parameters, CancellationToken token)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        return WriteLine(process, request, token);
    }

    private static Task WriteNotification(Process process, string method, JsonObject parameters, CancellationToken token)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters
        };
        return WriteLine(process, request, token);
    }

    private static async Task WriteLine(Process process, JsonObject payload, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(payload.ToJsonString().AsMemory(), token).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<JsonNode?> ReadResponse(Process process, int id, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null) break;
            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch { continue; }
            if ((int?)node?["id"] == id)
                return node;
        }
        var stderr = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
        throw new TimeoutException($"No JSON-RPC response with id {id}. stderr: {stderr}");
    }
}

internal static class Com
{
    public static bool IsRegistered(string progId, out string? error)
    {
        try { _ = GetTypeFromProgId(progId); error = null; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static object GetOrCreate(string progId)
    {
        if (TryGetActive(progId, out var activeApp, out _)) return activeApp;
        return Create(progId);
    }

    public static bool TryGetActive(string progId, out object app, out string? error)
    {
        app = null!;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("COM automation requires Windows.");
            var clsIdResult = CLSIDFromProgID(progId, out var clsId);
            if (clsIdResult != 0) Marshal.ThrowExceptionForHR(clsIdResult);

            var activeResult = GetActiveObject(ref clsId, IntPtr.Zero, out var activeObject);
            if (activeResult != 0) Marshal.ThrowExceptionForHR(activeResult);
            app = activeObject ?? throw new InvalidOperationException($"Active COM object is null: {progId}");
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static object Create(string progId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("COM automation requires Windows.");
        var type = GetTypeFromProgId(progId);
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Unable to create COM object: {progId}");
    }

    private static Type GetTypeFromProgId(string progId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("COM automation requires Windows.");
        return Type.GetTypeFromProgID(progId, throwOnError: false) ?? throw new InvalidOperationException($"COM ProgID not registered: {progId}");
    }

    public static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? activeObject);
}

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);

internal static class CommandRunner
{
    public static async Task<CommandResult> Run(string fileName, string[] args, string workingDirectory, int timeoutMs, int maxOutputBytes)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = StartProcess(fileName, psi);
        var stdoutTask = ReadLimited(process.StandardOutput, maxOutputBytes);
        var stderrTask = ReadLimited(process.StandardError, maxOutputBytes);
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Command timed out after {timeoutMs} ms.");
        }
        return new CommandResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static async Task<string> ReadLimited(StreamReader reader, int maxBytes)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        var count = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (read <= 0) break;
            count += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (count > maxBytes) break;
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }

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
}
