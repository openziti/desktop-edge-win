using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge {
    public class CommonDelegates {
        public delegate void ButtonDelegate(UserControl sender);
        public delegate void CloseAction(bool isComplete, UserControl sender);
        public delegate void JoinAction(bool isComplete, UserControl sender);
        public delegate void JoinNetwork(string URL);
        public delegate void CompleteExternalAuth(ZitiIdentity identity, string provider);
    }
}
