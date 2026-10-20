using System.Reflection;
using System.Text;

namespace ZitiDesktopEdge.UITests.Drivers;

// A folder window belongs to the shell's explorer process, so it outlives the app and a test has to close it.
public static class ExplorerWindows
{
    private const string FolderWindowClass = "CabinetWClass";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<TopLevelWindow> Open() =>
        AppiumSession.TopLevelWindows().Where(w => ClassName(w.Handle) == FolderWindowClass).ToList();

    // A window joins the shell's window list only once it opened its folder. Explorer closes it some time later.
    public static void Quit(TopLevelWindow window, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        object? folderWindow = ShellWindow(window.Handle);
        while (folderWindow == null)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Explorer window '{window.Title}' never opened a folder within {timeout.TotalSeconds}s");
            Thread.Sleep(PollInterval);
            folderWindow = ShellWindow(window.Handle);
        }
        folderWindow.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, folderWindow, null);
    }

    public static bool IsOpen(TopLevelWindow window) => Win32Window.IsWindow(window.Handle);

    private static object? ShellWindow(IntPtr hWnd)
    {
        Type shellType = Type.GetTypeFromProgID("Shell.Application", true)!;
        object shell = Activator.CreateInstance(shellType)!;
        object windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null)!;
        int count = (int)windows.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, windows, null)!;
        for (int i = 0; i < count; i++)
        {
            object? item = windows.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, windows, new object[] { i });
            if (item != null && Convert.ToInt64(item.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, item, null)) == hWnd.ToInt64())
                return item;
        }
        return null;
    }

    private static string ClassName(IntPtr hWnd)
    {
        StringBuilder name = new StringBuilder(64);
        Win32Window.GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }
}
