namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// One line per test step on the console, the way ZET's integration tests t.Logf theirs, so a CI log reads as what each
/// test did. Tests run one at a time, so the lines land under that test's START line. Local time, like ui.log.
/// </summary>
public static class Step
{
    public static void Log(string message) =>
        Console.WriteLine($"    {DateTime.Now:HH:mm:ss.fff} {message}");
}
