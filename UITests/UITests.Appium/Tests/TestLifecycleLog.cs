using System.Reflection;
using Xunit.Sdk;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Logs START and DONE lines around each test, so a hung suite shows which test is stuck.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TestLifecycleLogAttribute : BeforeAfterTestAttribute
{
    private DateTime _start;

    public override void Before(MethodInfo methodUnderTest)
    {
        _start = DateTime.UtcNow;
        Console.WriteLine($"===> START : {methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name}");
    }

    public override void After(MethodInfo methodUnderTest)
    {
        TimeSpan dur = DateTime.UtcNow - _start;
        Console.WriteLine($"<=== DONE  : {methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name} in {dur.TotalSeconds:F1}s");
    }
}
