using System.Diagnostics;
using System.Runtime.InteropServices;
using RichForever.Core;

namespace RichForever.App;

/// <summary>Finds the WoW Forever process and the screen position of its client area.</summary>
internal sealed class GameWatcher : IDisposable
{
    private Process? _process;
    private nint _window;

    public int? ProcessId => _process?.Id;
    public bool HasExited => _process is null || _process.HasExited;

    public bool TryAttach(AppSettings settings)
    {
        if (_process is not null) return true;
        foreach (var processId in FindProcesses(settings.GameProcessNames))
        {
            if (settings.RequireForeverFolder && !GameProcess.IsForeverPath(ImagePath(processId))) continue;
            try
            {
                _process = Process.GetProcessById(processId);
                _window = 0;
                return true;
            }
            catch (ArgumentException)
            {
                // Exited between the snapshot and now.
            }
        }
        return false;
    }

    public void Detach()
    {
        _process?.Dispose();
        _process = null;
        _window = 0;
    }

    /// <summary>
    /// Top-left of the game's client area in physical pixels, if the game window is the topmost
    /// window at that spot (not minimised, not covered). Otherwise the strip cannot be read.
    /// </summary>
    public bool TryGetStripOrigin(out int x, out int y)
    {
        x = y = 0;
        if (_process is null) return false;
        if (_window == 0 || !Native.IsWindow(_window))
        {
            _process.Refresh();
            _window = _process.MainWindowHandle;
            if (_window == 0) return false;
        }
        if (!Native.IsWindowVisible(_window) || Native.IsIconic(_window)) return false;
        if (!Native.GetClientRect(_window, out var client) || client.Right < StripDecoder.Width || client.Bottom < StripDecoder.Height) return false;

        var origin = new Native.Point();
        if (!Native.ClientToScreen(_window, ref origin)) return false;
        var probe = new Native.Point { X = origin.X + 1, Y = origin.Y + 1 };
        var far = new Native.Point { X = origin.X + StripDecoder.Width - 2, Y = origin.Y + StripDecoder.Height - 2 };
        if (Native.GetAncestor(Native.WindowFromPoint(probe), Native.GaRoot) != _window
            || Native.GetAncestor(Native.WindowFromPoint(far), Native.GaRoot) != _window)
        {
            return false;
        }
        x = origin.X;
        y = origin.Y;
        return true;
    }

    public void Dispose() => Detach();

    /// <summary>
    /// Process IDs with a visible top-level window whose executable name (without .exe) is in the
    /// list. Walking windows and querying only their owners is far cheaper than a full process
    /// snapshot, which makes the kernel collect every thread on the system.
    /// </summary>
    private static unsafe List<int> FindProcesses(IReadOnlyCollection<string> names)
    {
        var owners = new HashSet<uint>();
        var handle = GCHandle.Alloc(owners);
        try
        {
            Native.EnumWindows(&CollectOwner, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        var found = new List<int>();
        foreach (var owner in owners)
        {
            var name = Path.GetFileNameWithoutExtension(ImagePath((int)owner));
            if (name is not null && names.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add((int)owner);
        }
        return found;
    }

    [UnmanagedCallersOnly]
    private static int CollectOwner(nint window, nint state)
    {
        if (Native.IsWindowVisible(window) && Native.GetWindowThreadProcessId(window, out var processId) != 0)
        {
            ((HashSet<uint>)GCHandle.FromIntPtr(state).Target!).Add(processId);
        }
        return 1;
    }

    private static string? ImagePath(int processId)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0) return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}
