using Windows.UI.Notifications;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// The OS toasts an app exe raised. A toast left on screen covers the bottom of the next session's docked window, and a
/// click that lands on it makes Windows start another instance of the exe (`-ToastActivated -Embedding`, parented by
/// svchost) outside the test, which takes the foreground and hides the window under test.
/// </summary>
internal static class AppToasts
{
    /// <summary>
    /// The id Windows files an exe's toasts under. Microsoft.Toolkit.Uwp.Notifications uses the exe's full path with `/`
    /// separators, because the id is also a registry key name, which cannot hold a backslash.
    /// </summary>
    public static string ToastAppId(string exePath) => Path.GetFullPath(exePath).Replace('\\', '/');

    /// <summary>Remove every toast exePath raised, from the screen and from the notification center.</summary>
    public static void Clear(string exePath)
    {
        string appId = ToastAppId(exePath);
        ToastNotificationManager.History.Clear(appId);
        int left = ToastNotificationManager.History.GetHistory(appId).Count;
        if (left != 0)
            throw new InvalidOperationException($"{left} toasts of '{appId}' still in the notification history after Clear");
    }
}
