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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for StyledButton.xaml
    /// </summary>
    public partial class StyledButton : UserControl {

        public delegate void ClickAction(object sender, MouseButtonEventArgs e);
        public event ClickAction OnClick;
        private string _label = "";
        private string bgColor = "#0069FF";

        public string BgColor {
            get { return bgColor; }
            set {
                bgColor = value;
                ButtonBg.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));
            }
        }

        public string Label {
            get {
                return _label;
            }
            set {
                this._label = value;
                ButtonLabel.Content = this._label;
            }
        }

        public static readonly DependencyProperty ButtonMarginProperty = DependencyProperty.Register(
            "ButtonMargin",
            typeof(Thickness),
            typeof(StyledButton),
            new PropertyMetadata(new Thickness(40, 0, 40, 0)));

        public Thickness ButtonMargin {
            get => (Thickness)GetValue(ButtonMarginProperty);
            set => SetValue(ButtonMarginProperty, value);
        }

        public StyledButton() {
            InitializeComponent();
        }

        /// <summary>
        /// When the button area is entered slowly make it slightly opaque
        /// </summary>
        /// <param name="sender">The button object</param>
        /// <param name="e">The mouse event</param>
        private void Hover(object sender, MouseEventArgs e) {
            ButtonBgDarken.Opacity = 0.0;
            ButtonBgDarken.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0.2, TimeSpan.FromSeconds(.3)));
        }

        /// <summary>
        /// When the mouse leaves the button ara snap the opacity back to full
        /// </summary>
        /// <param name="sender">The button object</param>
        /// <param name="e">The mouse event</param>
        private void Leave(object sender, MouseEventArgs e) {
            ButtonBgDarken.Opacity = 0.2;
            ButtonBgDarken.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromSeconds(.3)));
        }

        /// <summary>
        /// Change the color to visualize a click event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Down(object sender, MouseButtonEventArgs e) {
            // ButtonBgColor.Color = Color.FromRgb(126, 180, 255);
        }

        /// <summary>
        ///  Execute the click operation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DoClick(object sender, MouseButtonEventArgs e) {
            this.OnClick?.Invoke(sender, e);
        }

        public void Disable() {
            this.IsEnabled = false;
            ButtonBg.Style = (Style)Resources["Disabled"];
            ButtonLabel.Foreground = (Brush)Resources["DisabledTextBrush"];
        }

        public void Enable() {
            this.IsEnabled = true;
            ButtonBg.Style = (Style)Resources["Enabled"];
            ButtonLabel.Foreground = Brushes.White;
        }
    }
}
