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
using System.Windows;
using System.Windows.Controls;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for Toggler.xaml
    /// </summary>
    public partial class Toggler : UserControl {

        public delegate void Toggled(bool on);
        public event Toggled OnToggled;
        private bool _isEnabled = false;
        public Toggler() {
            InitializeComponent();
        }

        public Boolean Enabled {
            get {
                return _isEnabled;
            }
            set {
                _isEnabled = value;
                if (_isEnabled) {
                    // ToggleTab.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(16, TimeSpan.FromSeconds(.3)));
                    // OnColor.BeginAnimation(Border.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromSeconds(.3)));

                    OnColor.Opacity = 1;
                    Canvas.SetLeft(ToggleTab, 16);
                } else {
                    // ToggleTab.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(.3)));
                    // OnColor.BeginAnimation(Border.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(.3)));

                    OnColor.Opacity = 0;
                    Canvas.SetLeft(ToggleTab, 1);
                }
            }
        }

        public void Toggle() {
            Enabled = !Enabled;
            if (OnToggled != null) {
                OnToggled(Enabled);
            }
        }

        private void OnToggle(object sender, RoutedEventArgs e) {
            e.Handled = true;
            Enabled = !Enabled;
            if (OnToggled != null) {
                OnToggled(Enabled);
            }
        }
    }
}
