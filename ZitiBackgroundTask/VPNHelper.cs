using System;
using Windows.Networking.Vpn;

namespace NetFoundry.VPN
{
    public sealed class VPNHelper
    {

        public static void ConnectReal()
        {

        }
        public void ConnectDebug()
        {

        }
        public static void LogLine(string text, VpnChannel channel)
        {
            System.Diagnostics.Debug.WriteLine("__X___: " + Environment.CurrentManagedThreadId + ":" +
                                               channel?.GetHashCode() + ":" + text);
        }

        public static void LogLine(string text)
        {
            LogLine(text, null);
        }
    }
}