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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge {
    public class CommonDelegates {
        public delegate void ButtonDelegate(UserControl sender);
        public delegate void CloseAction(bool isComplete, UserControl sender);
        public delegate void JoinAction(bool isComplete, UserControl sender);
        public delegate void JoinNetwork(string URL);
        public delegate void CompleteExternalAuth(ZitiIdentity identity, string provider);
        public delegate void ShowBlurb(Blurb blurb);
    }

    public struct Blurb {
        public string Title;
        public string Message;
        public bool Complete;
        public string Level;
    }

    public class BooleanToVisibilityConverter : System.Windows.Data.IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is bool booleanValue) {
                return booleanValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }
}
