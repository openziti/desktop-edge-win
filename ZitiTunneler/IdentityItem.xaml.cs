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
using ZitiTunneler.Models;

namespace ZitiTunneler {
	/// <summary>
	/// User Control to list Identities and give status
	/// </summary>
	public partial class IdentityItem:UserControl {

		private ZitiIdentity _identity;
		public ZitiIdentity Identity {
			get {
				return _identity;
			}
			set {
				_identity = value;
				this.RefreshUI();
			}
		}

		public void RefreshUI () {
			ToggleSwitch.Enabled = _identity.IsEnabled;
			IdName.Content = _identity.Name;
			IdUrl.Content = _identity.ControllerUrl;
			ServiceCount.Content = _identity.Services.Count.ToString();
		}

		public bool isOn = false;
		public IdentityItem() {
			InitializeComponent();
		}

		private void ToggleButton_Checked(object sender, RoutedEventArgs e) {
			isOn = !isOn;
		}
	}
}
