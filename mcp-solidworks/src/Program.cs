using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:42197");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<SolidWorksTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-solidworks", mode = "windows-host" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class SolidWorksTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return SolidWorks MCP configuration and COM detection status.")]
    public static object Config()
    {
        var progId = SolidWorks.ProgId;
        var comRegistered = Com.IsRegistered(progId, out var registrationError);
        var activeComAvailable = Com.TryGetActive(progId, out _, out var activeError);
        return new
        {
            server = "mcp-solidworks",
            mode = "windows-host",
            lineage = "C# Windows host implementation based on open-source SolidWorks MCP COM automation patterns.",
            http = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:42197",
            allowedDirs = Guard.AllowedRoots,
            writesEnabled = Guard.WritesEnabled,
            progId,
            comAvailable = comRegistered,
            comRegistered,
            activeComAvailable,
            comError = registrationError ?? activeError
        };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Detect SolidWorks COM ProgID and executable hints.")]
    public static object DetectInstallations()
    {
        var progId = SolidWorks.ProgId;
        var comRegistered = Com.IsRegistered(progId, out var registrationError);
        var activeComAvailable = Com.TryGetActive(progId, out _, out var activeError);
        return new
        {
            progId,
            comAvailable = comRegistered,
            comRegistered,
            activeComAvailable,
            comError = registrationError ?? activeError,
            solidWorksExe = Detection.FindOnPath("SLDWORKS.exe"),
            configuredExe = Environment.GetEnvironmentVariable("SOLIDWORKS_EXE_PATH")
        };
    }

    [McpServerTool]
    [Description("Open a SolidWorks part, assembly, or drawing through COM Automation.")]
    public static object OpenDocument(string path, bool visible = true)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        var app = SolidWorks.Application(visible);
        var docType = SolidWorks.DocumentType(fullPath);
        var errors = 0;
        var warnings = 0;
        var doc = Com.Invoke(app, "OpenDoc6", fullPath, docType, 0, "", errors, warnings);
        return SolidWorks.DescribeDocument(doc, fullPath);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return active SolidWorks document information.")]
    public static object ActiveDocument() => SolidWorks.DescribeDocument(SolidWorks.ActiveDocument());

    [McpServerTool(ReadOnly = true)]
    [Description("List top-level features in the active SolidWorks document.")]
    public static object[] ListFeatures(int limit = 500)
    {
        var doc = SolidWorks.ActiveDocument();
        var feature = Com.Invoke(doc, "FirstFeature");
        var items = new List<object>();
        while (feature is not null && items.Count < Math.Clamp(limit, 1, 100000))
        {
            items.Add(new
            {
                name = Com.GetString(feature, "Name"),
                typeName = Com.Invoke(feature, "GetTypeName2")?.ToString() ?? Com.Invoke(feature, "GetTypeName")?.ToString()
            });
            feature = Com.Invoke(feature, "GetNextFeature");
        }
        return items.ToArray();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List assembly components in the active SolidWorks assembly.")]
    public static object[] ListComponents(int limit = 500)
    {
        var doc = SolidWorks.ActiveDocument();
        var config = Com.Invoke(doc, "GetActiveConfiguration");
        var root = Com.Invoke(config, "GetRootComponent3", true);
        var components = new List<object>();
        WalkComponent(root, components, Math.Clamp(limit, 1, 100000));
        return components.ToArray();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List configurations in the active SolidWorks document.")]
    public static object[] ListConfigurations()
    {
        var doc = SolidWorks.ActiveDocument();
        var names = Com.Invoke(doc, "GetConfigurationNames") as Array;
        if (names is null) return [];
        return names.Cast<object>().Select(name => new { name = name.ToString() }).ToArray<object>();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List equations in the active SolidWorks document.")]
    public static object[] ListEquations(int limit = 500)
    {
        var doc = SolidWorks.ActiveDocument();
        var manager = Com.Get(doc, "EquationMgr");
        var count = Convert.ToInt32(Com.GetSafe(manager, "GetCount") ?? Com.Invoke(manager, "GetCount") ?? 0);
        var items = new List<object>();
        for (var i = 0; i < Math.Min(count, Math.Clamp(limit, 1, 100000)); i++)
        {
            items.Add(new { index = i, equation = Com.Invoke(manager, "Equation", i)?.ToString() ?? Com.GetString(manager, "Equation") });
        }
        return items.ToArray();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List custom properties from the active document or a named configuration.")]
    public static object[] ListCustomProperties(string configuration = "")
    {
        var manager = SolidWorks.CustomPropertyManager(configuration);
        var names = Com.Invoke(manager, "GetNames") as Array;
        if (names is null) return [];
        return names.Cast<object>().Select(name =>
        {
            var key = name.ToString() ?? "";
            object? value = null;
            object? resolved = null;
            try { value = Com.Invoke(manager, "Get", key); } catch { }
            try { resolved = Com.Invoke(manager, "Get2", key, "", ""); } catch { }
            return new { name = key, value = value?.ToString(), resolved = resolved?.ToString() };
        }).ToArray<object>();
    }

    [McpServerTool]
    [Description("Set or add a custom property on the active document or a named configuration.")]
    public static object SetCustomProperty(string name, string value, string configuration = "")
    {
        Guard.RequireWrites();
        var manager = SolidWorks.CustomPropertyManager(configuration);
        var result = Com.Invoke(manager, "Add3", name, 30, value, 2) ?? Com.Invoke(manager, "Set2", name, value);
        return new { updated = true, name, value, configuration, result = result?.ToString() };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return mass properties for the active SolidWorks document when available.")]
    public static object GetMassProperties()
    {
        var doc = SolidWorks.ActiveDocument();
        var extension = Com.Get(doc, "Extension");
        var massProps = Com.Invoke(extension, "CreateMassProperty");
        return new
        {
            mass = Com.GetSafe(massProps, "Mass")?.ToString(),
            volume = Com.GetSafe(massProps, "Volume")?.ToString(),
            surfaceArea = Com.GetSafe(massProps, "SurfaceArea")?.ToString(),
            centerOfMass = Com.GetSafe(massProps, "CenterOfMass")?.ToString()
        };
    }

    [McpServerTool]
    [Description("Rebuild the active SolidWorks document.")]
    public static object RebuildModel()
    {
        Guard.RequireWrites();
        var doc = SolidWorks.ActiveDocument();
        var result = Com.Invoke(doc, "ForceRebuild3", false);
        return new { rebuilt = true, result = result?.ToString() };
    }

    [McpServerTool]
    [Description("Save the active SolidWorks document.")]
    public static object SaveDocument()
    {
        Guard.RequireWrites();
        var doc = SolidWorks.ActiveDocument();
        var result = Com.Invoke(doc, "Save3", 1, 0, 0);
        return new { saved = true, result = result?.ToString(), document = SolidWorks.DescribeDocument(doc) };
    }

    [McpServerTool]
    [Description("Export the active SolidWorks document to a target path such as STEP, IGES, STL, PDF, or native format supported by SaveAs.")]
    public static object ExportDocument(string outputPath)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedOutput(outputPath);
        var doc = SolidWorks.ActiveDocument();
        var result = Com.Invoke(doc, "SaveAs3", fullPath, 0, 0);
        return new { exported = true, path = fullPath, result = result?.ToString() };
    }

    [McpServerTool]
    [Description("Export the active SolidWorks document as STEP.")]
    public static object ExportStep(string outputPath)
    {
        var path = EnsureExtension(outputPath, ".step");
        return ExportDocument(path);
    }

    [McpServerTool]
    [Description("Export the active SolidWorks document as STL.")]
    public static object ExportStl(string outputPath)
    {
        var path = EnsureExtension(outputPath, ".stl");
        return ExportDocument(path);
    }

    [McpServerTool]
    [Description("Export the active SolidWorks document as PDF.")]
    public static object ExportPdf(string outputPath)
    {
        var path = EnsureExtension(outputPath, ".pdf");
        return ExportDocument(path);
    }

    [McpServerTool]
    [Description("Close the active SolidWorks document.")]
    public static object CloseActiveDocument()
    {
        Guard.RequireWrites();
        var app = SolidWorks.Application(true);
        var doc = SolidWorks.ActiveDocument();
        var title = Com.Invoke(doc, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
            Com.Invoke(app, "CloseDoc", title);
        return new { closed = true, title };
    }

    private static string EnsureExtension(string outputPath, string extension)
    {
        var current = Path.GetExtension(outputPath);
        return string.Equals(current, extension, StringComparison.OrdinalIgnoreCase) ? outputPath : outputPath + extension;
    }

    private static void WalkComponent(object? component, List<object> output, int limit)
    {
        if (component is null || output.Count >= limit) return;
        output.Add(new
        {
            name = Com.GetString(component, "Name2") ?? Com.GetString(component, "Name"),
            path = Com.Invoke(component, "GetPathName")?.ToString(),
            suppressed = Com.Invoke(component, "IsSuppressed")?.ToString()
        });
        var children = Com.Invoke(component, "GetChildren") as Array;
        if (children is null) return;
        foreach (var child in children)
        {
            if (output.Count >= limit) break;
            WalkComponent(child, output, limit);
        }
    }
}

internal static class SolidWorks
{
    public static string ProgId => Environment.GetEnvironmentVariable("SOLIDWORKS_COM_PROGID") ?? "SldWorks.Application";

    public static object Application(bool visible)
    {
        var app = Com.GetOrCreate(ProgId);
        Com.SetSafe(app, "Visible", visible);
        return app;
    }

    public static object ActiveDocument()
    {
        var doc = Com.Get(Application(true), "ActiveDoc");
        if (doc is null) throw new InvalidOperationException("No active SolidWorks document.");
        return doc;
    }

    public static int DocumentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".sldprt" => 1,
        ".sldasm" => 2,
        ".slddrw" => 3,
        _ => 0
    };

    public static object DescribeDocument(object? doc, string? openedPath = null) => new
    {
        path = openedPath ?? Com.Invoke(doc, "GetPathName")?.ToString(),
        title = Com.Invoke(doc, "GetTitle")?.ToString(),
        type = Com.Invoke(doc, "GetType")?.ToString()
    };

    public static object CustomPropertyManager(string configuration)
    {
        var extension = Com.Get(ActiveDocument(), "Extension");
        return Com.Invoke(extension, "CustomPropertyManager", configuration) ?? throw new InvalidOperationException("CustomPropertyManager is not available.");
    }
}

internal static class Guard
{
    public static string[] AllowedRoots { get; } = ParseAllowedRoots();
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_SOLIDWORKS_WRITES"), "false", StringComparison.OrdinalIgnoreCase);
    public static void RequireWrites()
    {
        if (!WritesEnabled) throw new UnauthorizedAccessException("MCP_ENABLE_SOLIDWORKS_WRITES=false blocks this tool.");
    }
    public static string RequireAllowedFile(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(full)) throw new FileNotFoundException("File not found.", full);
        if (!AllowedRoots.Any(root => IsInside(full, Path.GetFullPath(root))))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {full}");
        return full;
    }
    public static string RequireAllowedOutput(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var dir = Path.GetDirectoryName(full) ?? throw new ArgumentException("Output path must include a directory.");
        Directory.CreateDirectory(dir);
        if (!AllowedRoots.Any(root => IsInside(full, Path.GetFullPath(root))))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {full}");
        return full;
    }

    private static bool IsInside(string path, string root)
    {
        var normalizedRoot = root == Path.GetPathRoot(root)
            ? root
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalizedRoot == Path.GetPathRoot(normalizedRoot))
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
            return OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName).ToArray()
                : ["/"];
        return raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    public static object? Invoke(object? target, string method, params object?[] args)
    {
        if (target is null) return null;
        return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
    }

    public static object? GetSafe(object? target, string property)
    {
        try { return Get(target, property); } catch { return null; }
    }

    public static object? Get(object? target, string property)
    {
        if (target is null) return null;
        return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
    }

    public static string? GetString(object? target, string property) => GetSafe(target, property)?.ToString();

    public static void SetSafe(object? target, string property, object? value)
    {
        try { target?.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, [value]); } catch { }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? activeObject);
}

internal static class Detection
{
    public static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(dir.Trim(), name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

