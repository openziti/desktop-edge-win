using System.Collections.ObjectModel;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// Finds the first element with this AutomationId in one UIA search. An XPath walks the whole UIA tree on every call,
/// which costs up to about 700ms per lookup. FindElements makes a single FindElement and returns zero or
/// one element, because WinAppDriver's AccessibilityId FindElements can add an element with an empty ID, which
/// Selenium throws on.
/// </summary>
public sealed class ByAutomationId : By
{
    public ByAutomationId(string automationId)
        : base(context => context.FindElement(MobileBy.AccessibilityId(automationId)),
            context => FirstOrNone(context, automationId))
    {
        Description = $"AutomationId {automationId}";
    }

    private static ReadOnlyCollection<IWebElement> FirstOrNone(ISearchContext context, string automationId)
    {
        try
        {
            return new ReadOnlyCollection<IWebElement>(
                new[] { context.FindElement(MobileBy.AccessibilityId(automationId)) });
        }
        catch (NoSuchElementException)
        {
            return new ReadOnlyCollection<IWebElement>(Array.Empty<IWebElement>());
        }
    }
}
