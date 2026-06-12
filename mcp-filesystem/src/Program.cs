using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<FilesystemTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-filesystem" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class FilesystemTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("List the absolute directories this server is allowed to access.")]
    public static string[] ListAllowedDirectories() => Guard.AllowedRoots;

    [McpServerTool(ReadOnly = true)]
    [Description("Read a UTF-8 text file from an allowed directory.")]
    public static async Task<string> ReadFile(string path, int maxBytes = 1048576)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        await using var stream = File.OpenRead(fullPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[Math.Min(maxBytes, 1048576)];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Read multiple UTF-8 text files from allowed directories.")]
    public static async Task<Dictionary<string, string>> ReadMultipleFiles(string[] paths, int maxBytesPerFile = 1048576)
    {
        var result = new Dictionary<string, string>();
        foreach (var path in paths)
            result[path] = await ReadFile(path, maxBytesPerFile);
        return result;
    }

    [McpServerTool]
    [Description("Create or overwrite a UTF-8 text file.")]
    public static async Task<object> WriteFile(string path, string content)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
        return Info(fullPath);
    }

    [McpServerTool]
    [Description("Copy a file or directory.")]
    public static object Copy(string sourcePath, string destinationPath, bool overwrite = false)
    {
        Guard.RequireWrites();
        var source = Guard.RequireAllowedPath(sourcePath);
        var destination = Guard.RequireAllowedPath(destinationPath);
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite);
            return Info(destination);
        }
        if (!Directory.Exists(source))
            throw new FileNotFoundException("Source does not exist.", source);
        CopyDirectory(source, destination, overwrite);
        return Info(destination);
    }

    [McpServerTool]
    [Description("Move a file or directory.")]
    public static object Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        Guard.RequireWrites();
        var source = Guard.RequireAllowedPath(sourcePath);
        var destination = Guard.RequireAllowedPath(destinationPath);
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite);
            return Info(destination);
        }
        if (!Directory.Exists(source))
            throw new FileNotFoundException("Source does not exist.", source);
        if (overwrite && Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Directory.Move(source, destination);
        return Info(destination);
    }

    [McpServerTool]
    [Description("Delete a file or directory.")]
    public static object Delete(string path, bool recursive = false)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedPath(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive);
        else
            throw new FileNotFoundException("Path does not exist.", fullPath);
        return new { deleted = fullPath };
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return file or directory metadata.")]
    public static object Stat(string path) => Info(Guard.RequireAllowedPath(path));

    [McpServerTool(ReadOnly = true)]
    [Description("List a directory under an allowed root.")]
    public static object[] ListDirectory(string path = ".", string pattern = "*", bool recursive = false, int limit = 200)
    {
        var fullPath = Guard.RequireAllowedDirectory(path);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFileSystemEntries(fullPath, pattern, option)
            .Take(Math.Clamp(limit, 1, 2000))
            .Select(Info)
            .ToArray();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search file names under an allowed root using a regular expression.")]
    public static object[] Search(string path, string regex, int limit = 100)
    {
        var fullPath = Guard.RequireAllowedDirectory(path);
        var matcher = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        return Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
            .Where(p => matcher.IsMatch(Path.GetFileName(p)))
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(Info)
            .ToArray();
    }

    private static object Info(string path)
    {
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            return new { path = file.FullName, type = "file", file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc };
        }
        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            return new { path = dir.FullName, type = "directory", dir.LastWriteTimeUtc, dir.CreationTimeUtc };
        }
        return new { path, type = "missing" };
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), overwrite);
    }
}

internal static class Guard
{
    public static string[] AllowedRoots { get; } = ParseAllowedRoots();

    public static string RequireAllowedFile(string path)
    {
        var fullPath = RequireAllowedPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File does not exist.", fullPath);
        return fullPath;
    }

    public static string RequireAllowedDirectory(string path)
    {
        var fullPath = RequireAllowedPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    public static string RequireAllowedPath(string path)
    {
        var fullPath = Path.GetFullPath(TranslateHostPath(path));
        var finalPath = File.Exists(fullPath) || Directory.Exists(fullPath)
            ? Path.GetFullPath(new FileInfo(fullPath).FullName)
            : fullPath;
        if (!AllowedRoots.Any(root => IsInside(finalPath, root)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {finalPath}");
        return finalPath;
    }

    public static void RequireWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Writes are disabled because MCP_ENABLE_WRITES=false.");
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
