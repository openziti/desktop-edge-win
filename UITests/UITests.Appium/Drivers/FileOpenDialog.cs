using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ZitiDesktopEdge.UITests.Drivers;

// Win32 messages, because the Open dialog lies outside the WPF UIA tree the session's locators walk.
public static class FileOpenDialog
{
    // The File name box's ComboBoxEx32 (cmb13 in dlgs.h), which keeps this id in the Vista style dialog.
    private const int FileNameComboId = 0x47C;
    private const int IdOk = 1;
    private const uint WmSetText = 0x000C;
    private const uint WmGetText = 0x000D;
    private const uint WmCommand = 0x0111;
    private const uint GwEnabledPopup = 6;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static IntPtr? Find(int processId, string title) =>
        AppiumSession.TopLevelWindows().FirstOrDefault(w => w.ProcessId == processId && w.Title == title)?.Handle;

    // The window and its title come before the folder view, and a path set or Open posted before that can be lost.
    public static bool IsReady(IntPtr dialog) =>
        Win32Window.IsWindowVisible(dialog) && IsWindowEnabled(dialog) && HasDescendant(dialog, "SHELLDLL_DefView");

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
        while (Win32Window.IsWindow(dialog))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"the Open dialog stayed open {timeout.TotalSeconds}s after Open with '{path}': {State(dialog, edit)}");
            Thread.Sleep(PollInterval);
        }
    }

    private static string State(IntPtr dialog, IntPtr edit)
    {
        IntPtr popup = GetWindow(dialog, GwEnabledPopup);
        string popupState;
        if (popup == IntPtr.Zero || popup == dialog)
            popupState = "no popup";
        else
            popupState = $"popup '{WindowText(popup)}' reads '{string.Join(" ", StaticTexts(popup))}'";
        return $"File name box '{WindowText(edit)}', dialog enabled {IsWindowEnabled(dialog)}, {popupState}";
    }

    private static bool HasDescendant(IntPtr parent, string className)
    {
        bool found = false;
        EnumChildWindows(parent, (child, _) =>
        {
            found = Win32Window.ClassName(child) == className;
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    private static IReadOnlyList<string> StaticTexts(IntPtr parent)
    {
        List<string> texts = new List<string>();
        EnumChildWindows(parent, (child, _) =>
        {
            if (Win32Window.ClassName(child) == "Static" && WindowText(child).Length > 0)
                texts.Add(WindowText(child));
            return true;
        }, IntPtr.Zero);
        return texts;
    }

    private static string WindowText(IntPtr hWnd)
    {
        StringBuilder text = new StringBuilder(1024);
        SendMessage(hWnd, WmGetText, (IntPtr)text.Capacity, text);
        return text.ToString();
    }

    private static IntPtr FileNameEdit(IntPtr dialog)
    {
        IntPtr combo = IntPtr.Zero;
        EnumChildWindows(dialog, (child, _) =>
        {
            if (GetDlgCtrlID(child) != FileNameComboId || Win32Window.ClassName(child) != "ComboBoxEx32")
                return true;
            combo = child;
            return false;
        }, IntPtr.Zero);
        if (combo == IntPtr.Zero)
            throw new InvalidOperationException("the Open dialog has no File name ComboBoxEx32");
        IntPtr inner = FindWindowEx(combo, IntPtr.Zero, "ComboBox", null);
        if (inner == IntPtr.Zero)
            throw new InvalidOperationException("the Open dialog's File name ComboBoxEx32 has no ComboBox");
        IntPtr edit = FindWindowEx(inner, IntPtr.Zero, "Edit", null);
        if (edit == IntPtr.Zero)
            throw new InvalidOperationException("the Open dialog's File name ComboBox has no Edit");
        return edit;
    }

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
}
