using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var manager = new McpManager(AppContext.BaseDirectory, args);
await manager.Run();

internal sealed class McpManager
{
    private static readonly string[] MenuItems =
    [
        "전체 MCP 서버 지금 시작",
        "전체 MCP 서버 중지",
        "전체 MCP 서버 재시작",
        "상태 새로고침",
        "MCP 연결 주소 보기",
        "개별 서버 지금 시작",
        "개별 서버 중지",
        "서버 로그 보기",
        "종료"
    ];

    private readonly string[] _args;
    private readonly string _repoRoot;
    private readonly string _configPath;
    private readonly string _stateDir;
    private readonly string _logDir;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public McpManager(string baseDirectory, string[] args)
    {
        _args = args;
        _repoRoot = FindRepoRoot(baseDirectory);
        var localConfig = Path.Combine(baseDirectory, "servers.json");
        _configPath = Environment.GetEnvironmentVariable("MCP_MANAGER_CONFIG") ??
                      (File.Exists(localConfig) ? localConfig : Path.Combine(_repoRoot, "mcp-manager", "config", "servers.json"));
        _stateDir = Path.Combine(_repoRoot, ".mcp-manager");
        _logDir = Path.Combine(_stateDir, "logs");
    }

    public async Task Run()
    {
        Directory.CreateDirectory(_stateDir);
        Directory.CreateDirectory(_logDir);

        if (_args.Length == 0)
        {
            await RunInteractive();
            return;
        }

        if (IsHelp(_args[0]))
        {
            PrintHelp();
            return;
        }

        var config = await LoadConfig();
        var command = _args[0].ToLowerInvariant();
        var target = _args.Length > 1 ? _args[1] : "all";
        await ExecuteCommand(config, command, target);
    }

    private async Task ExecuteCommand(ManagerConfig config, string command, string target)
    {
        var selected = Select(config, target).ToArray();
        if (selected.Length == 0)
        {
            Console.Error.WriteLine($"No servers matched '{target}'.");
            Environment.ExitCode = 2;
            return;
        }

        switch (command)
        {
            case "list":
                PrintServers(config.Servers);
                break;
            case "start":
                foreach (var server in selected) await Start(server);
                break;
            case "stop":
                foreach (var server in selected) await Stop(server);
                break;
            case "restart":
                foreach (var server in selected)
                {
                    await Stop(server);
                    await Start(server);
                }
                break;
            case "status":
                await PrintStatus(selected);
                break;
            case "health":
                await PrintHealth(selected);
                break;
            case "logs":
                await PrintLogs(selected.First());
                break;
            case "urls":
                PrintUrls(selected);
                break;
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintHelp();
                Environment.ExitCode = 2;
                break;
        }
    }

    private async Task RunInteractive()
    {
        Console.Title = "MCP Manager - LIG Defense & Aerospace";
        var config = await LoadConfig();
        var selectedMenu = 0;
        while (true)
        {
            var rows = await GetStatusRows(config.Servers.ToArray());
            SafeClear();
            PrintBanner();
            PrintOperatorCard();
            PrintRuntimeCard(config);
            Console.WriteLine();
            PrintStatusRows(rows);
            Console.WriteLine();
            PrintConnectionSummary(config.Servers.ToArray(), compact: true);
            Console.WriteLine();
            PrintMenu(selectedMenu);
            Console.WriteLine();
            WriteColorLine("방향키로 이동, Enter로 실행, 숫자키로 바로 실행, Q로 종료", ConsoleColor.DarkGray);

            var choice = ReadMenuChoice(ref selectedMenu, selected =>
            {
                SafeClear();
                PrintBanner();
                PrintOperatorCard();
                PrintRuntimeCard(config);
                Console.WriteLine();
                PrintStatusRows(rows);
                Console.WriteLine();
                PrintConnectionSummary(config.Servers.ToArray(), compact: true);
                Console.WriteLine();
                PrintMenu(selected);
                Console.WriteLine();
                WriteColorLine("방향키로 이동, Enter로 실행, 숫자키로 바로 실행, Q로 종료", ConsoleColor.DarkGray);
            });
            Console.WriteLine();
            try
            {
                switch (choice)
                {
                    case "1":
                        await ExecuteCommand(config, "start", "all");
                        break;
                    case "2":
                        await ExecuteCommand(config, "stop", "all");
                        break;
                    case "3":
                        await ExecuteCommand(config, "restart", "all");
                        break;
                    case "4":
                        await PrintStatus(config.Servers.ToArray());
                        break;
                    case "5":
                        PrintUrls(config.Servers.ToArray());
                        break;
                    case "6":
                        await ExecuteCommand(config, "start", PromptServerName(config));
                        break;
                    case "7":
                        await ExecuteCommand(config, "stop", PromptServerName(config));
                        break;
                    case "8":
                        await ExecuteCommand(config, "logs", PromptServerName(config));
                        break;
                    case "9":
                    case "q":
                    case "Q":
                        WriteColorLine("MCP 매니저를 종료합니다.", ConsoleColor.DarkGray);
                        return;
                    default:
                        WriteColorLine("알 수 없는 선택입니다.", ConsoleColor.Yellow);
                        break;
                }
            }
            catch (Exception ex)
            {
                WriteColorLine(ex.Message, ConsoleColor.Red);
            }

            Console.WriteLine();
            WriteColor("계속하려면 Enter를 누르세요...", ConsoleColor.DarkGray);
            Console.ReadLine();
        }
    }

