using System.Xml.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

public sealed record UserSettings(string SortOption, string SortDirection, string DefaultProviderIdentity,
    string DefaultProvider);

// .NET Framework ignores an overridden LOCALAPPDATA, and a running app saves its copy over these files.
public static class AppUserConfig
{
    // ZitiIdentity.ProviderDelimiter: each DefaultProviders entry is "<identity file>|-|<provider>".
    public const string ProviderDelimiter = "|-|";

    private static string Root() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZitiDesktopEdge");

    /// <summary>Every version's user.config, since Settings.Upgrade copies older entries forward.</summary>
    public static void RemoveDefaultProvider(string provider)
    {
        if (!Directory.Exists(Root())) return;
        foreach (string configPath in Directory.EnumerateFiles(Root(), "user.config", SearchOption.AllDirectories))
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

    /// <summary>One user.config per exe path the app has run from.</summary>
    public static Dictionary<string, byte[]> ReadConfigs(Version version)
    {
        if (!Directory.Exists(Root())) return new Dictionary<string, byte[]>();
        return Directory.EnumerateDirectories(Root())
            .Select(exeDir => Path.Combine(exeDir, version.ToString(), "user.config"))
            .Where(File.Exists)
            .ToDictionary(path => path, File.ReadAllBytes);
    }
}
