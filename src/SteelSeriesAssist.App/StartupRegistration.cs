using Microsoft.Win32;
using System.IO;

namespace SteelSeriesAssist.App;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteelSeriesAssist";

    private static string GetCommand()
    {
        // Use the application host even when development starts it via dotnet.
        var executable = Path.Combine(AppContext.BaseDirectory, "SteelSeriesAssist.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("找不到 SteelSeriesAssist.exe，请使用解压后的完整程序目录。");
        return $"\"{executable}\"";
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, GetCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的启动设置。");
        if (enabled)
            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);

        if (IsEnabled() != enabled)
            throw new IOException("开机启动设置未能保存，请重试。");
    }
}
