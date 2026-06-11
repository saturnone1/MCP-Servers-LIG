using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8096");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<AutoCadTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-autocad", mode = "windows-host" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class AutoCadTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return AutoCAD MCP configuration and COM detection status.")]
    public static object Config()
    {
        var progId = AutoCad.ProgId;
        return new
        {
            server = "mcp-autocad",
            mode = "windows-host",
            lineage = "C# Windows host implementation based on AutoCAD COM automation patterns used by open-source AutoCAD MCP servers.",
            http = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8096",
            allowedDirs = Guard.AllowedRoots,
            writesEnabled = Guard.WritesEnabled,
            progId,
            comAvailable = Com.TryCreate(progId, out var error),
            comError = error
        };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Detect AutoCAD COM ProgID and common install path hints.")]
    public static object DetectInstallations() => new
    {
        progId = AutoCad.ProgId,
        comAvailable = Com.TryCreate(AutoCad.ProgId, out var error),
        comError = error,
        acadExe = Detection.FindOnPath("acad.exe"),
        configuredExe = Environment.GetEnvironmentVariable("AUTOCAD_EXE_PATH")
    };

    [McpServerTool]
    [Description("Open an AutoCAD drawing through COM Automation.")]
    public static object OpenDrawing(string path, bool visible = true)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        var app = AutoCad.Application(visible);
        var documents = Com.Get(app, "Documents");
        var document = Com.Invoke(documents, "Open", fullPath);
        return AutoCad.DescribeDocument(document, fullPath);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return active AutoCAD drawing information.")]
    public static object ActiveDrawing()
    {
        var app = AutoCad.Application(true);
        var document = Com.Get(app, "ActiveDocument");
        return AutoCad.DescribeDocument(document);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List layers in the active AutoCAD drawing.")]
    public static object[] ListLayers(int limit = 500)
    {
        var doc = AutoCad.ActiveDocument();
        var layers = Com.Get(doc, "Layers");
        return AutoCad.Enumerate(layers, Math.Clamp(limit, 1, 5000))
            .Select(layer => new
            {
                name = Com.GetString(layer, "Name"),
                color = Com.GetSafe(layer, "Color")?.ToString(),
                lineType = Com.GetSafe(layer, "Linetype")?.ToString(),
                freeze = Com.GetSafe(layer, "Freeze")?.ToString(),
                lockState = Com.GetSafe(layer, "Lock")?.ToString()
            })
            .ToArray<object>();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List entities in model space of the active AutoCAD drawing.")]
    public static object[] ListModelSpaceEntities(int limit = 500)
    {
        var doc = AutoCad.ActiveDocument();
        var modelSpace = Com.Get(doc, "ModelSpace");
        return AutoCad.Enumerate(modelSpace, Math.Clamp(limit, 1, 5000))
            .Select(entity => new
            {
                objectName = Com.GetString(entity, "ObjectName"),
                handle = Com.GetString(entity, "Handle"),
                layer = Com.GetString(entity, "Layer")
            })
            .ToArray<object>();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List block definitions in the active AutoCAD drawing.")]
    public static object[] ListBlocks(int limit = 500)
    {
        var blocks = Com.Get(AutoCad.ActiveDocument(), "Blocks");
        return AutoCad.Enumerate(blocks, Math.Clamp(limit, 1, 5000))
            .Select(block => new
            {
                name = Com.GetString(block, "Name"),
                isLayout = Com.GetSafe(block, "IsLayout")?.ToString(),
                count = Com.GetSafe(block, "Count")?.ToString()
            })
            .ToArray<object>();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List block references inserted in model space.")]
    public static object[] ListBlockReferences(int limit = 500) =>
        AutoCad.ListEntitiesByObjectName(["AcDbBlockReference"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("List text and mtext entities in model space.")]
    public static object[] ListTexts(int limit = 500) =>
        AutoCad.ListEntitiesByObjectName(["AcDbText", "AcDbMText"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("List dimension entities in model space.")]
    public static object[] ListDimensions(int limit = 500) =>
        AutoCad.ListEntitiesByObjectName(["AcDbDimension", "AcDbAlignedDimension", "AcDbRotatedDimension"], limit);

    [McpServerTool(ReadOnly = true)]
    [Description("List line and polyline entities in model space.")]
    public static object[] ListCurves(int limit = 500) =>
        AutoCad.ListEntitiesByObjectName(["AcDbLine", "AcDbPolyline", "AcDb2dPolyline", "AcDb3dPolyline"], limit);

    [McpServerTool]
    [Description("Run an AutoCAD command string through SendCommand.")]
    public static object RunCommand(string command)
    {
        Guard.RequireWrites();
        var doc = AutoCad.ActiveDocument();
        Com.Invoke(doc, "SendCommand", command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n");
        return new { sent = true, command };
    }

    [McpServerTool]
    [Description("Create an AutoCAD layer in the active drawing.")]
    public static object CreateLayer(string name)
    {
        Guard.RequireWrites();
        var layers = Com.Get(AutoCad.ActiveDocument(), "Layers");
        var layer = Com.Invoke(layers, "Add", name);
        return new { created = true, name = Com.GetString(layer, "Name") ?? name };
    }

    [McpServerTool]
    [Description("Add a line to model space using two XYZ points.")]
    public static object AddLine(double[] start, double[] end)
    {
        Guard.RequireWrites();
        if (start.Length != 3 || end.Length != 3) throw new ArgumentException("start and end must be XYZ arrays.");
        var modelSpace = Com.Get(AutoCad.ActiveDocument(), "ModelSpace");
        var line = Com.Invoke(modelSpace, "AddLine", start, end);
        return new { created = true, handle = Com.GetString(line, "Handle"), objectName = Com.GetString(line, "ObjectName") };
    }

    [McpServerTool]
    [Description("Add a circle to model space using center XYZ and radius.")]
    public static object AddCircle(double[] center, double radius)
    {
        Guard.RequireWrites();
        if (center.Length != 3) throw new ArgumentException("center must be an XYZ array.");
        var modelSpace = Com.Get(AutoCad.ActiveDocument(), "ModelSpace");
        var circle = Com.Invoke(modelSpace, "AddCircle", center, radius);
        return AutoCad.DescribeEntity(circle);
    }

    [McpServerTool]
    [Description("Add single-line text to model space.")]
    public static object AddText(string text, double[] insertionPoint, double height = 2.5)
    {
        Guard.RequireWrites();
        if (insertionPoint.Length != 3) throw new ArgumentException("insertionPoint must be an XYZ array.");
        var modelSpace = Com.Get(AutoCad.ActiveDocument(), "ModelSpace");
        var entity = Com.Invoke(modelSpace, "AddText", text, insertionPoint, height);
        return AutoCad.DescribeEntity(entity);
    }

    [McpServerTool]
    [Description("Save the active AutoCAD drawing.")]
    public static object SaveDrawing()
    {
        Guard.RequireWrites();
        var doc = AutoCad.ActiveDocument();
        Com.Invoke(doc, "Save");
        return AutoCad.DescribeDocument(doc);
    }

    [McpServerTool]
    [Description("Export the active drawing using AutoCAD Document.Export. Common formats include DXF, WMF, SAT, EPS, BMP, and PDF where supported.")]
    public static object ExportDrawing(string outputPath, string format = "PDF")
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedOutput(outputPath);
        var doc = AutoCad.ActiveDocument();
        var basePath = Path.Combine(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath));
        Com.Invoke(doc, "Export", basePath, format, null);
        return new { exported = true, requestedPath = fullPath, basePath, format };
    }

    [McpServerTool]
    [Description("Save a copy of the active drawing to a target path using SaveAs.")]
    public static object SaveAsDrawing(string outputPath)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedOutput(outputPath);
        var doc = AutoCad.ActiveDocument();
        Com.Invoke(doc, "SaveAs", fullPath);
        return new { saved = true, path = fullPath };
    }
}

internal static class AutoCad
{
    public static string ProgId => Environment.GetEnvironmentVariable("AUTOCAD_COM_PROGID") ?? "AutoCAD.Application";

    public static object Application(bool visible)
    {
        var app = Com.Create(ProgId);
        Com.SetSafe(app, "Visible", visible);
        return app;
    }

    public static object ActiveDocument() => Com.Get(Application(true), "ActiveDocument");

    public static object DescribeDocument(object? doc, string? openedPath = null) => new
    {
        path = openedPath ?? Com.GetString(doc, "FullName"),
        name = Com.GetString(doc, "Name"),
        activeLayer = Com.GetString(Com.GetSafe(doc, "ActiveLayer"), "Name")
    };

    public static IEnumerable<object> Enumerate(object collection, int limit)
    {
        var count = Convert.ToInt32(Com.GetSafe(collection, "Count") ?? 0);
        for (var i = 0; i < Math.Min(count, limit); i++)
        {
            object? item = null;
            try { item = Com.Invoke(collection, "Item", i); }
            catch { try { item = Com.Invoke(collection, "Item", i + 1); } catch { } }
            if (item is not null) yield return item;
        }
    }

    public static object[] ListEntitiesByObjectName(string[] objectNames, int limit)
    {
        var modelSpace = Com.Get(ActiveDocument(), "ModelSpace");
        return Enumerate(modelSpace, Math.Clamp(limit, 1, 5000))
            .Where(entity =>
            {
                var objectName = Com.GetString(entity, "ObjectName") ?? "";
                return objectNames.Any(name => objectName.Contains(name, StringComparison.OrdinalIgnoreCase));
            })
            .Select(DescribeEntity)
            .ToArray();
    }

    public static object DescribeEntity(object? entity) => new
    {
        objectName = Com.GetString(entity, "ObjectName"),
        handle = Com.GetString(entity, "Handle"),
        layer = Com.GetString(entity, "Layer"),
        text = Com.GetString(entity, "TextString"),
        insertionPoint = Com.GetSafe(entity, "InsertionPoint")?.ToString(),
        length = Com.GetSafe(entity, "Length")?.ToString(),
        area = Com.GetSafe(entity, "Area")?.ToString(),
        radius = Com.GetSafe(entity, "Radius")?.ToString(),
        blockName = Com.GetString(entity, "Name")
    };
}

internal static class Guard
{
    public static string[] AllowedRoots => (Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS") ?? "C:\\")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_AUTOCAD_WRITES"), "false", StringComparison.OrdinalIgnoreCase);
    public static void RequireWrites()
    {
        if (!WritesEnabled) throw new UnauthorizedAccessException("MCP_ENABLE_AUTOCAD_WRITES=false blocks this tool.");
    }
    public static string RequireAllowedFile(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(full)) throw new FileNotFoundException("File not found.", full);
        if (!AllowedRoots.Any(root => full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {full}");
        return full;
    }
    public static string RequireAllowedOutput(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var dir = Path.GetDirectoryName(full) ?? throw new ArgumentException("Output path must include a directory.");
        Directory.CreateDirectory(dir);
        if (!AllowedRoots.Any(root => full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {full}");
        return full;
    }
}

internal static class Com
{
    public static bool TryCreate(string progId, out string? error)
    {
        try { _ = Create(progId); error = null; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static object Create(string progId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("COM automation requires Windows.");
        var type = Type.GetTypeFromProgID(progId, throwOnError: false) ?? throw new InvalidOperationException($"COM ProgID not registered: {progId}");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Unable to create COM object: {progId}");
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

    public static object Get(object? target, string property)
    {
        if (target is null) throw new InvalidOperationException($"COM target is null while reading {property}.");
        return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null) ?? "";
    }

    public static string? GetString(object? target, string property) => GetSafe(target, property)?.ToString();

    public static void SetSafe(object? target, string property, object? value)
    {
        try { target?.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, [value]); } catch { }
    }
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
