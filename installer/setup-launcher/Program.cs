using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LigAiMcp.Setup;

internal static partial class Program
{
    private const string ProductName = "LIG AI MCP Server Suite";
    private const string PayloadResource = "LIG.AI.MCP.payload.msi";
    private const uint MessageBoxIconError = 0x10;
    private const uint MessageBoxIconInformation = 0x40;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var quiet = args.Any(argument => string.Equals(argument, "--quiet", StringComparison.OrdinalIgnoreCase));
        string? msiPath = null;
        try
        {
            if (!IsProcessElevated())
                throw new InvalidOperationException("설치 프로그램이 관리자 권한으로 실행되지 않았습니다.");

            var assembly = Assembly.GetExecutingAssembly();
            await using var payload = assembly.GetManifestResourceStream(PayloadResource)
                ?? throw new InvalidOperationException("설치 프로그램에 MSI payload가 없습니다.");

            var installerRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LIG AI MCP",
                "Installer");
            Directory.CreateDirectory(installerRoot);

            msiPath = Path.Combine(installerRoot, $"LIG-AI-MCP-{Guid.NewGuid():N}.msi");
            await using (var output = new FileStream(msiPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await payload.CopyToAsync(output);

            var logPath = Path.Combine(installerRoot, $"LIG-AI-MCP-Setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installerRoot
            };
            startInfo.ArgumentList.Add("/i");
            startInfo.ArgumentList.Add(msiPath);
            startInfo.ArgumentList.Add(quiet ? "/qn" : "/qb!");
            startInfo.ArgumentList.Add("ADDLOCAL=Core");
            startInfo.ArgumentList.Add("/norestart");
            startInfo.ArgumentList.Add("/L*v");
            startInfo.ArgumentList.Add(logPath);

            using var installer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows Installer를 시작하지 못했습니다.");
            await installer.WaitForExitAsync();

            if (installer.ExitCode is 0 or 3010)
            {
                var restartMessage = installer.ExitCode == 3010 ? "\n변경 사항 적용을 위해 Windows를 다시 시작해 주세요." : string.Empty;
                if (!quiet)
                    ShowMessage($"{ProductName} 설치가 완료되었습니다.{restartMessage}", MessageBoxIconInformation);
                return installer.ExitCode;
            }

            if (!quiet)
                ShowMessage($"설치 중 오류가 발생했습니다.\n오류 코드: {installer.ExitCode}\n로그: {logPath}", MessageBoxIconError);
            return installer.ExitCode;
        }
        catch (Exception exception)
        {
            if (!quiet)
                ShowMessage($"설치 프로그램을 실행하지 못했습니다.\n{exception.Message}", MessageBoxIconError);
            return exception is Win32Exception win32Exception ? win32Exception.NativeErrorCode : 1;
        }
        finally
        {
            if (msiPath is not null)
            {
                try
                {
                    File.Delete(msiPath);
                }
                catch
                {
                    // Windows Installer may still have the payload open while it finishes cleanup.
                }
            }
        }
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
