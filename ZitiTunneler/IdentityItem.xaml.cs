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

		public ZitiIdentity _identity;
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
			if (ToggleSwitch.Enabled) {
				ToggleStatus.Content = "ENABLED";
			} else {
				ToggleStatus.Content = "DISABLED";
			}
		}

		public IdentityItem() {
			InitializeComponent();
			ToggleSwitch.OnToggled += ToggleIdentity;
		}

		private void ToggleIdentity(bool on) {
			// Jeremy - make the messagebox pretty or something
			try
			{
				ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
				ServiceClient.Identity id = client.IdentityOnOff(_identity.Fingerprint, on);
				if (on) {
					ToggleStatus.Content = "ENABLED";
					// MessageBox.Show("jeremy - update the identity and services here. When disabled there will be services returned. service count: " + id?.Services?.Count());
				} else {
					ToggleStatus.Content = "DISABLED";
					// MessageBox.Show("jeremy - update the identity and services here. When disabled there will be no services, service count: " + id?.Services?.Count());
				}
			} catch(ServiceClient.ServiceException se) {
				MessageBox.Show("Unexpected error 5 the toggle needs to be rolled back!!! jeremy!", se.Message);
				MessageBox.Show(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				MessageBox.Show("Unexpected error 5 the toggle needs to be rolled back!!! jeremy!", ex.Message);
			}
		}

		private void Canvas_MouseEnter(object sender, MouseEventArgs e) {
			OverState.Opacity = 0.2;
		}

		private void Canvas_MouseLeave(object sender, MouseEventArgs e) {
			OverState.Opacity = 0;
		}

		private void OpenDetails(object sender, MouseButtonEventArgs e) {
			IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
			deets.SelectedIdentity = this;
			deets.Identity = this.Identity;
			this.ToggleSwitch.IsEnabled = this.Identity.IsEnabled;
		}
	}
}
