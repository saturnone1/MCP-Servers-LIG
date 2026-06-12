using System.ComponentModel;
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
        var extraArgs = _args.Skip(2).ToArray();
        await ExecuteCommand(config, command, target, extraArgs);
    }

    private async Task ExecuteCommand(ManagerConfig config, string command, string target, string[]? extraArgs = null)
    {
        extraArgs ??= [];
        var selected = Select(config, target).ToArray();
        if (selected.Length == 0)
        {
            Console.Error.WriteLine($"'{target}'에 해당하는 서버가 없습니다.");
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
            case "env":
                if (selected.Length != 1)
                    throw new InvalidOperationException("env 명령은 서버 하나를 지정해야 합니다.");
                if (Console.IsInputRedirected || Console.IsOutputRedirected)
                    PrintEnv(selected[0]);
                else
                    ShowEnvScreen(selected[0]);
                break;
            case "set-env":
                if (selected.Length != 1)
                    throw new InvalidOperationException("set-env 명령은 서버 하나를 지정해야 합니다.");
                if (extraArgs.Length < 2)
                    throw new InvalidOperationException("사용법: set-env <server> <KEY> <VALUE>");
                SetEnvValue(selected[0], extraArgs[0], string.Join(' ', extraArgs.Skip(1)));
                break;
            case "remove-env":
                if (selected.Length != 1)
                    throw new InvalidOperationException("remove-env 명령은 서버 하나를 지정해야 합니다.");
                if (extraArgs.Length < 1)
                    throw new InvalidOperationException("사용법: remove-env <server> <KEY>");
                RemoveEnvValue(selected[0], extraArgs[0]);
                break;
            default:
                Console.Error.WriteLine($"알 수 없는 명령입니다: {command}");
                PrintHelp();
                Environment.ExitCode = 2;
                break;
        }
    }

    private async Task RunInteractive()
    {
        Console.Title = "LIG AI MCP - LIG Defense & Aerospace";
        var cursorWasVisible = TryGetCursorVisible();
        PrepareInteractiveConsole();
        var config = await LoadConfig();
        var servers = config.Servers.ToArray();
        var selected = 0;
        var rows = await GetStatusRows(servers);
        var message = "준비됨. MCP 서버는 자동으로 시작되지 않습니다.";

        try
        {
            while (true)
            {
                RenderDashboard(config, rows, selected, message);
                var key = ReadDashboardKey();
                try
                {
                    switch (key)
                    {
                        case DashboardKey.Up:
                            selected = selected == 0 ? servers.Length - 1 : selected - 1;
                            break;
                        case DashboardKey.Down:
                            selected = (selected + 1) % servers.Length;
                            break;
                        case DashboardKey.Home:
                            selected = 0;
                            break;
                        case DashboardKey.End:
                            selected = servers.Length - 1;
                            break;
                        case DashboardKey.Details:
                            ShowServerDetails(servers[selected], rows.First(row => row.Server == servers[selected]));
                            break;
                        case DashboardKey.StartSelected:
                            await ExecuteCommand(config, "start", servers[selected].Name);
                            rows = await GetStatusRows(servers);
                            message = $"{servers[selected].Name} 시작 완료.";
                            break;
                        case DashboardKey.StopSelected:
                            await ExecuteCommand(config, "stop", servers[selected].Name);
                            rows = await GetStatusRows(servers);
                            message = $"{servers[selected].Name} 중지 완료.";
                            break;
                        case DashboardKey.RestartSelected:
                            await ExecuteCommand(config, "restart", servers[selected].Name);
                            rows = await GetStatusRows(servers);
                            message = $"{servers[selected].Name} 재시작 완료.";
                            break;
                        case DashboardKey.StartAll:
                            await ExecuteCommand(config, "start", "all");
                            rows = await GetStatusRows(servers);
                            message = "전체 서버 시작 완료.";
                            break;
                        case DashboardKey.StopAll:
                            await ExecuteCommand(config, "stop", "all");
                            rows = await GetStatusRows(servers);
                            message = "전체 서버 중지 완료.";
                            break;
                        case DashboardKey.RestartAll:
                            await ExecuteCommand(config, "restart", "all");
                            rows = await GetStatusRows(servers);
                            message = "전체 서버 재시작 완료.";
                            break;
                        case DashboardKey.Refresh:
                            rows = await GetStatusRows(servers);
                            message = $"{DateTime.Now:HH:mm:ss}에 상태를 새로고침했습니다.";
                            break;
                        case DashboardKey.UrlsSelected:
                            ShowUrls([servers[selected]]);
                            break;
                        case DashboardKey.UrlsAll:
                            ShowUrls(servers);
                            break;
                        case DashboardKey.Logs:
                            await ShowLogsScreen(servers[selected]);
                            break;
                        case DashboardKey.Env:
                            ShowEnvScreen(servers[selected]);
                            message = $"{servers[selected].Name} 환경변수를 확인했습니다.";
                            break;
                        case DashboardKey.Quit:
                            SafeClear();
                            WriteColorLine("LIG AI MCP를 종료합니다.", ConsoleColor.DarkGray);
                            return;
                    }
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                }
            }
        }
        finally
        {
            RestoreInteractiveConsole(cursorWasVisible);
        }
    }

    private void RenderDashboard(ManagerConfig config, StatusRow[] rows, int selected, string message)
    {
        SafeClear();
        var total = rows.Length;
        var running = rows.Count(IsServerActive);
        var stopped = total - running;
        var healthy = rows.Count(row => string.Equals(row.Health, "healthy", StringComparison.OrdinalIgnoreCase));
        var down = rows.Count(row => string.Equals(row.Health, "down", StringComparison.OrdinalIgnoreCase));

        PrintHero(config);
        WriteChip("전체", total.ToString(), ConsoleColor.White, ConsoleColor.DarkBlue);
        WriteChip("실행", running.ToString(), ConsoleColor.Black, ConsoleColor.Green);
        WriteChip("중지", stopped.ToString(), ConsoleColor.White, ConsoleColor.DarkGray);
        WriteChip("정상", healthy.ToString(), ConsoleColor.Black, ConsoleColor.Green);
        WriteChip("응답없음", down.ToString(), ConsoleColor.White, down > 0 ? ConsoleColor.Red : ConsoleColor.DarkGray);
        Console.WriteLine();
        WriteColorLine(new string('-', SafeWidth()), ConsoleColor.DarkGray);

        var tableHeight = Math.Max(1, SafeHeight() - HeroLineCount() - 9);
        var start = Math.Clamp(selected - tableHeight / 2, 0, Math.Max(0, rows.Length - tableHeight));
        var visible = rows.Skip(start).Take(tableHeight).ToArray();

        WriteColor("  ", ConsoleColor.DarkGray);
        WriteColor($"{PadCell("서버", 18)}", ConsoleColor.DarkGray);
        WriteColor($"{PadCell("분류", 24)}", ConsoleColor.DarkGray);
        WriteColor($"{PadCell("포트", 6)}", ConsoleColor.DarkGray);
        WriteColor($"{PadCell("실행상태", 14)}", ConsoleColor.DarkGray);
        WriteColorLine("헬스", ConsoleColor.DarkGray);

        for (var i = 0; i < visible.Length; i++)
        {
            var absoluteIndex = start + i;
            var row = visible[i];
            var isSelected = absoluteIndex == selected;
            var background = isSelected ? ConsoleColor.DarkBlue : (ConsoleColor?)null;
            WriteCell(isSelected ? ">" : " ", 2, isSelected ? ConsoleColor.White : ConsoleColor.DarkGray, background);
            WriteCell(row.Server.Name, 18, isSelected ? ConsoleColor.White : ConsoleColor.Cyan, background);
            WriteCell(string.Join(",", row.Server.Groups), 24, ConsoleColor.DarkGray, background);
            WriteCell(row.Server.Port.ToString(), 6, ConsoleColor.Gray, background);
            WriteCell(FormatRuntime(row), 14, RuntimeColor(row), background);
            WriteCell(FormatHealth(row.Health), Math.Max(10, SafeWidth() - 64), HealthColor(row.Health), background);
            Console.WriteLine();
        }

        WriteColorLine(new string('-', SafeWidth()), ConsoleColor.DarkGray);
        WriteColor(" 상태 ", ConsoleColor.DarkCyan);
        WriteColorLine(message, MessageColor(message));
        WriteColor(" 단축키 ", ConsoleColor.DarkCyan);
        WriteColorLine(TrimCell("↑/↓ 이동  Enter 상세  S 시작  T 중지  R 재시작  A 전체시작  X 전체중지  U 주소  L 로그  E 환경  F5/Space 새로고침  Q 종료", SafeWidth()), ConsoleColor.DarkGray);
    }

    private static DashboardKey ReadDashboardKey()
    {
        if (Console.IsInputRedirected)
        {
            var value = Console.ReadLine()?.Trim().ToLowerInvariant();
            return value is "q" or "9" ? DashboardKey.Quit : DashboardKey.Refresh;
        }

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: return DashboardKey.Up;
                case ConsoleKey.DownArrow: return DashboardKey.Down;
                case ConsoleKey.Home: return DashboardKey.Home;
                case ConsoleKey.End: return DashboardKey.End;
                case ConsoleKey.Enter: return DashboardKey.Details;
                case ConsoleKey.S: return DashboardKey.StartSelected;
                case ConsoleKey.T: return DashboardKey.StopSelected;
                case ConsoleKey.R: return DashboardKey.RestartSelected;
                case ConsoleKey.A: return DashboardKey.StartAll;
                case ConsoleKey.X: return DashboardKey.StopAll;
                case ConsoleKey.U: return DashboardKey.UrlsSelected;
                case ConsoleKey.L: return DashboardKey.Logs;
                case ConsoleKey.E: return DashboardKey.Env;
                case ConsoleKey.F5:
                case ConsoleKey.Spacebar:
                    return DashboardKey.Refresh;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return DashboardKey.Quit;
                default:
                    switch (key.KeyChar)
                    {
                        case '1': return DashboardKey.StartAll;
                        case '2': return DashboardKey.StopAll;
                        case '3': return DashboardKey.RestartAll;
                        case '4': return DashboardKey.Refresh;
                        case '5': return DashboardKey.UrlsAll;
                        case '6': return DashboardKey.StartSelected;
                        case '7': return DashboardKey.StopSelected;
                        case '8': return DashboardKey.Logs;
                        case '9': return DashboardKey.Quit;
                    }
                    break;
            }
        }
    }

    private void PrintHero(ManagerConfig config)
    {
        var width = SafeWidth();
        WriteColorLine(new string('=', width), ConsoleColor.DarkCyan);
        PrintLogoMark();
        WriteColorLine(new string('-', width), ConsoleColor.DarkCyan);
        WriteKeyValueLine("시스템", "LIG Defense & Aerospace LIG AI MCP", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        WriteKeyValueLine("담당", "서태원 선임연구원 / 미사일시스템통제기술연구소. M&S개발단.3팀", $"서버 {config.Servers.Count}개");
        WriteKeyValueLine("설정", ShortenPath(_configPath, width - 16), "");
        WriteKeyValueLine("번들", ShortenPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), width - 16), "");
        WriteColorLine(new string('=', width), ConsoleColor.DarkCyan);
    }

    private static void PrintLogoMark()
    {
        var width = SafeWidth();
        if (width < 78 || SafeHeight() < 30)
            return;

        var logoWidth = 74;
        var top = "+" + new string('-', logoWidth - 2) + "+";
        var lines = new[]
        {
            "L      III   GGG       A     III      M   M   CCC   PPP ",
            "L       I   G         A A     I       MM MM  C   C  P  P",
            "L       I   G  GG    AAAAA    I       M M M  C      PPP ",
            "L       I   G   G    A   A    I       M   M  C   C  P   ",
            "LLLL   III   GGG     A   A   III      M   M   CCC   P   ",
            "",
            "AI Model Context Protocol Server Suite"
        };

        WriteColorLine(CenterText(top, width), ConsoleColor.DarkCyan);
        foreach (var line in lines)
            WriteLogoBoxLine(line, logoWidth, width, line.Contains("AI Model") ? ConsoleColor.Gray : ConsoleColor.Cyan);
        WriteColorLine(CenterText(top, width), ConsoleColor.DarkCyan);
    }

    private static void WriteLogoBoxLine(string content, int logoWidth, int screenWidth, ConsoleColor color)
    {
        var innerWidth = logoWidth - 4;
        var line = "| " + CenterFixed(content, innerWidth) + " |";
        WriteColorLine(CenterText(line, screenWidth), color);
    }

    private static string CenterFixed(string text, int width)
    {
        var trimmed = TrimCell(text, width);
        var remaining = Math.Max(0, width - DisplayWidth(trimmed));
        var left = remaining / 2;
        var right = remaining - left;
        return new string(' ', left) + trimmed + new string(' ', right);
    }

    private static int HeroLineCount()
    {
        var artLines = SafeWidth() >= 78 && SafeHeight() >= 30 ? 9 : 0;
        return 7 + artLines;
    }

    private void ShowServerDetails(ServerConfig server, StatusRow row)
    {
        SafeClear();
        PrintPanelHeader(server.Name, "서버 상세");
        WriteField("방식", server.Kind);
        WriteField("그룹", string.Join(", ", server.Groups));
        WriteField("포트", server.Port.ToString());
        WriteField("실행상태", FormatRuntime(row));
        WriteField("헬스", FormatHealth(row.Health));
        WriteField("HTTP", $"http://localhost:{server.Port}/mcp");
        WriteField("SSE", $"http://localhost:{server.Port}/sse");
        WriteField("헬스 URL", server.HealthUrl);
        WriteField("작업폴더", Expand(server.WorkingDirectory));
        WriteField("실행파일", ResolveExecutable(server));
        WriteField("환경파일", ResolveEditableEnvPath(server));
        WriteField("표준출력", Path.Combine(_logDir, $"{server.Name}.out.log"));
        WriteField("오류로그", Path.Combine(_logDir, $"{server.Name}.err.log"));
        WaitBack();
    }

    private static void ShowUrls(ServerConfig[] servers)
    {
        SafeClear();
        PrintPanelHeader(servers.Length == 1 ? servers[0].Name : "전체 서버", "MCP 연결 주소");
        foreach (var server in servers)
        {
            WriteColor($"{server.Name,-18}", ConsoleColor.Cyan);
            WriteColor(" HTTP ", ConsoleColor.DarkGray);
            WriteColor($"http://localhost:{server.Port}/mcp", ConsoleColor.White);
            WriteColor("   SSE ", ConsoleColor.DarkGray);
            WriteColorLine($"http://localhost:{server.Port}/sse", ConsoleColor.Gray);
        }
        WaitBack();
    }

    private async Task ShowLogsScreen(ServerConfig server)
    {
        SafeClear();
        PrintPanelHeader(server.Name, "최근 로그");
        if (server.Kind == "docker")
        {
            await RunCommand("docker", ["logs", "--tail", "120", server.ContainerName], _repoRoot);
            WaitBack();
            return;
        }

        foreach (var path in new[] { Path.Combine(_logDir, $"{server.Name}.out.log"), Path.Combine(_logDir, $"{server.Name}.err.log") })
        {
            WriteColorLine($"== {path}", ConsoleColor.DarkGray);
            if (!File.Exists(path))
            {
                WriteColorLine("(로그 없음)", ConsoleColor.DarkGray);
                continue;
            }

            var limit = Math.Max(20, SafeHeight() / 2);
            foreach (var line in (await File.ReadAllLinesAsync(path)).TakeLast(limit))
                Console.WriteLine(TrimCell(line, SafeWidth()));
        }
        WaitBack();
    }

    private void OpenEnvEditor(ServerConfig server)
    {
        var managerDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var editCmd = Path.Combine(managerDirectory, $"edit-env-{server.Name}.cmd");
        if (File.Exists(editCmd))
        {
            Process.Start(new ProcessStartInfo(editCmd) { UseShellExecute = true, WorkingDirectory = managerDirectory });
            return;
        }

        var envPath = ResolveEditableEnvPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
        if (!File.Exists(envPath))
        {
            File.WriteAllLines(envPath,
            [
                $"# {server.Name} 환경변수 파일입니다.",
                "# 값을 바꾼 뒤 LIG AI MCP에서 서버를 재시작하세요."
            ]);
        }
        Process.Start(new ProcessStartInfo("notepad.exe", envPath) { UseShellExecute = true });
    }

    private void ShowEnvScreen(ServerConfig server)
    {
        var envPath = EnsureEditableEnvFile(server);
        var entries = ReadEditableEnvEntries(envPath);
        var selected = 0;
        var message = "Enter 수정, A 추가, D 삭제, N 메모장, R 다시읽기, B 뒤로";

        while (true)
        {
            SafeClear();
            PrintPanelHeader(server.Name, "환경변수 설정");
            WriteField("환경파일", envPath);
            WriteField("적용방법", "값을 바꾼 뒤 해당 MCP 서버를 재시작하세요.");
            Console.WriteLine();

            WriteColor("  ", ConsoleColor.DarkGray);
            WriteColor($"{PadCell("키", 32)}", ConsoleColor.DarkGray);
            WriteColorLine("값", ConsoleColor.DarkGray);
            WriteColorLine(new string('-', SafeWidth()), ConsoleColor.DarkGray);

            if (entries.Count == 0)
            {
                WriteColorLine("  등록된 환경변수가 없습니다. A를 눌러 추가하세요.", ConsoleColor.DarkGray);
            }
            else
            {
                selected = Math.Clamp(selected, 0, entries.Count - 1);
                var tableHeight = Math.Max(4, SafeHeight() - 13);
                var start = Math.Clamp(selected - tableHeight / 2, 0, Math.Max(0, entries.Count - tableHeight));
                foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)).Skip(start).Take(tableHeight))
                {
                    var isSelected = index == selected;
                    var background = isSelected ? ConsoleColor.DarkBlue : (ConsoleColor?)null;
                    WriteCell(isSelected ? ">" : " ", 2, isSelected ? ConsoleColor.White : ConsoleColor.DarkGray, background);
                    WriteCell(entry.Key, 32, isSelected ? ConsoleColor.White : ConsoleColor.Cyan, background);
                    WriteCell(MaskEnvValue(entry.Key, entry.Value), Math.Max(10, SafeWidth() - 34), ConsoleColor.Gray, background);
                    Console.WriteLine();
                }
            }

            WriteColorLine(new string('-', SafeWidth()), ConsoleColor.DarkGray);
            WriteColor(" 상태 ", ConsoleColor.DarkCyan);
            WriteColorLine(message, MessageColor(message));
            WriteColor(" 단축키 ", ConsoleColor.DarkCyan);
            WriteColorLine("↑/↓ 이동  Enter 수정  A 추가  D 삭제  N 메모장  R 다시읽기  B 뒤로", ConsoleColor.DarkGray);

            if (Console.IsInputRedirected)
                return;

            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (entries.Count > 0) selected = selected == 0 ? entries.Count - 1 : selected - 1;
                    break;
                case ConsoleKey.DownArrow:
                    if (entries.Count > 0) selected = (selected + 1) % entries.Count;
                    break;
                case ConsoleKey.Enter:
                    if (entries.Count == 0)
                    {
                        message = "수정할 환경변수가 없습니다.";
                        break;
                    }
                    if (PromptEnvValue(entries[selected].Key, entries[selected].Value, out var editedValue))
                    {
                        entries[selected] = entries[selected] with { Value = editedValue };
                        WriteEditableEnvEntries(envPath, server.Name, entries);
                        message = $"{entries[selected].Key} 저장 완료.";
                    }
                    else
                    {
                        message = "수정을 취소했습니다.";
                    }
                    break;
                case ConsoleKey.A:
                    if (PromptNewEnv(entries, out var newEntry))
                    {
                        entries.RemoveAll(entry => string.Equals(entry.Key, newEntry.Key, StringComparison.OrdinalIgnoreCase));
                        entries.Add(newEntry);
                        entries = entries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToList();
                        selected = entries.FindIndex(entry => string.Equals(entry.Key, newEntry.Key, StringComparison.OrdinalIgnoreCase));
                        WriteEditableEnvEntries(envPath, server.Name, entries);
                        message = $"{newEntry.Key} 추가 완료.";
                    }
                    else
                    {
                        message = "추가를 취소했습니다.";
                    }
                    break;
                case ConsoleKey.D:
                case ConsoleKey.Delete:
                    if (entries.Count == 0)
                    {
                        message = "삭제할 환경변수가 없습니다.";
                        break;
                    }
                    var removedKey = entries[selected].Key;
                    if (Confirm($"'{removedKey}' 환경변수를 삭제할까요?"))
                    {
                        entries.RemoveAt(selected);
                        if (selected >= entries.Count) selected = Math.Max(0, entries.Count - 1);
                        WriteEditableEnvEntries(envPath, server.Name, entries);
                        message = $"{removedKey} 삭제 완료.";
                    }
                    else
                    {
                        message = "삭제를 취소했습니다.";
                    }
                    break;
                case ConsoleKey.N:
                    OpenEnvEditor(server);
                    message = "메모장을 열었습니다.";
                    break;
                case ConsoleKey.R:
                case ConsoleKey.F5:
                    entries = ReadEditableEnvEntries(envPath);
                    message = "환경변수 파일을 다시 읽었습니다.";
                    break;
                case ConsoleKey.B:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private void PrintEnv(ServerConfig server)
    {
        var envPath = EnsureEditableEnvFile(server);
        Console.WriteLine($"{server.Name} env: {envPath}");
        foreach (var entry in ReadEditableEnvEntries(envPath))
            Console.WriteLine($"{entry.Key}={entry.Value}");
    }

    private void SetEnvValue(ServerConfig server, string key, string value)
    {
        ValidateEnvKey(key);
        var envPath = EnsureEditableEnvFile(server);
        var entries = ReadEditableEnvEntries(envPath);
        entries.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        entries.Add(new EnvEntry(key, value));
        entries = entries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToList();
        WriteEditableEnvEntries(envPath, server.Name, entries);
        WriteColorLine($"{server.Name}: {key} 저장 완료. 서버를 재시작하면 적용됩니다.", ConsoleColor.Green);
    }

    private void RemoveEnvValue(ServerConfig server, string key)
    {
        ValidateEnvKey(key);
        var envPath = EnsureEditableEnvFile(server);
        var entries = ReadEditableEnvEntries(envPath);
        var removed = entries.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        WriteEditableEnvEntries(envPath, server.Name, entries);
        WriteColorLine(removed > 0
            ? $"{server.Name}: {key} 삭제 완료. 서버를 재시작하면 적용됩니다."
            : $"{server.Name}: {key} 값이 없습니다.", removed > 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
    }

    private string ResolveEditableEnvPath(ServerConfig server)
    {
        var workingDirectory = Expand(server.WorkingDirectory);
        return Path.Combine(workingDirectory, $"{server.Name}.env");
    }

    private string EnsureEditableEnvFile(ServerConfig server)
    {
        var envPath = ResolveEditableEnvPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
        if (!File.Exists(envPath))
        {
            var entries = server.Env
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new EnvEntry(pair.Key, pair.Value))
                .ToList();
            WriteEditableEnvEntries(envPath, server.Name, entries);
        }
        return envPath;
    }

    private static List<EnvEntry> ReadEditableEnvEntries(string path)
    {
        if (!File.Exists(path))
            return [];

        var entries = new List<EnvEntry>();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            var value = UnquoteEnvValue(line[(equals + 1)..].Trim());
            if (IsValidEnvKey(key))
                entries.Add(new EnvEntry(key, value));
        }

        return entries
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteEditableEnvEntries(string path, string serverName, IReadOnlyList<EnvEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new List<string>
        {
            $"# Editable environment variables for {serverName}.",
            "# Restart the server through LIG AI MCP after changing this file.",
            "# Lines use KEY=VALUE format.",
            ""
        };
        lines.AddRange(entries.Select(entry => $"{entry.Key}={EscapeEnvValue(entry.Value)}"));
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool PromptEnvValue(string key, string currentValue, out string value)
    {
        SafeClear();
        PrintPanelHeader(key, "환경변수 값 수정");
        WriteField("현재값", MaskEnvValue(key, currentValue));
        WriteColorLine("새 값을 입력하세요. 빈 값도 저장됩니다. 취소하려면 Ctrl+C 대신 ESC 화면으로 돌아가려면 그냥 Enter 후 다시 수정하세요.", ConsoleColor.DarkGray);
        Console.WriteLine();
        value = PromptLine("새 값", currentValue);
        return true;
    }

    private static bool PromptNewEnv(IReadOnlyList<EnvEntry> entries, out EnvEntry entry)
    {
        SafeClear();
        PrintPanelHeader("새 환경변수", "환경변수 추가");
        var key = PromptLine("키", "");
        if (string.IsNullOrWhiteSpace(key))
        {
            entry = default;
            return false;
        }
        ValidateEnvKey(key);
        var value = PromptLine("값", entries.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "");
        entry = new EnvEntry(key.Trim(), value);
        return true;
    }

    private static string PromptLine(string label, string defaultValue)
    {
        var previousCursor = TryGetCursorVisible();
        try { Console.CursorVisible = true; } catch { }
        try
        {
            WriteColor($"{label}", ConsoleColor.DarkCyan);
            if (!string.IsNullOrEmpty(defaultValue))
                WriteColor($" [{MaskEnvValue(label, defaultValue)}]", ConsoleColor.DarkGray);
            WriteColor(": ", ConsoleColor.Gray);
            var input = Console.ReadLine();
            return input is null || input.Length == 0 ? defaultValue : input;
        }
        finally
        {
            try { Console.CursorVisible = previousCursor; } catch { }
        }
    }

    private static bool Confirm(string message)
    {
        SafeClear();
        PrintPanelHeader("확인", message);
        WriteColor("Y를 누르면 진행합니다. 다른 키는 취소합니다.", ConsoleColor.Yellow);
        if (Console.IsInputRedirected)
            return false;
        var key = Console.ReadKey(intercept: true);
        return key.Key is ConsoleKey.Y;
    }

    private static void ValidateEnvKey(string key)
    {
        if (!IsValidEnvKey(key))
            throw new InvalidOperationException($"환경변수 키 형식이 올바르지 않습니다: {key}");
    }

    private static bool IsValidEnvKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        return key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') && !char.IsDigit(key[0]);
    }

    private static string UnquoteEnvValue(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
            return value[1..^1];
        return value;
    }

    private static string EscapeEnvValue(string value)
    {
        value = value.Replace("\r", "").Replace("\n", "\\n");
        if (value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('#') || value.Contains('"'))
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return value;
    }

    private static string MaskEnvValue(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(빈 값)";

        var sensitiveTokens = new[] { "TOKEN", "PASSWORD", "SECRET", "KEY", "CONNECTION_STRING", "CONNSTR", "PAT" };
        if (!sensitiveTokens.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return TrimCell(value, Math.Max(10, SafeWidth() - 42));

        if (value.Length <= 6)
            return new string('*', Math.Min(value.Length, 6));
        return value[..2] + new string('*', Math.Min(12, value.Length - 4)) + value[^2..];
    }

    private string ResolveExecutable(ServerConfig server)
    {
        var workingDirectory = Expand(server.WorkingDirectory);
        return Path.IsPathRooted(server.Executable) ? Expand(server.Executable) : Path.Combine(workingDirectory, server.Executable);
    }

    private static void PrintPanelHeader(string title, string subtitle)
    {
        WriteColor(" LIG Defense & Aerospace LIG AI MCP", ConsoleColor.Cyan);
        WriteColor(" / ", ConsoleColor.DarkGray);
        WriteColorLine(subtitle, ConsoleColor.White);
        WriteColorLine(new string('-', SafeWidth()), ConsoleColor.DarkGray);
        WriteColorLine(title, ConsoleColor.Yellow);
        Console.WriteLine();
    }

    private static void WriteField(string name, string value)
    {
        WriteColor($"{name,-12}", ConsoleColor.DarkGray);
        WriteColorLine(value, ConsoleColor.White);
    }

    private static void WaitBack()
    {
        WriteColorLine(new string('-', SafeWidth()), ConsoleColor.DarkGray);
        WriteColor("B/Enter: 뒤로", ConsoleColor.DarkGray);
        if (Console.IsInputRedirected)
        {
            Console.WriteLine();
            return;
        }
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.B or ConsoleKey.Enter or ConsoleKey.Escape)
                return;
        }
    }

    private static bool TryGetCursorVisible()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        try { return Console.CursorVisible; }
        catch { return true; }
    }

    private static void PrepareInteractiveConsole()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || !OperatingSystem.IsWindows())
            return;

        try { Console.CursorVisible = false; } catch { }

        try
        {
            var width = Math.Max(60, Console.WindowWidth);
            var height = Math.Max(10, Console.WindowHeight);
            if (Console.BufferWidth != width || Console.BufferHeight != height)
                Console.SetBufferSize(width, height);
        }
        catch
        {
        }
    }

    private static void RestoreInteractiveConsole(bool cursorWasVisible)
    {
        if (Console.IsOutputRedirected || !OperatingSystem.IsWindows())
            return;

        try { Console.CursorVisible = cursorWasVisible; } catch { }
    }

    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
                Console.SetCursorPosition(0, 0);
            }
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

    private static ConsoleColor RuntimeColor(StatusRow row)
    {
        if (IsServerActive(row))
            return ConsoleColor.Green;
        return RuntimeColor(row.Runtime);
    }

    private static string FormatRuntime(string runtime)
    {
        if (runtime.StartsWith("Up ", StringComparison.OrdinalIgnoreCase))
            return "docker 실행";
        if (string.Equals(runtime, "stopped", StringComparison.OrdinalIgnoreCase))
            return "중지됨";
        return runtime.Length <= 12 ? runtime : runtime[..12];
    }

    private static string FormatRuntime(StatusRow row)
    {
        if (string.Equals(row.Runtime, "stopped", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Health, "healthy", StringComparison.OrdinalIgnoreCase))
            return "외부 실행";
        return FormatRuntime(row.Runtime);
    }

    private static bool IsRuntimeRunning(string runtime) =>
        runtime.StartsWith("pid ", StringComparison.OrdinalIgnoreCase) ||
        runtime.StartsWith("Up", StringComparison.OrdinalIgnoreCase);

    private static bool IsServerActive(StatusRow row) =>
        IsRuntimeRunning(row.Runtime) ||
        string.Equals(row.Health, "healthy", StringComparison.OrdinalIgnoreCase);

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

    private static void WriteMetric(string label, string value, ConsoleColor color)
    {
        WriteColor($" {label} ", ConsoleColor.DarkGray);
        WriteColor(value.PadRight(4), color);
    }

    private static void WriteChip(string label, string value, ConsoleColor foreground, ConsoleColor background)
    {
        var originalForeground = Console.ForegroundColor;
        var originalBackground = Console.BackgroundColor;
        try
        {
            Console.ForegroundColor = foreground;
            Console.BackgroundColor = background;
            Console.Write($" {label} {value.PadLeft(2)} ");
        }
        finally
        {
            Console.ForegroundColor = originalForeground;
            Console.BackgroundColor = originalBackground;
        }
        Console.Write("  ");
    }

    private static void WriteCell(string value, int width, ConsoleColor foreground, ConsoleColor? background)
    {
        var originalForeground = Console.ForegroundColor;
        var originalBackground = Console.BackgroundColor;
        try
        {
            Console.ForegroundColor = foreground;
            if (background.HasValue)
                Console.BackgroundColor = background.Value;
            Console.Write(PadCell(value, width));
        }
        finally
        {
            Console.ForegroundColor = originalForeground;
            Console.BackgroundColor = originalBackground;
        }
    }

    private static string PadCell(string value, int width)
    {
        var trimmed = TrimCell(value, width - 1);
        var padding = Math.Max(0, width - DisplayWidth(trimmed));
        return trimmed + new string(' ', padding);
    }

    private static ConsoleColor MessageColor(string message)
    {
        if (message.Contains("오류", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("실패", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("찾을 수", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (message.Contains("완료", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("준비", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        return ConsoleColor.Yellow;
    }

    private static void WriteKeyValueLine(string key, string value, string right)
    {
        var width = SafeWidth();
        var left = $" {key,-4} {value}";
        if (!string.IsNullOrWhiteSpace(right))
        {
            var gap = Math.Max(1, width - DisplayWidth(left) - DisplayWidth(right) - 1);
            left = left + new string(' ', gap) + right;
        }
        WriteColorLine(TrimCell(left, width), ConsoleColor.Gray);
    }

    private static string CenterText(string text, int width)
    {
        var textWidth = DisplayWidth(text);
        if (textWidth >= width)
            return TrimCell(text, width);
        var left = (width - textWidth) / 2;
        return new string(' ', left) + text;
    }

    private static int SafeWidth()
    {
        try { return Math.Clamp(Console.WindowWidth - 3, 50, 150); }
        catch { return 100; }
    }

    private static int SafeHeight()
    {
        try { return Math.Clamp(Console.WindowHeight, 10, 80); }
        catch { return 40; }
    }

    private static string ShortenPath(string path, int maxLength)
    {
        if (DisplayWidth(path) <= maxLength) return path;
        var fileName = Path.GetFileName(path);
        var fileNameWidth = DisplayWidth(fileName);
        if (fileNameWidth + 4 >= maxLength)
            return "..." + TrimCell(path, Math.Max(1, maxLength - 3));
        var prefix = TrimCell(path, Math.Max(1, maxLength - fileNameWidth - 4));
        return prefix + "..." + Path.DirectorySeparatorChar + fileName;
    }

    private static string TrimCell(string value, int maxLength)
    {
        value = value.Replace('\t', ' ').Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        if (maxLength <= 0)
            return "";
        if (DisplayWidth(value) <= maxLength)
            return value;

        var builder = new StringBuilder();
        var width = 0;
        foreach (var c in value)
        {
            var charWidth = CharDisplayWidth(c);
            if (width + charWidth >= maxLength)
                break;
            builder.Append(c);
            width += charWidth;
        }
        return builder + "~";
    }

    private static int DisplayWidth(string value)
    {
        var width = 0;
        foreach (var c in value)
            width += CharDisplayWidth(c);
        return width;
    }

    private static int CharDisplayWidth(char c)
    {
        if (char.IsControl(c))
            return 0;
        return IsWideChar(c) ? 2 : 1;
    }

    private static bool IsWideChar(char c)
    {
        var code = c;
        return code is >= '\u1100' and <= '\u115F' ||
               code is >= '\u2329' and <= '\u232A' ||
               code is >= '\u2E80' and <= '\uA4CF' ||
               code is >= '\uAC00' and <= '\uD7A3' ||
               code is >= '\uF900' and <= '\uFAFF' ||
               code is >= '\uFE10' and <= '\uFE19' ||
               code is >= '\uFE30' and <= '\uFE6F' ||
               code is >= '\uFF00' and <= '\uFF60' ||
               code is >= '\uFFE0' and <= '\uFFE6';
    }

    private async Task<ManagerConfig> LoadConfig()
    {
        if (!File.Exists(_configPath))
            throw new FileNotFoundException("매니저 설정 파일을 찾을 수 없습니다.", _configPath);

        var json = await File.ReadAllTextAsync(_configPath);
        var config = JsonSerializer.Deserialize<ManagerConfig>(json, JsonOptions()) ?? throw new InvalidOperationException("매니저 설정 파일 형식이 올바르지 않습니다.");
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
            throw new FileNotFoundException($"{server.Name} 실행 파일을 찾을 수 없습니다. 먼저 Windows 호스트 패키지를 publish 하세요.", executable);

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

        var process = StartManagedProcess(server.Name, executable, psi);
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
            WriteColor($"{FormatRuntime(row),-13}", RuntimeColor(row));
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
            return "docker 시간초과";
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
            throw new InvalidOperationException($"{fileName} 종료 코드 {result.ExitCode}: {result.Stderr}");
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
        using var process = StartManagedProcess(fileName, fileName, psi);
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

    private static Process StartManagedProcess(string displayName, string executable, ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException($"{displayName} 시작에 실패했습니다.");
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"{displayName} 실행 파일을 시작할 수 없습니다. 경로/PATH/권한을 확인하세요: {executable}. {ex.Message}", executable, ex);
        }
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
        LIG AI MCP - LIG Defense & Aerospace

        사용법:
          mcp-manager list [all|docker|windows|group|name]
          mcp-manager start [all|docker|windows|group|name]
          mcp-manager stop [all|docker|windows|group|name]
          mcp-manager restart [all|docker|windows|group|name]
          mcp-manager status [all|docker|windows|group|name]
          mcp-manager health [all|docker|windows|group|name]
          mcp-manager logs <name>
          mcp-manager urls [all|docker|windows|group|name]
          mcp-manager env <name>
          mcp-manager set-env <name> <KEY> <VALUE>
          mcp-manager remove-env <name> <KEY>

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

internal readonly record struct EnvEntry(string Key, string Value);

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

internal enum DashboardKey
{
    Up,
    Down,
    Home,
    End,
    Details,
    StartSelected,
    StopSelected,
    RestartSelected,
    StartAll,
    StopAll,
    RestartAll,
    Refresh,
    UrlsSelected,
    UrlsAll,
    Logs,
    Env,
    Quit
}
