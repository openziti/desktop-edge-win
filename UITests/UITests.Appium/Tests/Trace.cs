using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Per-test timing trace, on when ZDEW_TEST_TRACE=1: every Mark and Time call writes
/// `[TRACE] t=&lt;total&gt;ms +&lt;step&gt;ms &lt;op&gt;` to stderr, because xUnit swallows stdout for passing tests.
/// The stopwatch is AsyncLocal so it follows a test across awaits, and it starts on the first call.
/// </summary>
public static class Trace
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("ZDEW_TEST_TRACE") == "1";

    /// <summary>
    /// Scales every Settle from ZDEW_SETTLE_MULT (default 1). Setting 0.5 shows which settles are needed without a
    /// rebuild.
    /// </summary>
    public static readonly double SettleMultiplier =
        double.TryParse(Environment.GetEnvironmentVariable("ZDEW_SETTLE_MULT"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double m) && m > 0
            ? m
            : 1.0;

    /// <summary>Fixed wait for an animation to settle, labeled in the trace by its call site.</summary>
    public static async Task Settle(int targetMs,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        int ms = (int)(targetMs * SettleMultiplier);
        if (ms < 1) ms = 1;
        string fileName = string.IsNullOrEmpty(file) ? "?" : Path.GetFileNameWithoutExtension(file);
        string label = $"Settle({targetMs}ms) {fileName}:{caller}:{line}";
        await TimeAsync(label, () => Task.Delay(ms));
    }

    private static readonly AsyncLocal<Stopwatch?> _sw = new();
    private static readonly AsyncLocal<long> _lastTickMs = new();
    private static readonly AsyncLocal<string?> _testName = new();

    /// <summary>Optional: reset the timeline and print a banner with the test name.</summary>
    public static void Begin([CallerMemberName] string? testName = null)
    {
        if (!Enabled) return;
        _sw.Value = Stopwatch.StartNew();
        _lastTickMs.Value = 0;
        _testName.Value = testName;
        Console.Error.WriteLine($"[TRACE] t=    0ms  +   0ms  ===> {testName}");
    }

    private static Stopwatch? GetOrStartSw()
    {
        if (!Enabled) return null;
        Stopwatch? sw = _sw.Value;
        if (sw == null)
        {
            sw = Stopwatch.StartNew();
            _sw.Value = sw;
            _lastTickMs.Value = 0;
        }
        return sw;
    }

    public static void Mark(string label)
    {
        Stopwatch? sw = GetOrStartSw();
        if (sw == null) return;
        long total = sw.ElapsedMilliseconds;
        long step = total - _lastTickMs.Value;
        _lastTickMs.Value = total;
        Console.Error.WriteLine($"[TRACE] t={total,5}ms  +{step,4}ms  {label}");
    }

    public static T Time<T>(string label, Func<T> op)
    {
        Stopwatch? sw = GetOrStartSw();
        if (sw == null) return op();
        long t0 = sw.ElapsedMilliseconds;
        T result = op();
        long t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
        return result;
    }

    public static void Time(string label, Action op)
    {
        Stopwatch? sw = GetOrStartSw();
        if (sw == null) { op(); return; }
        long t0 = sw.ElapsedMilliseconds;
        op();
        long t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
    }

    public static async Task<T> TimeAsync<T>(string label, Func<Task<T>> op)
    {
        Stopwatch? sw = GetOrStartSw();
        if (sw == null) return await op();
        long t0 = sw.ElapsedMilliseconds;
        T result = await op();
        long t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
        return result;
    }

    public static async Task TimeAsync(string label, Func<Task> op)
    {
        Stopwatch? sw = GetOrStartSw();
        if (sw == null) { await op(); return; }
        long t0 = sw.ElapsedMilliseconds;
        await op();
        long t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
    }
}
