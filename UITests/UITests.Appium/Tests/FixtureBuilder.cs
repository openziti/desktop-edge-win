using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Status fixtures too large or varied for a committed JSON file, e.g. 50 identities with mixed states.
/// </summary>
public static class FixtureBuilder
{
    /// <summary>
    /// Five identities with mixed case and mixed states (enabled, disabled, MFA enabled, ext-auth needed) for the
    /// sort tests. Five, not more, because each PageSource fetch dominates a sort test (about 1-2s for 5 rows,
    /// 3-5s for 15). The names span A to Z so case-insensitive ordering has anchors at both ends.
    /// </summary>
    public static JObject SortableMixed()
    {
        JObject status = SkeletonStatus();
        JArray arr = (JArray)status["Identities"]!;
        JObject[] entries = new[]
        {
            Identity("zebra-prod",    active: true,  mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
            Identity("Bravo-Staging", active: false, mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
            Identity("ALPHA-DEV",     active: true,  mfaEnabled: true,  mfaNeeded: false, needsExtAuth: false),
            Identity("oscar-prod",    active: true,  mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
            Identity("CharlieEdge",   active: false, mfaEnabled: false, mfaNeeded: false, needsExtAuth: true),
        };
        foreach (JObject e in entries) arr.Add(e);
        return status;
    }

    public static JObject ManyMixedIdentities(int count)
    {
        JObject status = SkeletonStatus();
        JArray arr = (JArray)status["Identities"]!;

        for (int i = 0; i < count; i++)
        {
            JObject id = (i % 4) switch
            {
                0 => Identity($"enabled-{i:D2}", active: true, mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
                1 => Identity($"disabled-{i:D2}", active: false, mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
                2 => Identity($"mfa-required-{i:D2}", active: true, mfaEnabled: true, mfaNeeded: true, needsExtAuth: false),
                _ => Identity($"ext-auth-{i:D2}", active: true, mfaEnabled: false, mfaNeeded: false, needsExtAuth: true),
            };
            arr.Add(id);
        }
        return status;
    }

    private static JObject Identity(
        string name,
        bool active,
        bool mfaEnabled,
        bool mfaNeeded,
        bool needsExtAuth)
    {
        JObject o = new JObject
        {
            ["Name"] = name,
            ["Identifier"] = $"c:\\fake\\ids\\{name}.json",
            ["FingerPrint"] = $"FP-{name.ToUpperInvariant()}",
            ["Active"] = active,
            ["Loaded"] = true,
            ["IdFileStatus"] = false,
            ["NeedsExtAuth"] = needsExtAuth,
            ["MfaEnabled"] = mfaEnabled,
            ["MfaNeeded"] = mfaNeeded,
            ["Metrics"] = new JObject { ["Up"] = 0, ["Down"] = 0 },
            ["MfaMinTimeout"] = 0,
            ["MfaMaxTimeout"] = 0,
            ["MfaMinTimeoutRem"] = 0,
            ["MfaMaxTimeoutRem"] = 0,
            ["MinTimeoutRemInSvcEvent"] = 0,
            ["MaxTimeoutRemInSvcEvent"] = 0,
            ["Deleted"] = false,
            ["Notified"] = false,
        };
        if (needsExtAuth)
        {
            o["ExtAuthProviders"] = new JArray("keycloak", "auth0");
            o["Config"] = new JObject { ["ztAPI"] = "https://controller.example", ["ztAPIs"] = new JArray("https://controller.example") };
            o["ControllerVersion"] = "v2.0.0-mock";
        }
        return o;
    }

    private static JObject SkeletonStatus() => new JObject
    {
        ["Active"] = true,
        ["Duration"] = 0,
        ["StartTime"] = "2026-05-12T00:00:00.000000Z",
        ["Identities"] = new JArray(),
        ["IpInfo"] = new JObject { ["Ip"] = "100.150.0.0", ["Subnet"] = "255.255.0.0", ["MTU"] = 65535, ["DNS"] = "100.150.0.1" },
        ["LogLevel"] = "info",
        ["ServiceVersion"] = new JObject { ["Version"] = "v0.0.0-mock", ["BuildDate"] = "Mock" },
        ["TunIpv4"] = "100.150.0.0",
        ["TunIpv4Mask"] = 16,
        ["AddDns"] = false,
        ["ApiPageSize"] = 25,
        ["TunName"] = "ziti-tun-mock",
        ["L2Enabled"] = false,
        ["PcapInterface"] = "",
        ["TapInfo"] = new JObject(),
        ["ConfigDir"] = "c:\\fake\\ids",
    };
}
