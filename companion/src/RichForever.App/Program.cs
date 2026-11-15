namespace RichForever.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)) return Uninstall(quiet: args.Contains("--quiet", StringComparer.OrdinalIgnoreCase));

        var startedByWindows = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, @"Local\RichForever.Companion", out var firstInstance);
        if (!firstInstance)
        {
            if (!startedByWindows) MessageBox.Show("RichForever is already running. Look for its icon in the system tray.", "RichForever");
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);
        Application.Run(new TrayApplication(startedByWindows));
        return 0;
    }

    /// <summary>Remove everything RichForever created outside its own folder.</summary>
    private static int Uninstall(bool quiet)
    {
        var removed = new List<string>();
        if (StartupRegistration.Current() is not null)
        {
            StartupRegistration.Apply(false, "");
            removed.Add($@"HKCU\{StartupRegistration.RunKey}\{StartupRegistration.ValueName}");
        }
        foreach (var directory in new[] { AppPaths.ConfigDirectory, AppPaths.LogDirectory }.Distinct())
        {
            if (!Directory.Exists(directory)) continue;
            Directory.Delete(directory, recursive: true);
            removed.Add(directory);
        }
        if (!quiet)
        {
            var summary = removed.Count == 0 ? "Nothing to remove." : "Removed:\n" + string.Join("\n", removed);
            MessageBox.Show(summary + "\n\nYou can now delete the RichForever folder and the RichForever addon folder.", "RichForever uninstall");
        }
        return 0;
    }
}
