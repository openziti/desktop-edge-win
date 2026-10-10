using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// One line per test step on the console, so a CI log reads as what each test did. Tests run one at a time, so the
/// lines land under that test's START line. Local time, like ui.log. Lines crossing a pipe say who sent them, the
/// app (UI:) or ZET (ZET:).
/// </summary>
public static class Step
{
    public static void Log(string message) =>
        Console.WriteLine($"    {DateTime.Now:HH:mm:ss.fff} {message}");

    public static void UiSent(JObject command)
    {
        string? identifier = (string?)command["Data"]?["Identifier"];
        Log(identifier == null
            ? $"UI: sending {command["Command"]}"
            : $"UI: sending {command["Command"]} for \"{identifier}\"");
    }

    public static void ZetReplied(JObject reply) =>
        Log($"ZET: reply Success:{reply["Success"]} Code:{reply["Code"]}");

    public static void ZetEvent(JObject evt) =>
        Log($"ZET: event {evt["Op"]} {evt["Action"]}");
}
