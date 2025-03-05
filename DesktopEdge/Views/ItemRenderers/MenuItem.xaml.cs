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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Ziti.Desktop.Edge.Utils;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MenuItem.xaml
    /// </summary>
    public partial class OZMenuItem : UserControl {

        private string _label = "";
        private string _icon = "";

        public string Label {
            get {
                return _label;
            }
            set {
                this._label = value;
                LabelCtrl.Content = this._label;
            }
        }
        public string Icon {
            get {
                return _icon;
            }
            set {
                this._icon = value;
                IconCtrl.Source = new BitmapImage(new Uri(this._icon, UriKind.Relative));
            }
        }


        public OZMenuItem() {
            InitializeComponent();
        }

        private void UserControl_MouseEnter(object sender, MouseEventArgs e) {

        }

        private void UserControl_MouseLeave(object sender, MouseEventArgs e) {

        }

        private void MainUI_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            UIUtils.ClickedControl = e.Source as UIElement;
        }
    }
}
