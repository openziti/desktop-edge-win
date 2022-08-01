using System;
using System.IO;

using NLog;

namespace ZitiUpdateService.Checkers {
	internal class FilesystemCheck : UpdateCheck {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		
		Version version = new Version("0.0.0");

		public FilesystemCheck(Version current, int updateAvailable, DateTime publishedDate, string fileNameToReturn, Version versionToReturn) : base(current) {
			Avail = updateAvailable;
			PublishDate = publishedDate;
			FileName = fileNameToReturn;
			version = versionToReturn;
		}

		override public bool AlreadyDownloaded(string destinationFolder, string destinationName) {
			return File.Exists(Path.Combine(destinationFolder, destinationName));
		}

		override public void CopyUpdatePackage(string destinationFolder, string destinationName) {
			var dest = Path.Combine(destinationFolder, destinationName);
			File.WriteAllText(dest, "file check test file");
		}

		override public bool HashIsValid(string destinationFolder, string destinationName) {
			return true;
		}

		override public Version GetNextVersion() {
			return version;
		}
	}
}
