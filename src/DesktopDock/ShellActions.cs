using System.Diagnostics;
using System.Runtime.Versioning;

using DesktopDock.Core;

namespace DesktopDock;

/// <summary>Opening things, showing them in Explorer, and starting with Windows.</summary>
internal static class ShellActions
{
    private const string RunKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DesktopDock";

    /// <summary>The dock's own executable, used for the autostart entry.</summary>
    public static string ExecutablePath => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

    public static void Launch(Pin pin)
    {
        string target = pin.IsLink
            ? DropParser.EnsureScheme(pin.Target)
            : Environment.ExpandEnvironmentVariables(pin.Target);

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    public static void Reveal(string target)
    {
        string path = Environment.ExpandEnvironmentVariables(target ?? string.Empty);
        if (path.Length == 0)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
            else
            {
                // The switch and the path have to arrive as a single argument.
                Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            }
        }
        else
        {
            string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? ".";
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }
    }

    public static void OpenDataFile() =>
        Process.Start(new ProcessStartInfo(PinStore.DefaultPath) { UseShellExecute = true })?.Dispose();

    [SupportedOSPlatform("windows")]
    public static bool IsAutostartEnabled()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(
                "reg.exe", $"query \"{RunKeyPath}\" /v {RunValueName}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(4000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Adds or removes the Run entry that starts the dock at login.</summary>
    [SupportedOSPlatform("windows")]
    public static void SetAutostart(bool enabled)
    {
        string arguments = enabled
            ? $"add \"{RunKeyPath}\" /v {RunValueName} /t REG_SZ /d \"\\\"{ExecutablePath}\\\"\" /f"
            : $"delete \"{RunKeyPath}\" /v {RunValueName} /f";

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("reg.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(4000);
        }
        catch (Exception)
        {
            // Nothing we can do about a locked registry; the dock still runs.
        }
    }
}
