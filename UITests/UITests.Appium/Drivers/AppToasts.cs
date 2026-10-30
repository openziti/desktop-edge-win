using Windows.UI.Notifications;

namespace ZitiDesktopEdge.UITests.Drivers;

// A leftover toast covers the next docked window, and a click on it starts an instance that takes the foreground.
internal static class AppToasts
{
    public static void Clear(string exePath)
    {
        // The id is also a registry key name, which cannot hold a backslash.
        string appId = Path.GetFullPath(exePath).Replace('\\', '/');
        ToastNotificationManager.History.Clear(appId);
        int left = ToastNotificationManager.History.GetHistory(appId).Count;
        if (left != 0)
            throw new InvalidOperationException($"{left} toasts of '{appId}' still in the notification history after Clear");
    }
}
