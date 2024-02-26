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
using System.Threading.Tasks;

using NLog;

namespace Ziti.Desktop.Edge.Utils {
    public class UIUtils {

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public static void SetLogLevel(string level) {
			try
			{
				Logger.Info("request to change log level received: {0}", level);
				if ((""+level).ToLower() == "verbose") {
					level = "trace";
					Logger.Info("request to change log level to verbose - but using trace instead");
				}
				var l = LogLevel.FromString(level);
				foreach (var rule in LogManager.Configuration.LoggingRules) {
					rule.EnableLoggingForLevel(l);
					rule.SetLoggingLevels(l, LogLevel.Fatal);
				}

				LogManager.ReconfigExistingLoggers();
				Logger.Info("logger reconfigured to log at level: {0}", l);
			} catch (Exception e) {
				Logger.Error(e, "Failed to set log level: {0}", e.Message);
			}
		}
	}
}
