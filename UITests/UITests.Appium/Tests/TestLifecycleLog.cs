using System.Reflection;
using Xunit.Sdk;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// xUnit before/after attribute that logs which test is starting and how long
/// it took to the console. Applied at the class level so every [Fact] in the
/// class gets the bookend lines -- makes it obvious which test is hanging when
/// the suite gets stuck.
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
        var dur = DateTime.UtcNow - _start;
        Console.WriteLine($"<=== DONE  : {methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name} in {dur.TotalSeconds:F1}s");
    }
}
