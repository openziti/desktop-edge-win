using System;

namespace ZitiUpdateService {
    interface IUpdateCheck {
        int IsUpdateAvailable(Version current);
        string FileName();
        void CopyUpdatePackage(string destinationFolder, string destinationName);
        bool AlreadyDownloaded(string destinationFolder, string destinationName);
        bool HashIsValid(string destinationFolder, string destinationName);
        Version GetNextVersion();
    }
}
