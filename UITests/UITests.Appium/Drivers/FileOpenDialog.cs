using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// Drives the shell's Open dialog that WPF's OpenFileDialog shows, through Win32 messages, because the dialog belongs
/// to the app's process but lies outside the WPF UIA tree the session's locators walk.
/// </summary>
public static class FileOpenDialog
{
    // The File name box's ComboBoxEx32 (cmb13 in dlgs.h), which keeps this id in the Vista style dialog.
    private const int FileNameComboId = 0x47C;
    private const int IdOk = 1;
    private const uint WmSetText = 0x000C;
    private const uint WmGetText = 0x000D;
    private const uint WmCommand = 0x0111;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The visible top-level window of processId titled title, or null when there is none.</summary>
    public static IntPtr? Find(int processId, string title) =>
        AppiumSession.TopLevelWindows().FirstOrDefault(w => w.ProcessId == processId && w.Title == title)?.Handle;

    /// <summary>Type path into the dialog's File name box, press Open, and wait for the dialog to close.</summary>
    public static void Choose(IntPtr dialog, string path, TimeSpan timeout)
    {
        IntPtr edit = FileNameEdit(dialog);
        SendMessage(edit, WmSetText, IntPtr.Zero, path);
        StringBuilder typed = new StringBuilder(path.Length + 1);
        SendMessage(edit, WmGetText, (IntPtr)typed.Capacity, typed);
        if (typed.ToString() != path)
            throw new InvalidOperationException($"the Open dialog's File name box reads '{typed}' after setting '{path}'");
        // Posted, since Open runs the dialog's own validation before it returns.
        if (!PostMessage(dialog, WmCommand, (IntPtr)IdOk, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "posting Open to the Open dialog failed");

        DateTime deadline = DateTime.UtcNow + timeout;
        while (IsWindow(dialog))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"the Open dialog stayed open {timeout.TotalSeconds}s after Open with '{path}'");
            Thread.Sleep(PollInterval);
        }
    }

    private static IntPtr FileNameEdit(IntPtr dialog)
    {
        IntPtr combo = IntPtr.Zero;
        EnumChildWindows(dialog, (child, _) =>
        {
            if (GetDlgCtrlID(child) != FileNameComboId || ClassName(child) != "ComboBoxEx32")
                return true;
            combo = child;
            return false;
        }, IntPtr.Zero);
        if (combo == IntPtr.Zero)
            throw new InvalidOperationException("the Open dialog has no File name ComboBoxEx32");
        IntPtr inner = FindWindowEx(combo, IntPtr.Zero, "ComboBox", null);
        IntPtr edit = inner == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(inner, IntPtr.Zero, "Edit", null);
        if (edit == IntPtr.Zero)
            throw new InvalidOperationException("the Open dialog's File name ComboBoxEx32 has no ComboBox with an Edit");
        return edit;
    }

    private static string ClassName(IntPtr hWnd)
    {
        StringBuilder name = new StringBuilder(64);
        GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}
