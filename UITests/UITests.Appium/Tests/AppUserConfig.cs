using System.Xml.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// The app's user-scoped settings, %LOCALAPPDATA%\ZitiDesktopEdge\ZitiDesktopEdge.exe_Url_&lt;hash&gt;\&lt;version&gt;\user.config.
/// .NET Framework ignores an overridden LOCALAPPDATA, so a test that changes one of them has to undo it in the
/// running user's real file. Edit it only while the app is not running: the app saves its whole copy over it.
/// </summary>
public static class AppUserConfig
{
    // ZitiIdentity.ProviderDelimiter: each DefaultProviders entry is "<identity file>|-|<provider>".
    private const string ProviderDelimiter = "|-|";

    /// <summary>
    /// Remove every identity's default provider entry for provider, from every version's user.config, since
    /// Settings.Upgrade copies an older version's entries into a new version directory.
    /// </summary>
    public static void RemoveDefaultProvider(string provider)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZitiDesktopEdge");
        if (!Directory.Exists(root)) return;
        foreach (string configPath in Directory.EnumerateFiles(root, "user.config", SearchOption.AllDirectories))
        {
            XDocument config = XDocument.Load(configPath);
            List<XElement> entries = config.Descendants("setting")
                .Where(setting => (string?)setting.Attribute("name") == "DefaultProviders")
                .Descendants("string")
                .Where(entry => entry.Value.EndsWith(ProviderDelimiter + provider, StringComparison.Ordinal))
                .ToList();
            if (entries.Count == 0) continue;
            entries.ForEach(entry => entry.Remove());
            config.Save(configPath);
            Step.Log($"removed {entries.Count} default provider entries for {provider} from {configPath}");
        }
    }
}
