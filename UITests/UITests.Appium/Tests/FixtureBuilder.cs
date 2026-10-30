using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

public static class FixtureBuilder
{
    // Five rows, since each PageSource fetch dominates a sort test: about 1-2s for 5 rows, 3-5s for 15.
    public static JObject SortableMixed()
    {
        JObject status = SkeletonStatus();
        JArray identities = (JArray)status["Identities"]!;
        JObject[] entries = new[]
        {
            Identity("zebra-prod",    active: true,  mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
            Identity("Bravo-Staging", active: false, mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
            Identity("ALPHA-DEV",     active: true,  mfaEnabled: true,  mfaNeeded: false, needsExtAuth: false),
            Identity("oscar-prod",    active: true,  mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
            Identity("CharlieEdge",   active: false, mfaEnabled: false, mfaNeeded: false, needsExtAuth: true),
        };
        foreach (JObject entry in entries) identities.Add(entry);
        return status;
    }

    public static JObject ManyMixedIdentities(int count)
    {
        JObject status = SkeletonStatus();
        JArray identities = (JArray)status["Identities"]!;

        for (int index = 0; index < count; index++)
        {
            JObject identity = (index % 4) switch
            {
                0 => Identity($"enabled-{index:D2}", active: true, mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
                1 => Identity($"disabled-{index:D2}", active: false, mfaEnabled: false, mfaNeeded: false, needsExtAuth: false),
                2 => Identity($"mfa-required-{index:D2}", active: true, mfaEnabled: true, mfaNeeded: true, needsExtAuth: false),
                _ => Identity($"ext-auth-{index:D2}", active: true, mfaEnabled: false, mfaNeeded: false, needsExtAuth: true),
            };
            identities.Add(identity);
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
        JObject identity = new JObject
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
            identity["ExtAuthProviders"] = new JArray("keycloak", "auth0");
            identity["Config"] = new JObject { ["ztAPI"] = "https://controller.example", ["ztAPIs"] = new JArray("https://controller.example") };
            identity["ControllerVersion"] = "v2.0.0-mock";
        }
        return identity;
    }

    private static JObject SkeletonStatus() => TestHelpers.Fixture("no-identities.json");
}
