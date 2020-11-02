using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiUpdateService {
    interface IUpdateCheck {
        bool IsUpdateAvailable(Version current);
        string FileName();
        void CopyUpdatePackage(string destinationFolder, string destinationName);

        bool AlreadyDownloaded(string destinationFolder, string destinationName);
    }
}
