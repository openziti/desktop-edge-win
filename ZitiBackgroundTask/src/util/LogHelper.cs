using System;
using System.Diagnostics;
using Windows.Networking.Vpn;

namespace NetFoundry.VPN.Util
{
    public sealed class LogHelper
    {
        private const string locator = "__X__";
        public static void LogLine(string msg)
        {
            Debug.WriteLine(string.Format("${0} ${1}: ${2}", locator, Environment.CurrentManagedThreadId, msg));
        }
        public static void LogLine(string text, VpnChannel channel)
        {
            LogLine(channel?.GetHashCode() + ":" + text);
        }
    }
}