    private void PrintBanner()
    {
        WriteColorLine("================================================================================", ConsoleColor.DarkCyan);
        WriteColorLine(@"   __  __  ____ ____       __  __    _    _   _    _    ____ _____ ____  ", ConsoleColor.Cyan);
        WriteColorLine(@"  |  \/  |/ ___|  _ \     |  \/  |  / \  | \ | |  / \  / ___| ____|  _ \ ", ConsoleColor.Cyan);
        WriteColorLine(@"  | |\/| | |   | |_) |    | |\/| | / _ \ |  \| | / _ \| |  _|  _| | |_) |", ConsoleColor.Cyan);
        WriteColorLine(@"  | |  | | |___|  __/     | |  | |/ ___ \| |\  |/ ___ \ |_| | |___|  _ < ", ConsoleColor.Cyan);
        WriteColorLine(@"  |_|  |_|\____|_|        |_|  |_/_/   \_\_| \_/_/   \_\____|_____|_| \_\", ConsoleColor.Cyan);
        WriteColorLine("--------------------------------------------------------------------------------", ConsoleColor.DarkCyan);
        WriteColorLine("  LIG Defense & Aerospace | AI Agent MCP Control Console", ConsoleColor.White);
        WriteColorLine("  Streamable HTTP / Legacy SSE 통합 서버 오케스트레이터", ConsoleColor.DarkGray);
        WriteColorLine("  매니저 실행만으로 MCP 서버를 자동 시작하지 않습니다. 시작은 메뉴 1/6에서 수행합니다.", ConsoleColor.Yellow);
        WriteColorLine("================================================================================", ConsoleColor.DarkCyan);
    }

    private static void PrintOperatorCard()
    {
        WriteColor("  저작자  ", ConsoleColor.DarkGray);
        WriteColorLine("서태원 선임연구원", ConsoleColor.White);
        WriteColor("  부서    ", ConsoleColor.DarkGray);
        WriteColorLine("미사일시스템통제기술연구소. M&S개발단.3팀", ConsoleColor.White);
        WriteColor("  Email   ", ConsoleColor.DarkGray);
        WriteColorLine("taewon.seo2@ligdna.com", ConsoleColor.White);
    }

    private void PrintRuntimeCard(ManagerConfig config)
    {
        WriteColor("  설정    ", ConsoleColor.DarkGray);
        WriteColorLine(_configPath, ConsoleColor.Gray);
        WriteColor("  번들    ", ConsoleColor.DarkGray);
        WriteColorLine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), ConsoleColor.Gray);
        WriteColor("  서버    ", ConsoleColor.DarkGray);
        WriteColorLine($"{config.Servers.Count}개 등록됨", ConsoleColor.Green);
        WriteColor("  모드    ", ConsoleColor.DarkGray);
        WriteColorLine("수동 시작 / 수동 중지", ConsoleColor.Yellow);
    }

    private static void PrintMenu(int selected)
    {
        WriteColorLine("작전 메뉴", ConsoleColor.Cyan);
        for (var i = 0; i < MenuItems.Length; i++)
        {
            var isSelected = i == selected;
            var marker = isSelected ? ">" : " ";
            var color = i == MenuItems.Length - 1 ? ConsoleColor.DarkGray : ConsoleColor.White;
            if (isSelected)
            {
                WriteColor($" {marker} ", ConsoleColor.Cyan);
                WriteColor($"[{i + 1}] ", ConsoleColor.Yellow);
                WriteColorLine(MenuItems[i], ConsoleColor.Cyan);
            }
            else
            {
                WriteColor($" {marker} ", ConsoleColor.DarkGray);
                WriteColor($" {i + 1}  ", ConsoleColor.DarkGray);
                WriteColorLine(MenuItems[i], color);
            }
        }
    }

    private static void PrintConnectionSummary(ServerConfig[] servers, bool compact)
    {
        WriteColorLine("MCP 연결 주소", ConsoleColor.Cyan);
        if (compact)
        {
            foreach (var row in servers.Chunk(3))
            {
                foreach (var server in row)
                {
                    WriteColor($"{server.Name,-16}", ConsoleColor.Cyan);
                    WriteColor($"{server.Port,-6}", ConsoleColor.White);
                }
                Console.WriteLine();
            }
            WriteColorLine("상세 주소: 메뉴 5번 또는 CLI `McpManager.exe urls all`", ConsoleColor.DarkGray);
            return;
        }

        foreach (var server in servers)
        {
            WriteColor($"{server.Name,-18}", ConsoleColor.Cyan);
            WriteColor("HTTP ", ConsoleColor.DarkGray);
            WriteColor($"http://localhost:{server.Port}/mcp", ConsoleColor.White);
            WriteColor("   SSE ", ConsoleColor.DarkGray);
            WriteColorLine($"http://localhost:{server.Port}/sse", ConsoleColor.Gray);
        }
    }

    private static string ReadMenuChoice(ref int selected, Action<int> redraw)
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine()?.Trim() ?? "";

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = selected == 0 ? MenuItems.Length - 1 : selected - 1;
                    redraw(selected);
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % MenuItems.Length;
                    redraw(selected);
                    break;
                case ConsoleKey.Home:
                    selected = 0;
                    redraw(selected);
                    break;
                case ConsoleKey.End:
                    selected = MenuItems.Length - 1;
                    redraw(selected);
                    break;
                case ConsoleKey.Enter:
                    return (selected + 1).ToString();
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return "9";
                default:
                    if (key.KeyChar is >= '1' and <= '9')
                    {
                        selected = key.KeyChar - '1';
                        return key.KeyChar.ToString();
                    }
                    break;
            }
        }
    }

    private static string PromptServerName(ManagerConfig config)
    {
        WriteColorLine("서버 목록:", ConsoleColor.Cyan);
        foreach (var server in config.Servers)
            WriteColorLine($"- {server.Name}", ConsoleColor.Gray);
        WriteColor("서버 이름: ", ConsoleColor.Cyan);
        return Console.ReadLine()?.Trim() ?? "";
    }

    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected)
                Console.Clear();
        }
        catch
        {
        }
    }

    private static ConsoleColor RuntimeColor(string runtime)
    {
        if (runtime.StartsWith("pid ", StringComparison.OrdinalIgnoreCase) ||
            runtime.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (string.Equals(runtime, "stopped", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.DarkGray;
        return ConsoleColor.Yellow;
    }

    private static string FormatRuntime(string runtime)
    {
        if (runtime.StartsWith("Up ", StringComparison.OrdinalIgnoreCase))
            return "docker 실행";
        if (string.Equals(runtime, "stopped", StringComparison.OrdinalIgnoreCase))
            return "중지됨";
        return runtime.Length <= 12 ? runtime : runtime[..12];
    }

    private static ConsoleColor HealthColor(string health)
    {
        if (string.Equals(health, "healthy", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (string.Equals(health, "down", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        return ConsoleColor.Yellow;
    }

    private static string FormatHealth(string health)
    {
        if (string.Equals(health, "healthy", StringComparison.OrdinalIgnoreCase))
            return "정상";
        if (string.Equals(health, "down", StringComparison.OrdinalIgnoreCase))
            return "응답 없음";
        return health;
    }

    private static void WriteColor(string text, ConsoleColor color)
    {
        var original = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }
        finally
        {
            Console.ForegroundColor = original;
        }
    }

    private static void WriteColorLine(string text, ConsoleColor color)
    {
        WriteColor(text + Environment.NewLine, color);
    }

    private async Task<ManagerConfig> LoadConfig()
    {
        if (!File.Exists(_configPath))
            throw new FileNotFoundException("Manager config not found.", _configPath);

        var json = await File.ReadAllTextAsync(_configPath);
        var config = JsonSerializer.Deserialize<ManagerConfig>(json, JsonOptions()) ?? throw new InvalidOperationException("Invalid manager config.");
        foreach (var server in config.Servers)
            server.Normalize(_repoRoot);
        return config;
    }

    private IEnumerable<ServerConfig> Select(ManagerConfig config, string target)
    {
        if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
            return config.Servers;
        if (string.Equals(target, "docker", StringComparison.OrdinalIgnoreCase))
            return config.Servers.Where(s => s.Kind == "docker");
        if (string.Equals(target, "windows", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "desktop", StringComparison.OrdinalIgnoreCase))
            return config.Servers.Where(s => s.Kind == "process");
        return config.Servers.Where(s => string.Equals(s.Name, target, StringComparison.OrdinalIgnoreCase) || s.Groups.Contains(target, StringComparer.OrdinalIgnoreCase));
    }

    private async Task Start(ServerConfig server)
    {
        WriteColorLine($"== 시작: {server.Name}", ConsoleColor.Green);
        if (server.Kind == "docker")
        {
            await StartDocker(server);
            return;
        }
        await StartProcess(server);
    }

    private async Task StartDocker(ServerConfig server)
    {
        if (await DockerExists(server.ContainerName))
            await RunCommand("docker", ["rm", "-f", server.ContainerName], _repoRoot, echo: false);

        var args = new List<string> { "run", "-d", "--name", server.ContainerName, "-p", $"{server.Port}:8080" };
        foreach (var volume in server.Volumes)
            args.AddRange(["-v", Expand(volume)]);
        foreach (var env in BuildEnvironment(server))
            args.AddRange(["-e", $"{env.Key}={env.Value}"]);
        foreach (var extraArg in server.Args)
            args.Add(Expand(extraArg));
        args.Add(server.Image);

        await RunCommand("docker", args, _repoRoot);
    }

    private async Task<bool> DockerExists(string name)
    {
        var result = await RunCapture("docker", ["ps", "-a", "--filter", $"name=^{name}$", "--format", "{{.Names}}"], _repoRoot);
        return result.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Any(line => line.Trim() == name);
    }

    private async Task StartProcess(ServerConfig server)
    {
        var pidPath = PidPath(server);
        if (TryReadProcess(pidPath, out var existing) && !existing.HasExited)
        {
            Console.WriteLine($"{server.Name} 이미 실행 중입니다. PID {existing.Id}");
            return;
        }

        var workingDirectory = Expand(server.WorkingDirectory);
        var executable = Path.IsPathRooted(server.Executable) ? Expand(server.Executable) : Path.Combine(workingDirectory, server.Executable);
        if (!File.Exists(executable))
            throw new FileNotFoundException($"Executable not found for {server.Name}. Publish Windows-host packages first.", executable);

        var stdout = Path.Combine(_logDir, $"{server.Name}.out.log");
        var stderr = Path.Combine(_logDir, $"{server.Name}.err.log");
        var psi = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var env in BuildEnvironment(server))
            psi.Environment[env.Key] = env.Value;
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{server.Port}";

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {server.Name}");
        _ = Pump(process.StandardOutput, stdout);
        _ = Pump(process.StandardError, stderr);
        await File.WriteAllTextAsync(pidPath, process.Id.ToString());
        Console.WriteLine($"{server.Name} 시작됨. PID {process.Id}");
    }

    private async Task Stop(ServerConfig server)
    {
        WriteColorLine($"== 중지: {server.Name}", ConsoleColor.Yellow);
        if (server.Kind == "docker")
        {
            await RunCommand("docker", ["rm", "-f", server.ContainerName], _repoRoot, echo: false, ignoreExit: true);
            return;
        }

        var pidPath = PidPath(server);
        if (TryReadProcess(pidPath, out var process) && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Console.WriteLine($"중지됨. PID {process.Id}");
        }
        if (File.Exists(pidPath))
            File.Delete(pidPath);
    }

    private async Task PrintStatus(ServerConfig[] servers)
    {
        PrintStatusRows(await GetStatusRows(servers));
    }

    private async Task<StatusRow[]> GetStatusRows(ServerConfig[] servers)
    {
        return await Task.WhenAll(servers.Select(async server =>
        {
            var runtimeTask = server.Kind == "docker" ? DockerStatus(server) : Task.FromResult(ProcessStatus(server));
            var healthTask = Health(server);
            await Task.WhenAll(runtimeTask, healthTask);
            return new StatusRow(server, runtimeTask.Result, healthTask.Result);
        }));
    }

    private static void PrintStatusRows(StatusRow[] rows)
    {
        WriteColor("서버              ", ConsoleColor.DarkGray);
        WriteColor("방식     ", ConsoleColor.DarkGray);
        WriteColor("포트  ", ConsoleColor.DarkGray);
        WriteColor("실행상태     ", ConsoleColor.DarkGray);
        WriteColorLine("헬스", ConsoleColor.DarkGray);
        foreach (var row in rows)
        {
            var server = row.Server;
            var runtime = row.Runtime;
            var health = row.Health;
            WriteColor($"{server.Name,-18}", ConsoleColor.Cyan);
            WriteColor($"{server.Kind,-9}", ConsoleColor.Gray);
            WriteColor($"{server.Port,-6}", ConsoleColor.Gray);
            WriteColor($"{FormatRuntime(runtime),-13}", RuntimeColor(runtime));
            WriteColorLine(FormatHealth(health), HealthColor(health));
        }
    }

    private async Task PrintHealth(ServerConfig[] servers)
    {
        foreach (var server in servers)
            Console.WriteLine($"{server.Name}: {await Health(server)} {server.HealthUrl}");
    }

    private async Task<string> DockerStatus(ServerConfig server)
    {
        var result = await RunCapture("docker", ["ps", "-a", "--filter", $"name=^{server.ContainerName}$", "--format", "{{.Status}}"], _repoRoot, TimeSpan.FromSeconds(2));
        if (result.ExitCode == -1)
            return "docker timeout";
        return string.IsNullOrWhiteSpace(result.Stdout) ? "stopped" : result.Stdout.Trim().Split(Environment.NewLine)[0];
    }

    private string ProcessStatus(ServerConfig server)
    {
        return TryReadProcess(PidPath(server), out var process) && !process.HasExited ? $"pid {process.Id}" : "stopped";
    }

    private async Task<string> Health(ServerConfig server)
    {
        try
        {
            using var response = await _http.GetAsync(server.HealthUrl);
            return response.IsSuccessStatusCode ? "healthy" : $"http {(int)response.StatusCode}";
        }
        catch
        {
            return "down";
        }
    }

    private async Task PrintLogs(ServerConfig server)
    {
        if (server.Kind == "docker")
        {
            await RunCommand("docker", ["logs", "--tail", "200", server.ContainerName], _repoRoot);
            return;
        }

        foreach (var path in new[] { Path.Combine(_logDir, $"{server.Name}.out.log"), Path.Combine(_logDir, $"{server.Name}.err.log") })
        {
            Console.WriteLine($"== {path}");
            if (!File.Exists(path))
            {
                Console.WriteLine("(로그 없음)");
                continue;
            }
            var lines = await File.ReadAllLinesAsync(path);
            foreach (var line in lines.TakeLast(200))
                Console.WriteLine(line);
        }
    }

    private void PrintUrls(ServerConfig[] servers)
    {
        foreach (var server in servers)
        {
            WriteColor($"{server.Name,-18}", ConsoleColor.Cyan);
            WriteColor(" HTTP ", ConsoleColor.DarkGray);
            WriteColor($"http://localhost:{server.Port}/mcp", ConsoleColor.White);
            WriteColor("   SSE ", ConsoleColor.DarkGray);
            WriteColorLine($"http://localhost:{server.Port}/sse", ConsoleColor.Gray);
        }
    }

    private static void PrintServers(IEnumerable<ServerConfig> servers)
    {
        WriteColor("서버              ", ConsoleColor.DarkGray);
        WriteColor("방식     ", ConsoleColor.DarkGray);
        WriteColor("포트  ", ConsoleColor.DarkGray);
        WriteColorLine("그룹", ConsoleColor.DarkGray);
        foreach (var server in servers)
        {
            WriteColor($"{server.Name,-18}", ConsoleColor.Cyan);
            WriteColor($"{server.Kind,-9}", ConsoleColor.Gray);
            WriteColor($"{server.Port,-6}", ConsoleColor.Gray);
            WriteColorLine(string.Join(",", server.Groups), ConsoleColor.DarkGray);
        }
    }

    private string PidPath(ServerConfig server) => Path.Combine(_stateDir, $"{server.Name}.pid");

    private Dictionary<string, string> BuildEnvironment(ServerConfig server)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var envFile in ResolveEnvFiles(server))
        {
            foreach (var env in ReadEnvFile(envFile))
                result[env.Key] = Expand(env.Value);
        }
        foreach (var env in server.Env)
            result[env.Key] = Expand(env.Value);
        return result;
    }

    private IEnumerable<string> ResolveEnvFiles(ServerConfig server)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in CandidateEnvFiles(server))
        {
            var fullPath = Path.GetFullPath(Expand(path));
            if (seen.Add(fullPath) && File.Exists(fullPath))
                yield return fullPath;
        }
    }

    private IEnumerable<string> CandidateEnvFiles(ServerConfig server)
    {
        var managerDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        yield return Path.Combine(managerDirectory, "common.env");
        yield return Path.Combine(managerDirectory, $"{server.Name}.env");

        if (!string.IsNullOrWhiteSpace(server.WorkingDirectory))
        {
            var workingDirectory = Expand(server.WorkingDirectory);
            if (Directory.Exists(workingDirectory))
            {
                foreach (var envFile in Directory.GetFiles(workingDirectory, "*.env").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    yield return envFile;
            }
        }

        foreach (var envFile in server.EnvFiles)
            yield return envFile;
    }

    private static Dictionary<string, string> ReadEnvFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }
            result[key] = value;
        }
        return result;
    }

    private static bool TryReadProcess(string pidPath, out Process process)
    {
        process = null!;
        try
        {
            if (!File.Exists(pidPath)) return false;
            var pid = int.Parse(File.ReadAllText(pidPath).Trim());
            process = Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunCommand(string fileName, IEnumerable<string> args, string workingDirectory, bool echo = true, bool ignoreExit = false)
    {
        var result = await RunCapture(fileName, args, workingDirectory);
        if (echo && !string.IsNullOrWhiteSpace(result.Stdout)) Console.WriteLine(result.Stdout.TrimEnd());
        if (echo && !string.IsNullOrWhiteSpace(result.Stderr)) Console.Error.WriteLine(result.Stderr.TrimEnd());
        if (result.ExitCode != 0 && !ignoreExit)
            throw new InvalidOperationException($"{fileName} exited with {result.ExitCode}: {result.Stderr}");
    }

    private static async Task<CommandResult> RunCapture(string fileName, IEnumerable<string> args, string workingDirectory, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (timeout is null)
        {
            await process.WaitForExitAsync();
        }
        else
        {
            var waitTask = process.WaitForExitAsync();
            var exited = await Task.WhenAny(waitTask, Task.Delay(timeout.Value)) == waitTask;
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return new CommandResult(-1, "", "timed out");
            }
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private static async Task Pump(StreamReader reader, string path)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            await writer.WriteLineAsync(line);
        }
    }

    private string Expand(string value)
    {
        return Environment.ExpandEnvironmentVariables(value)
            .Replace("{repo}", _repoRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{manager}", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            .Replace("{home}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mcp-manager")) && File.Exists(Path.Combine(dir.FullName, "README.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return start;
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

    private static void PrintHelp()
    {
        Console.WriteLine("""
        MCP Manager - LIG Defense & Aerospace

        사용법:
          mcp-manager list [all|docker|windows|group|name]
          mcp-manager start [all|docker|windows|group|name]
          mcp-manager stop [all|docker|windows|group|name]
          mcp-manager restart [all|docker|windows|group|name]
          mcp-manager status [all|docker|windows|group|name]
          mcp-manager health [all|docker|windows|group|name]
          mcp-manager logs <name>
          mcp-manager urls [all|docker|windows|group|name]

        주요 그룹: docker, windows, desktop, database, devops, observability, cad, office
        """);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

internal sealed class ManagerConfig
{
    public List<ServerConfig> Servers { get; set; } = [];
}

internal sealed class ServerConfig
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Port { get; set; }
    public string Image { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Executable { get; set; } = "";
    public string HealthUrl { get; set; } = "";
    public List<string> Groups { get; set; } = [];
    public List<string> Volumes { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = [];
    public List<string> EnvFiles { get; set; } = [];
    public List<string> Args { get; set; } = [];

    public void Normalize(string repoRoot)
    {
        ContainerName = string.IsNullOrWhiteSpace(ContainerName) ? Name : ContainerName;
        Image = string.IsNullOrWhiteSpace(Image) && Kind == "docker" ? $"local/{Name}" : Image;
        HealthUrl = string.IsNullOrWhiteSpace(HealthUrl) ? $"http://localhost:{Port}/healthz" : HealthUrl;
        if (!Groups.Contains(Kind, StringComparer.OrdinalIgnoreCase)) Groups.Add(Kind);
        if (Kind == "process" && !Groups.Contains("windows", StringComparer.OrdinalIgnoreCase)) Groups.Add("windows");
    }
}

internal sealed record CommandResult(int ExitCode, string Stdout, string Stderr);

internal sealed record StatusRow(ServerConfig Server, string Runtime, string Health);
