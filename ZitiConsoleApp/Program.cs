using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Text;

namespace ZitiConsoleApp {
	class Program {

		private static string _versionUrl = "https://netfoundry.jfrog.io/netfoundry/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/maven-metadata.xml";
		private static string _serviceUrl = "https://netfoundry.jfrog.io/netfoundry/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/${version}/ziti-tunnel-win-${version}.zip";
		
		static void Main(string[] args) {
			var request = WebRequest.Create(_versionUrl) as HttpWebRequest;
			var response = request.GetResponse();

			Stream receiveStream = response.GetResponseStream();
			StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);

			var result = readStream.ReadToEnd();

			XmlDocument xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(result);

			XmlNode node = xmlDoc.SelectSingleNode("metadata/versioning/release");
			string version = node.InnerText;
			Console.WriteLine(version);

			string remoteService = _serviceUrl.Replace("${version}", version);
			Console.WriteLine(remoteService);
		}
	}
}
