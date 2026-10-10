using System.Reflection;
using System.Xml.Linq;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>The values a version's user.config holds, with one identity's default provider.</summary>
public sealed record UserSettings(string SortOption, string SortDirection, string DefaultProviderIdentity,
    string DefaultProvider);

/// <summary>
/// The app's user-scoped settings, %LOCALAPPDATA%\ZitiDesktopEdge\ZitiDesktopEdge.exe_Url_&lt;hash&gt;\&lt;version&gt;\user.config.
/// .NET Framework ignores an overridden LOCALAPPDATA, so a test that changes one of them has to undo it in the
/// running user's real file. Edit it only while the app is not running: the app saves its whole copy over it.
/// </summary>
public static class AppUserConfig
{
    // ZitiIdentity.ProviderDelimiter: each DefaultProviders entry is "<identity file>|-|<provider>".
    private const string ProviderDelimiter = "|-|";

    private static string Root() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZitiDesktopEdge");

    /// <summary>
    /// Remove every identity's default provider entry for provider, from every version's user.config, since
    /// Settings.Upgrade copies an older version's entries into a new version directory.
    /// </summary>
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

    /// <summary>The version directory the exe's user.config sits in: the app sets no informational version, so it is the assembly version.</summary>
    public static Version SettingsVersion(string exePath) => AssemblyName.GetAssemblyName(exePath).Version!;

    /// <summary>Every user.config for version by path, one per exe path the app has run from.</summary>
    public static Dictionary<string, byte[]> ReadConfigs(Version version)
    {
        if (!Directory.Exists(Root())) return new Dictionary<string, byte[]>();
        return Directory.EnumerateDirectories(Root())
            .Select(exeDir => Path.Combine(exeDir, version.ToString(), "user.config"))
            .Where(File.Exists)
            .ToDictionary(path => path, File.ReadAllBytes);
    }

    /// <summary>Delete every user.config for version, then write back the originals ReadConfigs returned.</summary>
    public static void RestoreConfigs(Version version, IReadOnlyDictionary<string, byte[]> originals)
    {
        foreach (string path in ReadConfigs(version).Keys) File.Delete(path);
        foreach ((string path, byte[] bytes) in originals) File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Write settings as the user.config of the version just below current, beside currentConfig, which is the one
    /// Settings.Upgrade copies from. Returns the new version directory.
    /// </summary>
    public static string SeedPreviousVersion(string currentConfig, Version current, UserSettings settings)
    {
        string exeDir = Path.GetDirectoryName(Path.GetDirectoryName(currentConfig)!)!;
        string versionDir = Path.Combine(exeDir, PreviousVersion(current).ToString());
        if (Directory.Exists(versionDir))
            throw new InvalidOperationException($"{versionDir} already exists: delete it, it is left from an earlier run");
        Directory.CreateDirectory(versionDir);
        new XDocument(new XElement("configuration", new XElement("userSettings",
            new XElement("ZitiDesktopEdge.Properties.Settings",
                StringSetting("SortOption", settings.SortOption),
                StringSetting("SortDirection", settings.SortDirection),
                StringSetting("SettingsUpgradeRequired", "False"),
                new XElement("setting", new XAttribute("name", "DefaultProviders"), new XAttribute("serializeAs", "Xml"),
                    new XElement("value", new XElement("ArrayOfString", new XElement("string",
                        settings.DefaultProviderIdentity + ProviderDelimiter + settings.DefaultProvider))))))))
            .Save(Path.Combine(versionDir, "user.config"));
        return versionDir;
    }

    private static XElement StringSetting(string name, string value) =>
        new XElement("setting", new XAttribute("name", name), new XAttribute("serializeAs", "String"),
            new XElement("value", value));

    /// <summary>The highest version below current, so no real version directory can sit between them.</summary>
    private static Version PreviousVersion(Version current)
    {
        if (current.Revision > 0) return new Version(current.Major, current.Minor, current.Build, current.Revision - 1);
        if (current.Build > 0) return new Version(current.Major, current.Minor, current.Build - 1, int.MaxValue);
        throw new NotSupportedException($"no previous version to seed below {current}");
    }
}
