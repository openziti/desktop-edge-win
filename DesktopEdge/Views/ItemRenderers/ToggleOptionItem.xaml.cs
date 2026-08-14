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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for ToggleOptionItem.xaml. A sub-menu option row carrying an on/off
    /// switch, the toggling counterpart to the selectable SubOptionItem. Clicking anywhere on
    /// the row flips the switch, and IsOn is two-way bindable to a view model property.
    /// </summary>
    public partial class ToggleOptionItem : UserControl {

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(ToggleOptionItem), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
            nameof(IsOn), typeof(bool), typeof(ToggleOptionItem),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsOnChanged));

        public string Label {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public bool IsOn {
            get => (bool)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        public ToggleOptionItem() {
            InitializeComponent();
            ToggleSwitch.OnToggled += ToggleSwitch_OnToggled;
        }

        // Toggler keeps its state in a plain property that no binding can reach, so IsOn is pushed
        // into it here. false is both the DP default and the switch's rest position, so a view model
        // that starts out false needs no callback to look right.
        private static void IsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            ((ToggleOptionItem)d).ToggleSwitch.Enabled = (bool)e.NewValue;
        }

        private void ToggleSwitch_OnToggled(bool on) {
            IsOn = on;
        }

        // Toggler marks its own mouse-up handled, so a click on the switch never lands here as well.
        private void Row_MouseUp(object sender, MouseButtonEventArgs e) {
            ToggleSwitch.Toggle();
        }
    }
}
