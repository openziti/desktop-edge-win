using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// One line per test step on the console, so a CI log reads as what each test did. Tests run one at a time, so the
/// lines land under that test's START line. Local time, like ui.log.
/// </summary>
public static class Step
{
    public static void Log(string message) =>
        Console.WriteLine($"    {DateTime.Now:HH:mm:ss.fff} {message}");

    public static void LogSent(JObject command)
    {
        string? identifier = (string?)command["Data"]?["Identifier"];
        Log(identifier == null ? $"sending {command["Command"]}" : $"sending {command["Command"]} for \"{identifier}\"");
    }
}
