using System.Reflection;

namespace ZitiDesktopEdge.UITests.Drivers;

// A folder window belongs to the shell's explorer process, so it outlives the app and a test has to close it.
public static class ExplorerWindows
{
    public static IReadOnlyList<TopLevelWindow> Open() =>
        AppiumSession.TopLevelWindows().Where(window => Win32Window.ClassName(window.Handle) == "CabinetWClass").ToList();

    // A window joins the shell's window list only once it opened its folder. Explorer closes it some time later.
    public static void Quit(TopLevelWindow window, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        object? folderWindow = ShellWindow(window.Handle);
        while (folderWindow == null)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Explorer window '{window.Title}' never opened a folder within {timeout.TotalSeconds}s");
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            folderWindow = ShellWindow(window.Handle);
        }
        folderWindow.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, folderWindow, null);
    }

    private static object? ShellWindow(IntPtr handle)
    {
        Type shellType = Type.GetTypeFromProgID("Shell.Application", true)!;
        object shell = Activator.CreateInstance(shellType)!;
        object windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null)!;
        int count = (int)windows.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, windows, null)!;
        for (int index = 0; index < count; index++)
        {
            object? item = windows.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, windows, new object[] { index });
            if (item != null && Convert.ToInt64(item.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, item, null)) == handle.ToInt64())
                return item;
        }
        return null;
    }
}
