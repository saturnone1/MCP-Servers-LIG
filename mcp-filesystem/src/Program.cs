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
    public static async Task<string> ReadFile(string path, int maxBytes = 16777216)
    {
        var fullPath = Guard.RequireAllowedFile(path);
        await using var stream = File.OpenRead(fullPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[Math.Clamp(maxBytes, 1, 67108864)];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Read multiple UTF-8 text files from allowed directories.")]
    public static async Task<Dictionary<string, string>> ReadMultipleFiles(string[] paths, int maxBytesPerFile = 16777216)
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
    [Description("Append UTF-8 text to a file. Creates the file if it does not exist. Only the new fragment is sent, avoiding whole-file rewrite.")]
    public static async Task<object> AppendFile(string path, string content)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.AppendAllTextAsync(fullPath, content, Encoding.UTF8);
        return Info(fullPath);
    }

    [McpServerTool]
    [Description("Prepend UTF-8 text to a file. Creates the file if it does not exist.")]
    public static async Task<object> PrependFile(string path, string content)
    {
        Guard.RequireWrites();
        var fullPath = Guard.RequireAllowedPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var existing = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, Encoding.UTF8) : string.Empty;
        await File.WriteAllTextAsync(fullPath, content + existing, Encoding.UTF8);
        return Info(fullPath);
    }

    [McpServerTool]
    [Description("Replace occurrences of a substring in a UTF-8 file. Verifies actual occurrence count matches expectedOccurrences before writing to prevent unintended matches. Set expectedOccurrences to null to skip verification.")]
    public static async Task<object> PatchFile(string path, string find, string replace, int? expectedOccurrences = 1)
    {
        Guard.RequireWrites();
        if (string.IsNullOrEmpty(find))
            throw new ArgumentException("find must be a non-empty string.", nameof(find));
        var fullPath = Guard.RequireAllowedFile(path);
        var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        var count = CountOccurrences(content, find);
        if (count == 0)
            throw new InvalidOperationException($"'{find}' not found in {fullPath}.");
        if (expectedOccurrences.HasValue && count != expectedOccurrences.Value)
            throw new InvalidOperationException($"Expected {expectedOccurrences} occurrence(s) of '{find}' but found {count} in {fullPath}. Aborting to avoid unintended matches.");
        var patched = content.Replace(find, replace);
        await File.WriteAllTextAsync(fullPath, patched, Encoding.UTF8);
        return new { path = fullPath, replaced = count };
    }

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
        Guard.RequireNotNestedDirectoryOperation(source, destination, "copy");
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
        Guard.RequireNotNestedDirectoryOperation(source, destination, "move");
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
    public static object[] ListDirectory(string path = ".", string pattern = "*", bool recursive = false, int limit = 2000)
    {
        var fullPath = Guard.RequireAllowedDirectory(path);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFileSystemEntries(fullPath, pattern, option)
            .Take(Math.Clamp(limit, 1, 100000))
            .Select(Info)
            .ToArray();
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search file names under an allowed root using a regular expression.")]
    public static object[] Search(string path, string regex, int limit = 1000)
    {
        var fullPath = Guard.RequireAllowedDirectory(path);
        var matcher = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(30));
        return Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
            .Where(p => matcher.IsMatch(Path.GetFileName(p)))
            .Take(Math.Clamp(limit, 1, 100000))
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
        var finalPath = ResolvePathForAuthorization(TranslateHostPath(path));
        if (!AllowedRoots.Any(root => IsInside(finalPath, root)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {finalPath}");
        return finalPath;
    }

    public static void RequireWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Writes are disabled because MCP_ENABLE_WRITES=false.");
    }

    public static void RequireNotNestedDirectoryOperation(string source, string destination, string operation)
    {
        if (!Directory.Exists(source))
            return;

        var normalizedSource = NormalizeRoot(source);
        var normalizedDestination = NormalizeRoot(destination);
        if (IsInside(normalizedDestination, normalizedSource))
            throw new IOException($"Cannot {operation} a directory into itself or one of its descendants: {normalizedDestination}");
    }

    private static string[] ParseAllowedRoots()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS");
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
            return AllFilesystemRoots();
        var values = raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Select(ResolvePathForAuthorization).Select(NormalizeRoot).ToArray();
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

    private static string ResolvePathForAuthorization(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new ArgumentException($"Path has no filesystem root: {path}", nameof(path));
        var segments = fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        for (var index = 0; index < segments.Length; index++)
        {
            var candidate = Path.Combine(current, segments[index]);
            FileSystemInfo? entry = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : File.Exists(candidate)
                    ? new FileInfo(candidate)
                    : null;

            if (entry is null)
            {
                current = Path.Combine(current, Path.Combine(segments[index..]));
                break;
            }

            var target = entry.ResolveLinkTarget(returnFinalTarget: true);
            current = target is null ? candidate : Path.GetFullPath(target.FullName);
        }

        return Path.GetFullPath(current);
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
