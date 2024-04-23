using NLog;
using NLog.Config;
using NLog.Targets;

internal static class TestUtils {
	private static readonly Logger logger = LogManager.GetCurrentClassLogger();

	public static async Task DownloadFileAsync(string url, string destination) {
		string directory = Path.GetDirectoryName(destination);
		if (!Directory.Exists(directory)) {
			Directory.CreateDirectory(directory);
			logger.Info($"Created directory: {directory}");
		}

		if (!File.Exists(destination)) {
			using (HttpClient httpClient = new HttpClient()) {
				var response = await httpClient.GetAsync(url);
				if (response.IsSuccessStatusCode) {
					using (var fileStream = new FileStream(destination, FileMode.Create)) {
						await response.Content.CopyToAsync(fileStream);
						logger.Info($"Downloaded file from {url} to {destination}");
					}
				} else {
					logger.Error($"Failed to download file from {url}. Status code: {response.StatusCode}");
				}
			}
		} else {
			logger.Info($"File already exists at {destination}. Skipping download.");
		}
	}

	public static void ConfigureNLog() {
		var config = new LoggingConfiguration();
		// Targets where to log to: File and Console
		var logconsole = new ConsoleTarget("logconsole");

		// Rules for mapping loggers to targets            
		config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, logconsole);

		// Apply config           
		LogManager.Configuration = config;
	}
}
