using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Per-test timing trace. When the ZDEW_TEST_TRACE environment variable is set
/// to "1", every Mark / Time call writes a `[TRACE] t=<total>ms +<step>ms <op>`
/// line. Goes to STDERR (Console.Error) -- xUnit only captures stdout for
/// passing tests, so writing to stderr bypasses that capture and lines surface
/// in the live console + run-output.txt regardless of pass/fail.
///
/// The stopwatch and "last tick" are AsyncLocal -- they flow across await
/// boundaries (xUnit async tests can resume on a different thread). The first
/// Mark/Time call in a test auto-starts the stopwatch; no Begin() required.
/// Use Begin() explicitly only if you want a labelled banner line.
/// </summary>
public static class Trace
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("ZDEW_TEST_TRACE") == "1";

    /// <summary>
    /// Multiplier applied to every Settle() call. Default 1.0 (use the
    /// configured target ms). Set ZDEW_SETTLE_MULT=0.5 to halve every settle
    /// wait globally and see what breaks -- a no-build way to A/B-test which
    /// animation settles are actually required.
    /// </summary>
    public static readonly double SettleMultiplier =
        double.TryParse(Environment.GetEnvironmentVariable("ZDEW_SETTLE_MULT"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0
            ? m
            : 1.0;

    /// <summary>
    /// Fixed-time wait for an animation / async UI update to settle.
    /// Auto-labeled by caller member + line number so each call site in the
    /// trace is uniquely identifiable. ZDEW_SETTLE_MULT scales every settle
    /// globally (e.g. =0.5 to halve them all and see what breaks).
    /// </summary>
    public static async Task Settle(int targetMs,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        var ms = (int)(targetMs * SettleMultiplier);
        if (ms < 1) ms = 1;
        var fileName = string.IsNullOrEmpty(file) ? "?" : Path.GetFileNameWithoutExtension(file);
        var label = $"Settle({targetMs}ms) {fileName}:{caller}:{line}";
        await TimeAsync(label, () => Task.Delay(ms));
    }

    private static readonly AsyncLocal<Stopwatch?> _sw = new();
    private static readonly AsyncLocal<long> _lastTickMs = new();
    private static readonly AsyncLocal<string?> _testName = new();

    /// <summary>
    /// Optional: print a banner and reset the timeline. Each test that calls
    /// this gets a clean `===> testName` header. Tests that don't call it just
    /// get implicit auto-start on first Mark/Time.
    /// </summary>
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
        var sw = _sw.Value;
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
        var sw = GetOrStartSw();
        if (sw == null) return;
        var total = sw.ElapsedMilliseconds;
        var step = total - _lastTickMs.Value;
        _lastTickMs.Value = total;
        Console.Error.WriteLine($"[TRACE] t={total,5}ms  +{step,4}ms  {label}");
    }

    public static T Time<T>(string label, Func<T> op)
    {
        var sw = GetOrStartSw();
        if (sw == null) return op();
        var t0 = sw.ElapsedMilliseconds;
        var result = op();
        var t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
        return result;
    }

    public static void Time(string label, Action op)
    {
        var sw = GetOrStartSw();
        if (sw == null) { op(); return; }
        var t0 = sw.ElapsedMilliseconds;
        op();
        var t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
    }

    public static async Task<T> TimeAsync<T>(string label, Func<Task<T>> op)
    {
        var sw = GetOrStartSw();
        if (sw == null) return await op();
        var t0 = sw.ElapsedMilliseconds;
        var result = await op();
        var t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
        return result;
    }

    public static async Task TimeAsync(string label, Func<Task> op)
    {
        var sw = GetOrStartSw();
        if (sw == null) { await op(); return; }
        var t0 = sw.ElapsedMilliseconds;
        await op();
        var t1 = sw.ElapsedMilliseconds;
        _lastTickMs.Value = t1;
        Console.Error.WriteLine($"[TRACE] t={t1,5}ms  +{t1 - t0,4}ms  {label}");
    }
}
