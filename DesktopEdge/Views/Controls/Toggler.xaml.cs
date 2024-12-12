using System;
using System.Windows;
using System.Windows.Controls;

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
