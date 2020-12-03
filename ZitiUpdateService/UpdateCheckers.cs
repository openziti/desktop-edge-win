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
		string currentResponse = null;

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

		public bool IsUpdateAvailable(Version currentVersion) {
			Logger.Debug("checking for update begins. current version detected as {0}", currentVersion);
			Logger.Debug("issuing http get to url: {0}", url);
			HttpWebRequest httpWebRequest = WebRequest.CreateHttp(url);
			httpWebRequest.Method = "GET";
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.UserAgent = "OpenZiti UpdateService";
			HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			StreamReader streamReader = new StreamReader(httpResponse.GetResponseStream());
			currentResponse = streamReader.ReadToEnd();
			Logger.Trace("response received: {0}", currentResponse);
			JObject json = JObject.Parse(currentResponse);

			JArray assets = JArray.Parse(json.Property("assets").Value.ToString());
			foreach (JObject asset in assets.Children<JObject>()) {
				string assetName = asset.Property("name").Value.ToString();

				if (assetName.StartsWith("Ziti.Desktop.Edge.Client-")) {
					downloadUrl = asset.Property("browser_download_url").Value.ToString();
				} else {
					Logger.Debug("skipping asset with name: {assetName}", assetName);
                }
				break;
			}

			if (downloadUrl == null) {
				Logger.Error("DOWNLOAD URL not found at: {0}", url);
				return false;
			}
			Logger.Debug("download url detected: {0}", downloadUrl);
			downloadFileName = downloadUrl.Substring(downloadUrl.LastIndexOf('/') + 1);
			Logger.Debug("download file name: {0}", downloadFileName);

			string releaseVersion = json.Property("tag_name").Value.ToString();
			string releaseName = json.Property("name").Value.ToString();
			Version publishedVersion = NormalizeVersion(new Version(releaseVersion));
			int compare = currentVersion.CompareTo(publishedVersion);
			if (compare < 0) {
				Logger.Info("upgrade {} is available. Published version: {} is newer than the current version: {}", releaseName, publishedVersion, currentVersion);
				return true;
			} else if (compare > 0) {
				Logger.Info("the version installed: {0} is newer than the released version: {1}", currentVersion, publishedVersion);
				return false;
			} else {
				return false;
			}
		}

		private Version NormalizeVersion(Version v) {
			if (v.Minor < 1) return new Version(v.Major, 0, 0, 0);
			if (v.Build < 1) return new Version(v.Major, v.Minor, 0, 0);
			if (v.Revision < 1) return new Version(v.Major, v.Minor, v.Build, 0);
			return v;
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
