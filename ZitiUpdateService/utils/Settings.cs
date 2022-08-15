using System.IO;
using Newtonsoft.Json;
using NLog;
using ZitiDesktopEdge.DataStructures;

namespace ZitiUpdateService.Utils
{
	internal class Settings
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		private FileSystemWatcher watcher;

		[JsonIgnore]
		private string Location { get; set; }

		public bool AutomaticUpdatesDisabled { get; set; }

		public event System.EventHandler<ControllerEvent> OnConfigurationChange;

		internal Settings(bool doInit)
		{
			if (doInit)
			{
				init();
			}
		}

		public Settings()
		{
		}

		private void init()
		{
			string folder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "NetFoundry", "ZitiUpdateService");
			string file = "settings.json";
			Location = Path.Combine(folder, file);
			Directory.CreateDirectory(folder);
			watcher = new FileSystemWatcher(folder);
			watcher.Filter = file;

			watcher.NotifyFilter = NotifyFilters.Attributes
								 | NotifyFilters.CreationTime
								 | NotifyFilters.DirectoryName
								 | NotifyFilters.FileName
								 | NotifyFilters.LastAccess
								 | NotifyFilters.LastWrite
								 | NotifyFilters.Security
								 | NotifyFilters.Size;
			watcher.Changed += OnChanged;
			watcher.Deleted += OnDeleted;
			watcher.Renamed += OnRenamed;
			watcher.Error += OnError;
			watcher.EnableRaisingEvents = true;
		}

		private static JsonSerializer serializer = new JsonSerializer() { Formatting = Formatting.Indented };
		internal void Load()
		{
			try
			{
				string json = File.ReadAllText(Location);
				var jsonReaderEvt = new JsonTextReader(new StringReader(json));
				Settings s = serializer.Deserialize<Settings>(jsonReaderEvt);
				Update(s);
			}
			catch
			{
				// do nothing
			}
		}
		internal void Write()
		{
			try
			{
				using (StreamWriter file = File.CreateText(Location))
				{
					serializer.Serialize(file, this);
				}
				this.OnConfigurationChange?.Invoke(null, null);
			}
			catch
			{
				// do nothing
			}
		}


		private static void OnError(object sender, ErrorEventArgs e)
		{
		}

		private void OnRenamed(object sender, RenamedEventArgs e)
		{
			Logger.Info("Settings file renamed. Resetting to defaults...");
			this.Update(new Settings());
		}

		private void OnDeleted(object sender, FileSystemEventArgs e)
		{
			Logger.Info("Settings file deleted. Resetting to defaults...");
			this.Update(new Settings());
		}

		private void OnCreated(object sender, FileSystemEventArgs e)
		{
		}

		private void OnChanged(object sender, FileSystemEventArgs e)
		{
			Logger.Info("Settings file changed. Reloading...");
			this.Load();
		}

		private void Update(Settings source)
		{
			this.AutomaticUpdatesDisabled = source.AutomaticUpdatesDisabled;
		}
	}
}
