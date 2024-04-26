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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Drawing;
using System.ComponentModel;

namespace ZitiDesktopEdge {

    public class WinAPI {
        public struct RECT {
            public int left;
            public int top;
            public int right;
            public int bottom;

            public override string ToString() {
                return "(" + left + ", " + top + ") --> (" + right + ", " + bottom + ")";
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string strClassName, string strWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, IntPtr windowTitle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);


        public static IntPtr GetTrayHandle() {
            IntPtr taskBarHandle = WinAPI.FindWindow("Shell_TrayWnd", null);
            if (!taskBarHandle.Equals(IntPtr.Zero)) {
                return WinAPI.FindWindowEx(taskBarHandle, IntPtr.Zero, "TrayNotifyWnd", IntPtr.Zero);
            }
            return IntPtr.Zero;
        }

        public static Rectangle GetTrayRectangle() {
            WinAPI.RECT rect;
            WinAPI.GetWindowRect(WinAPI.GetTrayHandle(), out rect);
            return new Rectangle(new Point(rect.left, rect.top), new Size((rect.right - rect.left) + 1, (rect.bottom - rect.top) + 1));
        }
    }
}


