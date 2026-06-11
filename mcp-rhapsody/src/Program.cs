using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8094");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<RhapsodyTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-rhapsody", mode = "windows-host" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class RhapsodyTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return Rhapsody MCP configuration and detected integration status.")]
    public static object Config()
    {
        var detection = RhapsodyDetection.Detect();
        return new
        {
            server = "mcp-rhapsody",
            mode = "windows-host",
            http = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8094",
            allowedDirs = Guard.AllowedRoots,
            writesEnabled = Guard.WritesEnabled,
            cliEnabled = Guard.CliEnabled,
            configured = new
            {
                installDir = Environment.GetEnvironmentVariable("RHAPSODY_INSTALL_DIR"),
                exePath = Environment.GetEnvironmentVariable("RHAPSODY_EXE_PATH"),
                cliPath = Environment.GetEnvironmentVariable("RHAPSODY_CLI_PATH"),
                comProgId = Environment.GetEnvironmentVariable("RHAPSODY_COM_PROGID")
            },
            detected = detection
        };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Detect Rhapsody installations, executable paths, CLI paths, and COM availability hints.")]
    public static object DetectInstallations() => RhapsodyDetection.Detect();

    [McpServerTool(ReadOnly = true)]
    [Description("Inspect a Rhapsody project/model text file without opening Rhapsody.")]
    public static async Task<object> InspectProjectFile(string path, int maxBytes = 1048576)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        var info = new FileInfo(fullPath);
        var bytesToRead = (int)Math.Min(Math.Clamp(maxBytes, 4096, 8 * 1024 * 1024), info.Length);
        var buffer = new byte[bytesToRead];
        await using (var stream = File.OpenRead(fullPath))
            await stream.ReadExactlyAsync(buffer.AsMemory(0, bytesToRead));
        var text = Encoding.UTF8.GetString(buffer);
        var latin1Fallback = text.Contains('\uFFFD') ? Encoding.Latin1.GetString(buffer) : text;
        text = latin1Fallback;
        return new
        {
            path = fullPath,
            exists = true,
            size = info.Length,
            extension = info.Extension,
            possibleName = FirstMatch(text, @"(?im)^\s*(?:name|Name)\s*[=:]\s*[""']?([^""'\r\n]+)"),
            packageCount = Regex.Matches(text, @"(?i)\b(?:package|IPackage)\b").Count,
            classCount = Regex.Matches(text, @"(?i)\b(?:class|IClass)\b").Count,
            statechartCount = Regex.Matches(text, @"(?i)\b(?:statechart|IStatechart)\b").Count,
            sample = text[..Math.Min(text.Length, 4000)]
        };
    }

    [McpServerTool]
    [Description("Run configured Rhapsody CLI with raw arguments.")]
    public static Task<CommandResult> RunRhapsodyCli(string[] args, int timeoutMs = 120000)
    {
        Guard.RequireCli();
        var cli = RhapsodyDetection.ResolveCliPath() ?? throw new FileNotFoundException("Rhapsody CLI was not found. Set RHAPSODY_CLI_PATH.");
        return CommandRunner.Run(cli, args, Environment.CurrentDirectory, Math.Clamp(timeoutMs, 1000, 600000), 4 * 1024 * 1024);
    }

    [McpServerTool]
    [Description("Open a Rhapsody project through the COM Automation API.")]
    public static object OpenProject(string path)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        var app = RhapsodyCom.GetApplication();
        var project = RhapsodyCom.InvokeAny(app, ["openProject"], fullPath) ?? RhapsodyCom.InvokeAny(app, ["activeProject"]);
        return RhapsodyCom.Describe(project, fullPath);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return the active Rhapsody project through the COM Automation API.")]
    public static object CurrentProject()
    {
        var app = RhapsodyCom.GetApplication();
        var project = RhapsodyCom.InvokeAny(app, ["activeProject"]);
        return RhapsodyCom.Describe(project);
    }

    [McpServerTool]
    [Description("Save the active Rhapsody project through the COM Automation API.")]
    public static object SaveProject()
    {
        Guard.RequireWrites();
        var project = RhapsodyCom.ActiveProject();
        RhapsodyCom.InvokeVoidRequired(project, "save");
        return new { saved = true, project = RhapsodyCom.Describe(project) };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List packages in the active Rhapsody project.")]
    public static object[] ListPackages(int limit = 200) => RhapsodyCom.ListByMetaClass(["Package"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("List classes in the active Rhapsody project.")]
    public static object[] ListClasses(int limit = 200) => RhapsodyCom.ListByMetaClass(["Class"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("List interfaces in the active Rhapsody project.")]
    public static object[] ListInterfaces(int limit = 200) => RhapsodyCom.ListByMetaClass(["Interface", "InterfaceBlock"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("List statecharts in the active Rhapsody project.")]
    public static object[] ListStatecharts(int limit = 200) => RhapsodyCom.ListByMetaClass(["Statechart", "StateMachine"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("Find a Rhapsody element by full path/name and metaclass.")]
    public static object GetElement(string nameOrPath, string metaClass = "Class")
    {
        var element = RhapsodyCom.FindElement(nameOrPath, metaClass);
        return RhapsodyCom.Describe(element);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search active Rhapsody project elements by text and optional metaclass.")]
    public static object[] SearchElements(string query, string? metaClass = null, int limit = 100)
    {
        var all = RhapsodyCom.Traverse(RhapsodyCom.ActiveProject(), Math.Clamp(limit * 20, 100, 5000));
        return all
            .Where(e =>
            {
                var itemMeta = RhapsodyCom.StringValue(e, ["getMetaClass", "getUserDefinedMetaClass", "metaClass"]);
                var name = RhapsodyCom.StringValue(e, ["getName", "name"]);
                var fullName = RhapsodyCom.StringValue(e, ["getFullPathName", "getFullName", "fullPathName"]);
                return (string.IsNullOrWhiteSpace(metaClass) || string.Equals(itemMeta, metaClass, StringComparison.OrdinalIgnoreCase)) &&
                       ((name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (fullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            })
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(e => RhapsodyCom.Describe(e))
            .ToArray();
    }

    [McpServerTool]
    [Description("Create a package under a parent Rhapsody element.")]
    public static object CreatePackage(string parentNameOrPath, string name)
    {
        Guard.RequireWrites();
        var parent = RhapsodyCom.ResolveParent(parentNameOrPath, "Package");
        var created = RhapsodyCom.InvokeAny(parent, ["addPackage"], name) ?? RhapsodyCom.InvokeRequired(parent, "addNewAggr", "Package", name);
        return RhapsodyCom.Describe(created);
    }

    [McpServerTool]
    [Description("Create a class under a package or model element.")]
    public static object CreateClass(string packageNameOrPath, string name)
    {
        Guard.RequireWrites();
        var parent = RhapsodyCom.ResolveParent(packageNameOrPath, "Package");
        var created = RhapsodyCom.InvokeAny(parent, ["addClass"], name) ?? RhapsodyCom.InvokeRequired(parent, "addNewAggr", "Class", name);
        return RhapsodyCom.Describe(created);
    }

    [McpServerTool]
    [Description("Create an interface under a package or model element.")]
    public static object CreateInterface(string packageNameOrPath, string name)
    {
        Guard.RequireWrites();
        var parent = RhapsodyCom.ResolveParent(packageNameOrPath, "Package");
        var created = RhapsodyCom.InvokeAny(parent, ["addInterface"], name) ?? RhapsodyCom.InvokeRequired(parent, "addNewAggr", "Interface", name);
        return RhapsodyCom.Describe(created);
    }

    [McpServerTool]
    [Description("Set a Rhapsody element property value through COM Automation.")]
    public static object SetElementProperty(string nameOrPath, string metaClass, string propertyName, string value)
    {
        Guard.RequireWrites();
        var element = RhapsodyCom.FindElement(nameOrPath, metaClass);
        RhapsodyCom.InvokeVoidAny(element, ["setPropertyValue"], propertyName, value);
        return new { updated = true, element = RhapsodyCom.Describe(element), propertyName, value };
    }

    [McpServerTool]
    [Description("Set a Rhapsody element tag value through COM Automation.")]
    public static object SetElementTag(string nameOrPath, string metaClass, string tagName, string value)
    {
        Guard.RequireWrites();
        var element = RhapsodyCom.FindElement(nameOrPath, metaClass);
        RhapsodyCom.InvokeVoidAny(element, ["setTagValue", "setTag"], tagName, value);
        return new { updated = true, element = RhapsodyCom.Describe(element), tagName, value };
    }

    private static string? FirstMatch(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}

internal static class RhapsodyCom
{
    public static object GetApplication()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Rhapsody COM Automation is only available on Windows.");

        var progId = Environment.GetEnvironmentVariable("RHAPSODY_COM_PROGID");
        if (string.IsNullOrWhiteSpace(progId))
            progId = RhapsodyDetection.ResolveComProgId() ?? "rhapsody.Application";

        var type = Type.GetTypeFromProgID(progId, throwOnError: false)
            ?? throw new InvalidOperationException($"Rhapsody COM ProgID is not registered: {progId}. Set RHAPSODY_COM_PROGID if your installation uses a different ProgID.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Failed to create Rhapsody COM application: {progId}");
    }

    public static object ActiveProject()
    {
        var app = GetApplication();
        return InvokeAny(app, ["activeProject"]) ?? throw new InvalidOperationException("No active Rhapsody project. Open a project first.");
    }

    public static object? FindElement(string nameOrPath, string metaClass)
    {
        var project = ActiveProject();
        return InvokeAny(project, ["findElementsByFullName"], nameOrPath, metaClass) ??
               InvokeAny(project, ["findNestedElementRecursive"], nameOrPath, metaClass) ??
               InvokeAny(project, ["findNestedElement"], nameOrPath, metaClass) ??
               throw new InvalidOperationException($"Rhapsody element not found: {nameOrPath} ({metaClass})");
    }

    public static object ResolveParent(string nameOrPath, string metaClass)
    {
        if (string.Equals(nameOrPath, "__active_project__", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nameOrPath, "active_project", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nameOrPath, "/", StringComparison.Ordinal))
        {
            return ActiveProject();
        }

        return FindElement(nameOrPath, metaClass) ?? ActiveProject();
    }

    public static object[] ListByMetaClass(string[] metaClasses, int limit)
    {
        var project = ActiveProject();
        return Traverse(project, Math.Clamp(limit * 20, 100, 5000))
            .Where(e =>
            {
                var meta = StringValue(e, ["getMetaClass", "getUserDefinedMetaClass", "metaClass"]);
                return metaClasses.Any(expected => string.Equals(meta, expected, StringComparison.OrdinalIgnoreCase));
            })
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(e => Describe(e))
            .ToArray();
    }

    public static IEnumerable<object> Traverse(object root, int limit)
    {
        var queue = new Queue<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(root);

        while (queue.Count > 0 && seen.Count < limit)
        {
            var current = queue.Dequeue();
            var key = StringValue(current, ["getGUID", "getGuid", "GUID"]) ?? RuntimeHelpers.GetHashCode(current).ToString();
            if (!seen.Add(key)) continue;
            yield return current;

            var children = InvokeAny(current, ["getNestedElements", "nestedElements"]);
            foreach (var child in EnumerateCollection(children))
                queue.Enqueue(child);
        }
    }

    public static object Describe(object? element, string? requestedPath = null)
    {
        if (element is null) return new { exists = false, requestedPath };
        return new
        {
            exists = true,
            requestedPath,
            name = StringValue(element, ["getName", "name"]),
            fullPath = StringValue(element, ["getFullPathName", "getFullName", "fullPathName"]),
            guid = StringValue(element, ["getGUID", "getGuid", "GUID"]),
            metaClass = StringValue(element, ["getMetaClass", "getUserDefinedMetaClass", "metaClass"]),
            description = StringValue(element, ["getDescription", "description"])
        };
    }

    public static string? StringValue(object target, string[] names)
    {
        foreach (var name in names)
        {
            var value = InvokeAny(target, [name]) ?? GetProperty(target, name);
            if (value is not null) return Convert.ToString(value);
        }
        return null;
    }

    public static object InvokeRequired(object? target, string method, params object?[] args) =>
        InvokeAny(target, [method], args) ?? throw new MissingMethodException($"Rhapsody COM method failed or is unavailable: {method}");

    public static void InvokeVoidRequired(object? target, string method, params object?[] args)
    {
        if (!InvokeVoidAny(target, [method], args))
            throw new MissingMethodException($"Rhapsody COM method failed or is unavailable: {method}");
    }

    public static bool InvokeVoidAny(object? target, string[] methods, params object?[] args)
    {
        if (target is null) return false;
        foreach (var method in methods)
        {
            try
            {
                target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
                return true;
            }
            catch
            {
                // Try the next known API spelling.
            }
        }
        return false;
    }

    public static object? InvokeAny(object? target, string[] methods, params object?[] args)
    {
        if (target is null) return null;
        foreach (var method in methods)
        {
            try
            {
                var result = target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
                if (result is not null) return result;
            }
            catch
            {
                // Try the next known API spelling.
            }
        }
        return null;
    }

    private static object? GetProperty(object target, string property)
    {
        try { return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, []); }
        catch { return null; }
    }

    private static IEnumerable<object> EnumerateCollection(object? collection)
    {
        if (collection is null) yield break;
        if (collection is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                if (item is not null) yield return item;
            yield break;
        }

        var countObj = InvokeAny(collection, ["getCount", "Count", "count"]) ?? GetProperty(collection, "Count");
        if (!int.TryParse(Convert.ToString(countObj), out var count)) yield break;
        for (var i = 1; i <= count; i++)
        {
            var item = InvokeAny(collection, ["getItem", "Item", "item"], i) ?? InvokeAny(collection, ["getModelElement"], i);
            if (item is not null) yield return item;
        }
    }
}

internal static class RhapsodyDetection
{
    public static object Detect()
    {
        var installDirs = FindInstallDirs().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var exePath = ResolveExePath(installDirs);
        var cliPath = ResolveCliPath(installDirs);
        var comProgId = ResolveComProgId();
        return new
        {
            isWindows = OperatingSystem.IsWindows(),
            installDirs,
            exePath,
            cliPath,
            comProgId,
            comAvailable = IsComAvailable(comProgId)
        };
    }

    public static string? ResolveCliPath(string[]? installDirs = null)
    {
        var configured = Environment.GetEnvironmentVariable("RHAPSODY_CLI_PATH");
        if (File.Exists(configured)) return configured;
        return CandidateFiles(installDirs ?? FindInstallDirs().ToArray(), ["rhapsodycl.exe", "RhapsodyCL.exe", "rhapsody.exe", "Rhapsody.exe"]).FirstOrDefault(File.Exists);
    }

    private static string? ResolveExePath(string[] installDirs)
    {
        var configured = Environment.GetEnvironmentVariable("RHAPSODY_EXE_PATH");
        if (File.Exists(configured)) return configured;
        return CandidateFiles(installDirs, ["Rhapsody.exe", "rhapsody.exe"]).FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> FindInstallDirs()
    {
        var configured = Environment.GetEnvironmentVariable("RHAPSODY_INSTALL_DIR");
        if (Directory.Exists(configured)) yield return configured!;

        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var dir in SafeEnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                if (name.Contains("Rhapsody", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("IBM", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Telelogic", StringComparison.OrdinalIgnoreCase))
                {
                    yield return dir;
                    foreach (var child in SafeEnumerateDirectories(dir, "*Rhapsody*", SearchOption.AllDirectories).Take(20))
                        yield return child;
                }
            }
        }
    }

    private static IEnumerable<string> CandidateFiles(string[] installDirs, string[] names)
    {
        foreach (var dir in installDirs)
            foreach (var name in names)
                foreach (var candidate in Directory.Exists(dir) ? SafeEnumerateFiles(dir, name, SearchOption.AllDirectories).Take(20) : Enumerable.Empty<string>())
                    yield return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var name in names)
                yield return Path.Combine(dir, name);
    }

    public static string? ResolveComProgId()
    {
        var configured = Environment.GetEnvironmentVariable("RHAPSODY_COM_PROGID");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        foreach (var candidate in new[] { "Rhapsody.Application", "Rhapsody.Application.1", "rhapsody.Application" })
            if (IsComAvailable(candidate)) return candidate;
        return null;
    }

    private static bool IsComAvailable(string? progId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(progId)) return false;
        try { return Type.GetTypeFromProgID(progId, throwOnError: false) is not null; }
        catch (COMException) { return false; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern, SearchOption option)
    {
        try { return Directory.EnumerateDirectories(path, pattern, option); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption option)
    {
        try { return Directory.EnumerateFiles(path, pattern, option); }
        catch { return []; }
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
    public static readonly string[] AllowedRoots = ParseAllowedRoots();
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_RHAPSODY_WRITES"), "false", StringComparison.OrdinalIgnoreCase);
    public static bool CliEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_RHAPSODY_CLI"), "false", StringComparison.OrdinalIgnoreCase);

    public static void RequireCli()
    {
        if (!CliEnabled)
            throw new UnauthorizedAccessException("Rhapsody CLI tools are disabled because MCP_ENABLE_RHAPSODY_CLI=false.");
    }

    public static void RequireWrites()
    {
        if (!WritesEnabled)
            throw new UnauthorizedAccessException("Rhapsody write tools are disabled because MCP_ENABLE_RHAPSODY_WRITES=false.");
    }

    public static string RequireAllowedFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"File not found: {fullPath}", fullPath);
        if (!AllowedRoots.Any(root => root == Path.GetPathRoot(root) || fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {fullPath}");
        return fullPath;
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        var values = string.IsNullOrWhiteSpace(raw)
            ? [Path.GetPathRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory]
            : raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(NormalizeRoot).ToArray();
    }

    private static string NormalizeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return fullPath == root ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
