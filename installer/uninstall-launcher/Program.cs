using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace LigAiMcp.Uninstall;

internal static partial class Program
{
    private const string ProductName = "LIG AI MCP Server Suite";
    private const string UpgradeCode = "{A77A4464-74E0-4B6C-94C4-D15FCBD744E5}";
    private const string CustomArpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LIG-AI-MCP-Server-Suite";
    private const uint ErrorSuccess = 0;
    private const uint ErrorNoMoreItems = 259;
    private const uint MoveFileDelayUntilReboot = 0x4;
    private const uint MessageBoxIconError = 0x10;
    private const uint MessageBoxIconInformation = 0x40;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var quiet = args.Any(argument => string.Equals(argument, "--quiet", StringComparison.OrdinalIgnoreCase));
        var execute = args.Any(argument => string.Equals(argument, "--execute", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (!IsProcessElevated())
                throw new InvalidOperationException("제거 프로그램이 관리자 권한으로 실행되지 않았습니다.");

            if (!execute)
                return LaunchStagedCopy(args);

            await WaitForParentExit(args);
            StopInstalledProcesses();

            var productCode = FindNewestInstalledProduct();
            if (productCode is null)
            {
                RemoveCustomArpEntry();
                CleanupApplicationData();
                if (!quiet)
                    ShowMessage("LIG AI MCP는 이미 제거되어 있습니다.", MessageBoxIconInformation);
                return 0;
            }

            var logPath = Path.Combine(Path.GetTempPath(), $"LIG-AI-MCP-Uninstall-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/x");
            startInfo.ArgumentList.Add(productCode);
            startInfo.ArgumentList.Add(quiet ? "/qn" : "/qb!");
            startInfo.ArgumentList.Add("/norestart");
            startInfo.ArgumentList.Add("/L*v");
            startInfo.ArgumentList.Add(logPath);

            using var installer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows Installer를 시작하지 못했습니다.");
            await installer.WaitForExitAsync();

            if (installer.ExitCode is 0 or 1605 or 3010)
            {
                CleanupApplicationData();
                var restartMessage = installer.ExitCode == 3010 ? "\n완전한 정리를 위해 Windows를 다시 시작해 주세요." : string.Empty;
                if (!quiet)
                    ShowMessage($"{ProductName} 제거가 완료되었습니다.{restartMessage}", MessageBoxIconInformation);
                return installer.ExitCode;
            }

            if (!quiet)
                ShowMessage($"제거 중 오류가 발생했습니다.\n오류 코드: {installer.ExitCode}\n로그: {logPath}", MessageBoxIconError);
            return installer.ExitCode;
        }
        catch (Exception exception)
        {
            if (!quiet)
                ShowMessage($"제거 프로그램을 실행하지 못했습니다.\n{exception.Message}", MessageBoxIconError);
            return exception is Win32Exception win32Exception ? win32Exception.NativeErrorCode : 1;
        }
        finally
        {
            if (execute)
                ScheduleStagedCleanup();
        }
    }

    private static int LaunchStagedCopy(string[] args)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("제거 프로그램 경로를 확인하지 못했습니다.");
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"LIG-AI-MCP-Uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var staged = Path.Combine(stagingDirectory, "LIG-AI-MCP-Uninstall.exe");
        File.Copy(source, staged, true);

        var startInfo = new ProcessStartInfo
        {
            FileName = staged,
            UseShellExecute = false,
            WorkingDirectory = stagingDirectory
        };
        startInfo.ArgumentList.Add("--execute");
        startInfo.ArgumentList.Add("--parent");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        if (args.Any(argument => string.Equals(argument, "--quiet", StringComparison.OrdinalIgnoreCase)))
            startInfo.ArgumentList.Add("--quiet");

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("임시 제거 프로그램을 시작하지 못했습니다.");
        return 0;
    }

    private static async Task WaitForParentExit(string[] args)
    {
        var parentIndex = Array.FindIndex(args, argument => string.Equals(argument, "--parent", StringComparison.OrdinalIgnoreCase));
        if (parentIndex < 0 || parentIndex + 1 >= args.Length || !int.TryParse(args[parentIndex + 1], out var parentId))
            return;

        try
        {
            using var parent = Process.GetProcessById(parentId);
            await parent.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // The original installed uninstaller has already exited.
        }
    }

    private static void StopInstalledProcesses()
    {
        var installRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LIG AI MCP"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                    continue;
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable is null || !Path.GetFullPath(executable).StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                        continue;
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch
                {
                    // Windows Installer will report any file that still cannot be removed.
                }
            }
        }
    }

    private static string? FindNewestInstalledProduct()
    {
        var products = new List<(Version Version, string ProductCode)>();
        for (uint index = 0; ; index++)
        {
            var productCode = new StringBuilder(39);
            var result = NativeMethods.MsiEnumRelatedProducts(UpgradeCode, 0, index, productCode);
            if (result == ErrorNoMoreItems)
                break;
            if (result != ErrorSuccess)
                throw new Win32Exception((int)result);

            var versionBuffer = new StringBuilder(64);
            uint versionLength = (uint)versionBuffer.Capacity;
            result = NativeMethods.MsiGetProductInfo(productCode.ToString(), "VersionString", versionBuffer, ref versionLength);
            var version = result == ErrorSuccess && Version.TryParse(versionBuffer.ToString(), out var parsed) ? parsed : new Version();
            products.Add((version, productCode.ToString()));
        }

        return products.OrderByDescending(product => product.Version).Select(product => product.ProductCode).FirstOrDefault();
    }

    private static void RemoveCustomArpEntry()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(CustomArpKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // The MSI normally owns and removes this registration.
        }
    }

    private static void CleanupApplicationData()
    {
        DeleteKnownDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LIG AI MCP"));
        DeleteKnownDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LIG AI MCP"));
    }

    private static void DeleteKnownDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Installation files are already removed; locked logs can be cleared after restart.
        }
    }

    private static void ScheduleStagedCleanup()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return;
        _ = NativeMethods.MoveFileEx(executable, null, MoveFileDelayUntilReboot);
        var directory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(directory))
            _ = NativeMethods.MoveFileEx(directory, null, MoveFileDelayUntilReboot);
    }

    private static bool IsProcessElevated()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), 0x0008, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var elevation = Marshal.SizeOf<TokenElevation>();
            if (!NativeMethods.GetTokenInformation(token, 20, out var information, elevation, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return information.TokenIsElevated != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static void ShowMessage(string message, uint icon) =>
        NativeMethods.MessageBox(IntPtr.Zero, message, ProductName, icon | 0x00010000 | 0x00040000);

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    private static partial class NativeMethods
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint MsiGetProductInfo(string productCode, string property, StringBuilder valueBuffer, ref uint valueLength);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool MoveFileEx(string existingFile, string? newFile, uint flags);

        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GetCurrentProcess();

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetTokenInformation(IntPtr token, int informationClass, out TokenElevation information, int informationLength, out int returnLength);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int MessageBox(IntPtr window, string text, string caption, uint type);
    }
}
