/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

﻿using System;
using System.Net;
using System.IO;

using NLog;
using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.Utility {
    public static class GithubAPI {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public const string ProdUrl = "https://get.openziti.io/zdew/stable.json";
		
		public static JObject GetJson(string url) {
			HttpWebRequest httpWebRequest = WebRequest.CreateHttp(url);
			httpWebRequest.Method = "GET";
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.UserAgent = "OpenZiti UpdateService";
			HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			StreamReader streamReader = new StreamReader(httpResponse.GetResponseStream());
			string currentResponse = streamReader.ReadToEnd();
			Logger.Trace("response received: {0}", currentResponse);
			return JObject.Parse(currentResponse);
		}
		public static JArray GetJsonArray(string url) {
			HttpWebRequest httpWebRequest = WebRequest.CreateHttp(url);
			httpWebRequest.Method = "GET";
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.UserAgent = "OpenZiti UpdateService";
			HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			StreamReader streamReader = new StreamReader(httpResponse.GetResponseStream());
			string currentResponse = streamReader.ReadToEnd();
			Logger.Trace("response received for url: {0}", url);
			return JArray.Parse(currentResponse);
		}

		public static Version GetVersion(JObject json) {
			string releaseVersion = json.Property("tag_name").Value.ToString();
			string releaseName = json.Property("name").Value.ToString();
			return VersionUtil.NormalizeVersion(new Version(releaseVersion));
		}
	}
}
