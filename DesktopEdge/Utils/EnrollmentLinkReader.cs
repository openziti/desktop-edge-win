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

using System;
using System.Collections.Specialized;
using System.Web;
using NLog;
using ZitiDesktopEdge.Models;

namespace Ziti.Desktop.Edge.Utils {
    internal static class EnrollmentLinkReader {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal static bool IsEnrollmentLink(string text) {
            Uri link;
            return Uri.TryCreate(text, UriKind.Absolute, out link) && link.Scheme == "ziti";
        }

        internal static EnrollmentLink GetEnrollmentLink(string enrollmentLink) {
            Uri link;
            if (!Uri.TryCreate(enrollmentLink, UriKind.Absolute, out link) || link.Scheme != "ziti" || link.Host != "enroll") {
                Logger.Warn("enrollment link is not a ziti://enroll link");
                return null;
            }
            NameValueCollection query = HttpUtility.ParseQueryString(link.Query);
            string[] controllers = query.GetValues("controller");
            if (controllers == null || controllers.Length != 1) {
                Logger.Warn("enrollment link needs exactly one controller");
                return null;
            }
            Uri controller;
            if (!Uri.TryCreate(controllers[0], UriKind.Absolute, out controller) || controller.Scheme != Uri.UriSchemeHttps) {
                Logger.Warn("enrollment link controller {controller} is not an https URL", controllers[0]);
                return null;
            }
            if (controller.UserInfo != "" || controller.Query != "" || controller.Fragment != "") {
                Logger.Warn("enrollment link controller {controller} has a user name, query or fragment", controllers[0]);
                return null;
            }
            string provider;
            if (!TryGetOptional(query, "provider", out provider)) return null;
            string enrollTo;
            if (!TryGetOptional(query, "type", out enrollTo)) return null;
            if (enrollTo != null && enrollTo != "cert" && enrollTo != "token") {
                Logger.Warn("enrollment link type {type} is not cert or token", enrollTo);
                return null;
            }
            return new EnrollmentLink { Controller = controller, Provider = provider, EnrollTo = enrollTo };
        }

        private static bool TryGetOptional(NameValueCollection query, string name, out string value) {
            string[] values = query.GetValues(name);
            if (values == null) {
                value = null;
                return true;
            }
            if (values.Length != 1 || values[0] == "") {
                Logger.Warn("enrollment link {name} must be given once and not empty", name);
                value = null;
                return false;
            }
            value = values[0];
            return true;
        }
    }
}
