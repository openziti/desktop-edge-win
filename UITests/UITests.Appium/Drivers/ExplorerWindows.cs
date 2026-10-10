using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// The shell's File Explorer folder windows. One the app opens through Explorer.exe belongs to the shell's explorer
/// process, so it outlives the app and a test has to close it.
/// </summary>
public static class ExplorerWindows
{
    private const string FolderWindowClass = "CabinetWClass";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The visible folder windows, from the top of the z-order down.</summary>
    public static IReadOnlyList<TopLevelWindow> Open() =>
        AppiumSession.TopLevelWindows().Where(w => ClassName(w.Handle) == FolderWindowClass).ToList();

    /// <summary>Close window and wait for it to go.</summary>
    public static void Close(TopLevelWindow window, TimeSpan timeout)
    {
        if (!Win32Window.PostMessage(window.Handle, Win32Window.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"posting WM_CLOSE to Explorer window '{window.Title}' failed");
        DateTime deadline = DateTime.UtcNow + timeout;
        while (Win32Window.IsWindow(window.Handle))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Explorer window '{window.Title}' stayed open {timeout.TotalSeconds}s after WM_CLOSE");
            Thread.Sleep(PollInterval);
        }
    }

    private static string ClassName(IntPtr hWnd)
    {
        StringBuilder name = new StringBuilder(64);
        Win32Window.GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }
}
