using System;

namespace ZitiUpdateService {
    interface IUpdateCheck {
        bool IsUpdateAvailable(Version current);
        string FileName();
        void CopyUpdatePackage(string destinationFolder, string destinationName);

        bool AlreadyDownloaded(string destinationFolder, string destinationName);
    }
}
