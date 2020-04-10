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

namespace ZitiTunneler {
    /// <summary>
    /// Interaction logic for Toggler.xaml
    /// </summary>
    public partial class Toggler : UserControl {

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
				// Clinton of the Clints... Need to blow an event bubble and turn this bad boy on or off
				if (_isEnabled) {
					Status.Content = "ENABLED";
					OnButton.Visibility = Visibility.Collapsed;
					OffButton.Visibility = Visibility.Visible;
				} else {
					Status.Content = "DISABLED";
					OnButton.Visibility = Visibility.Visible;
					OffButton.Visibility = Visibility.Collapsed;
				}
			}
		}

		private void ToggleConnect(object sender, RoutedEventArgs e) {
			Enabled = !Enabled;
		}

		private void OnLoad(object sender, RoutedEventArgs e) {
			if (_isEnabled) {
				Status.Content = "ENABLED";
			} else {
				Status.Content = "DISABLED";
			}
		}
	}
}
