using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

// Local time, like ui.log.
public static class Step
{
    // AsyncLocal so the pipe threads a test starts log into that test's block.
    private static readonly AsyncLocal<ITestOutputHelper?> Output = new();

    public static void Use(ITestOutputHelper output) => Output.Value = output;

    public static void Log(string message)
    {
        ITestOutputHelper output = Output.Value
            ?? throw new InvalidOperationException("Step.Log ran outside a test whose constructor called Step.Use");
        output.WriteLine($"    {DateTime.Now:HH:mm:ss.fff} {message}");
    }

    public static void Sent(string sender, JObject command)
    {
        string? identifier = (string?)command["Data"]?["Identifier"];
        if (identifier == null)
            Log($"{sender}: sending {command["Command"]}");
        else
            Log($"{sender}: sending {command["Command"]} for \"{identifier}\"");
    }
}
