using Microsoft.Win32;

namespace RichForever.App;

internal static class AppPaths
{
    public static string ConfigDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RichForever");
    public static string LogDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RichForever");
    public static string Settings => Path.Combine(ConfigDirectory, "settings.json");
    public static string AssetOverride => Path.Combine(ConfigDirectory, "assets.json");
    public static string Log => Path.Combine(LogDirectory, "richforever.log");
}

/// <summary>Small file log: state changes and errors only, capped at 1 MB with one rollover file.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Sync = new();

    public static bool Verbose { get; set; }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Debug(string message)
    {
        if (Verbose) Write("DEBUG", message);
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                var file = new FileInfo(AppPaths.Log);
                if (file.Exists && file.Length > MaxBytes) File.Move(file.FullName, file.FullName + ".1", overwrite: true);
                File.AppendAllText(AppPaths.Log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// "Start with Windows" through the per-user Run key (no administrator rights, no scheduled task).
/// The value is removed when the setting is turned off and by --uninstall.
/// </summary>
internal static class StartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "RichForever";

    public static string Command(string executable) => $"\"{executable}\" --startup";

    public static void Apply(bool enable, string executable, string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        if (enable)
        {
            var command = Command(executable);
            if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static string? Current(string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) as string;
    }
}
