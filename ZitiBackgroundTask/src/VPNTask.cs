using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Networking.Vpn;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices.WindowsRuntime;
using NetFoundry.VPN.Util;


namespace NetFoundry.VPN
{
    public sealed class VPNTask : IBackgroundTask
    {

        internal static IVpnPlugIn ziti = null;
        private static object lockobj = new object();

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            var backgroundTaskDeferral = taskInstance.GetDeferral();
            try
            {
                taskInstance.Canceled += TaskInstance_Canceled;
                VpnChannel.ProcessEventAsync(GetPlugin(), taskInstance.TriggerDetails);
            }
            catch (Exception e)
            {
                LogHelper.LogLine("An exception occurred. " + e.Message);
                LogHelper.LogLine(e.StackTrace);
            }
            finally
            {
                backgroundTaskDeferral.Complete();
            }
        }

        private void TaskInstance_Canceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            LogHelper.LogLine("TaskInstance_Canceled_a_" + sender.ToString());
            LogHelper.LogLine("TaskInstance_Canceled_b_" + reason.ToString());

            VpnChannel.ProcessEventAsync(GetPlugin(), sender.TriggerDetails);

            LogHelper.LogLine("TaskInstance_Canceled - done.  it has been triggered");
        }

        public static IVpnPlugIn GetPlugin()
        {
            lock (lockobj) {
                if (ziti == null)
                {
                    LogHelper.LogLine("singleton ziti plugin created");
                    ziti = new ZitiVPNPlugin();
                }
            }

            return ziti;
        }
    }
}
