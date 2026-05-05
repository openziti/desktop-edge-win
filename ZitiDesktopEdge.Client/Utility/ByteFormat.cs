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

namespace ZitiDesktopEdge.Utility {
    public static class ByteFormat {
        public static string Format(long bytes) {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int index = 0;
            while (value >= 1024 && index < suffixes.Length - 1) {
                value = value / 1024;
                index++;
            }
            return value.ToString("0.0") + " " + suffixes[index];
        }
    }
}
