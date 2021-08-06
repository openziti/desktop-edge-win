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
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
	/// <summary>
	/// User Control to list Identities and give status
	/// </summary>
	public partial class IdentityItem:UserControl {

		public delegate void StatusChanged(bool attached);
		public event StatusChanged OnStatusChanged;
		public delegate void OnAuthenticate(ZitiIdentity identity);
		public event OnAuthenticate Authenticate;
		private System.Windows.Forms.Timer _timer;

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
			if (_identity.IsMFAEnabled) {
				if (_identity.MFAInfo.IsAuthenticated) {
					ServiceCountArea.Visibility = Visibility.Visible;
					MfaRequired.Visibility = Visibility.Collapsed;
					ServiceCountAreaLabel.Content = "services";
					MainArea.Opacity = 1.0;
					if (_identity.MaxTimeout>0) {
						if (_timer != null) _timer.Stop();
						_timer = new System.Windows.Forms.Timer();
						_timer.Interval = _identity.MaxTimeout;
						_timer.Tick += TimerTicked;
						_timer.Start();
					}
				} else {
					ServiceCountArea.Visibility = Visibility.Collapsed;
					MfaRequired.Visibility = Visibility.Visible;
					ServiceCountAreaLabel.Content = "authorize";
					MainArea.Opacity = 0.6;
				}
			} else {
				ServiceCountArea.Visibility = Visibility.Visible;
				MfaRequired.Visibility = Visibility.Collapsed;
				ServiceCountAreaLabel.Content = "services";
				MainArea.Opacity = 1.0;
			}
			IdName.Content = _identity.Name;
			IdUrl.Content = _identity.ControllerUrl;
			if (_identity.IsMFAEnabled && !_identity.MFAInfo.IsAuthenticated) {
				ServiceCount.Content = "MFA";
			} else {
				ServiceCount.Content = _identity.Services.Count.ToString();
			}
			TimerCountdown.ToolTip = _identity.TimeoutMessage;
			TimerCountdown.Visibility = _identity.IsTimingOut ? Visibility.Visible : Visibility.Collapsed;
			if (ToggleSwitch.Enabled) {
				ToggleStatus.Content = "ENABLED";
			} else {
				ToggleStatus.Content = "DISABLED";
			}
		}

		private void TimerTicked(object sender, EventArgs e) {
			_identity.MFAInfo.IsAuthenticated = false;
			RefreshUI();
			_timer.Stop();
		}

		public IdentityItem() {
			InitializeComponent();
			ToggleSwitch.OnToggled += ToggleIdentity;
		}

		async private void ToggleIdentity(bool on) {
			try {
				if (OnStatusChanged != null) {
					OnStatusChanged(on);
				}
				DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
				DataStructures.Identity id = await client.IdentityOnOffAsync(_identity.Fingerprint, on);
				this.Identity.IsEnabled = on;
				if (on) {
					ToggleStatus.Content = "ENABLED";
				} else {
					ToggleStatus.Content = "DISABLED";
				}
			} catch (DataStructures.ServiceException se) {
				MessageBox.Show(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				MessageBox.Show("Error", ex.Message);
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
		}

		private void MFAAuthenticate(object sender, MouseButtonEventArgs e) {
			this.Authenticate?.Invoke(_identity);
		}

		private void ToggledSwitch(object sender, MouseButtonEventArgs e) {
			ToggleSwitch.Toggle();
		}

		private void DoMFAOrOpen(object sender, MouseButtonEventArgs e) {
			if (MfaRequired.Visibility==Visibility.Visible) {
				MFAAuthenticate(sender, e);
			} else {
				OpenDetails(sender, e);
			}
		}
	}
}
