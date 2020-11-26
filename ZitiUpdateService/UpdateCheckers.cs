using System;
using System.IO;
using System.Net;

using NLog;
using Newtonsoft.Json.Linq;
using System.Configuration;

namespace ZitiUpdateService {
	internal class GithubCheck : IUpdateCheck {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		string url;
		string downloadUrl = null;
		string downloadFileName = null;

		public GithubCheck(string url) {
			this.url = url;
		}

        public bool AlreadyDownloaded(string destinationFolder, string destinationName) {
			return File.Exists(Path.Combine(destinationFolder, destinationName));
		}

        public void CopyUpdatePackage(string destinationFolder, string destinationName) {
			WebClient webClient = new WebClient();
			string dest = Path.Combine(destinationFolder, destinationName);
			Logger.Info("download started: {0}", downloadUrl);
			webClient.DownloadFile(downloadUrl, dest);
			Logger.Info("download complete. file at {0}", dest);
		}

		public string FileName() {
			return downloadFileName;
		}

		public bool IsUpdateAvailable(Version current) {
			HttpWebRequest httpWebRequest = WebRequest.CreateHttp(url);
			httpWebRequest.Method = "GET";
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.UserAgent = "OpenZiti UpdateService";
			HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			StreamReader streamReader = new StreamReader(httpResponse.GetResponseStream());
			string result = streamReader.ReadToEnd();
			JObject json = JObject.Parse(result);
			string serverVersion = json.Property("tag_name").Value.ToString() + ".0";

			JArray assets = JArray.Parse(json.Property("assets").Value.ToString());
			foreach (JObject asset in assets.Children<JObject>()) {
				downloadUrl = asset.Property("browser_download_url").Value.ToString();
				break;
			}

			if (downloadUrl == null) {
				Logger.Error("DOWNLOAD URL not found at: {0}", url);
				return false;
			}
			downloadFileName = downloadUrl.Substring(downloadUrl.LastIndexOf('/') + 1);

			Version published = new Version(serverVersion);
			int compare = current.CompareTo(published);
			if (compare < 0) {
				Logger.Info("an upgrade is available.");
			} else if (compare > 0) {
				Logger.Info("the version installed is newer than the released version");
				return false;
			} else {
				Logger.Debug("current version {0} is the same as the latest release {0}", current, published);
				string useBetaReleases = ConfigurationManager.AppSettings.Get("UseBetaReleases");
				if(useBetaReleases != null) {
					Logger.Debug("BETA RELEASE DETECTED!");
					if (useBetaReleases == "yes") {
						Logger.Debug("BETA RELEASE == yes - returning true");
						return true;
					} else {
						Logger.Debug("BETA RELEASE value is not correct: {0}", useBetaReleases);
					}
                }
				return false;
			}

			return true;
		}
	}

	internal class FilesystemCheck : IUpdateCheck {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		string dest = null;

		bool isUpdateAvailable = false;
		public FilesystemCheck(bool updateAvailable) {
			this.isUpdateAvailable = updateAvailable;
		}

		public bool AlreadyDownloaded(string destinationFolder, string destinationName) {
			return File.Exists(Path.Combine(destinationFolder, destinationName));
		}

		public void CopyUpdatePackage(string destinationFolder, string destinationName) {
			dest = Path.Combine(destinationFolder, destinationName);
			File.Copy(@"C:\git\github\openziti\desktop-edge-win\Installer\Output\" + FileName(), dest);
		}

		public string FileName() {
			return "Ziti Desktop Edge Client-1.3.0.exe";
		}

		public bool IsUpdateAvailable(Version current) {
			return isUpdateAvailable;
		}
	}
}
