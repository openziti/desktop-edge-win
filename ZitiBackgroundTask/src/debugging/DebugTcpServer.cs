// This code is adapted from a sample found at the URL 
// "http://blogs.msdn.com/b/jmanning/archive/2004/12/19/325699.aspx"

using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;

namespace NetFoundry.VPN.Debugging
{
	public sealed class DebugTcpServer
	{
		private static bool started = false;
		public static void Start()
		{
			started = false;
			if (started) return;
			started = true;

			Console.WriteLine("Starting echo server...");

			TcpListener listener = new TcpListener(IPAddress.Loopback, ZitiVPNPlugin.DESIRED_PORT);
			listener.Start();

			while (true)
			{
				TcpClient client = listener.AcceptTcpClient();
				System.Threading.Tasks.Task.Run(() =>
				{
					NetworkStream stream = client.GetStream();
					StreamWriter writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };
					StreamReader reader = new StreamReader(stream, Encoding.ASCII);

					while (true)
					{
						try
						{
							string inputLine = "";
							while (inputLine != null)
							{
								inputLine = reader.ReadLine();
								writer.WriteLine("Echoing string: " + inputLine);
								Console.WriteLine("Echoing string: " + inputLine);
							}
						}
						catch { /* don't really care at this point */}
						Console.WriteLine("Server saw disconnect from client.");
					}
				});
			}
		}
	}
}