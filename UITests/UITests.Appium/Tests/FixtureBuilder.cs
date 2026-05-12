using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Programmatic builders for richer mock-IPC status fixtures than the committed
/// JSON files can comfortably express -- e.g. 50 identities with mixed states.
/// </summary>
public static class FixtureBuilder
{
    public static JObject ManyMixedIdentities(int count = 50)
    {
        var status = SkeletonStatus();
        var arr = (JArray)status["Identities"]!;

        for (int i = 0; i < count; i++)
        {
            var flavor = i % 4;
            var (name, id) = flavor switch
            {
                0 => ($"enabled-{i:D2}",       Identity($"enabled-{i:D2}", active: true)),
                1 => ($"disabled-{i:D2}",      Identity($"disabled-{i:D2}", active: false)),
                2 => ($"mfa-required-{i:D2}",  Identity($"mfa-required-{i:D2}", active: true, mfaNeeded: true, mfaEnabled: true)),
                _ => ($"ext-auth-{i:D2}",      Identity($"ext-auth-{i:D2}", active: true, needsExtAuth: true)),
            };
            arr.Add(id);
        }
        return status;
    }

    private static JObject Identity(
        string name,
        bool active = true,
        bool mfaEnabled = false,
        bool mfaNeeded = false,
        bool needsExtAuth = false)
    {
        var o = new JObject
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
