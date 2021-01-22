using System;
using System.IO;

using NLog;

namespace ZitiUpdateService.checkers {

    abstract class UpdateCheck {
        public abstract int IsUpdateAvailable(Version current);
        public abstract string FileName();
        public abstract void CopyUpdatePackage(string destinationFolder, string destinationName);
        public abstract bool AlreadyDownloaded(string destinationFolder, string destinationName);
        public abstract bool HashIsValid(string destinationFolder, string destinationName);
        public abstract Version GetNextVersion();

        public static Version NormalizeVersion(Version v) {
            if (v.Minor < 1) return new Version(v.Major, 0, 0, 0);
            if (v.Build < 1) return new Version(v.Major, v.Minor, 0, 0);
            if (v.Revision < 1) return new Version(v.Major, v.Minor, v.Build, 0);
            return v;
        }
    }
}
