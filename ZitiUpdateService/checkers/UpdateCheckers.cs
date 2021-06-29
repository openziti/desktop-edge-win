using System;
using System.IO;

using NLog;

namespace ZitiUpdateService.Checkers {

    public struct ZDEInstallerInfo
    {
        public DateTime CreationTime;
        public Version Version;
        public bool IsCritical;
        public Int16 TimeRemaining;

    }

    abstract class UpdateCheck {
        public abstract int IsUpdateAvailable(Version current);
        public abstract string FileName();
        public abstract void CopyUpdatePackage(string destinationFolder, string destinationName);
        public abstract bool AlreadyDownloaded(string destinationFolder, string destinationName);
        public abstract bool HashIsValid(string destinationFolder, string destinationName);
        public abstract Version GetNextVersion();

        public abstract ZDEInstallerInfo GetZDEInstallerInfo(string fileDestination);
    }
}
