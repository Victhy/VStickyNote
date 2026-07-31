using Microsoft.Win32;

namespace VSticky.Services;

/// <summary>通过 HKCU\...\Run 管理开机启动。</summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "VSticky";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (!enabled)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;

            key.SetValue(AppName, $"\"{exe}\"");
        }
        catch
        {
            // ignore registry errors
        }
    }
}
