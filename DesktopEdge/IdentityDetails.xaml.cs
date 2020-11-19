using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	/// Interaction logic for IdentityDetails.xaml
	/// </summary> 
	public partial class IdentityDetails:UserControl {

		private bool _isAttached = true;
		public delegate void Forgot(ZitiIdentity forgotten);
		public event Forgot OnForgot;
		public delegate void ErrorOccurred(string message);
		public event ErrorOccurred OnError;
		public delegate void Detched(MouseButtonEventArgs e);
		public event Detched OnDetach;
		public double MainHeight = 500;

		private List<ZitiIdentity> identities {
			get {
				return (List<ZitiIdentity>)Application.Current.Properties["Identities"];
			}
		}

		private ZitiIdentity _identity;

		public ZitiIdentity Identity {
			get {
				return _identity;
			}
			set {
				_identity = value;
				this.IdDetailToggle.Enabled = _identity.IsEnabled;
				UpdateView();
				IdentityArea.Opacity = 1.0;
				IdentityArea.Visibility = Visibility.Visible;
				this.Visibility = Visibility.Visible;
			}
		}

		public IdentityItem SelectedIdentity { get; set; }

		private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
			if (e.ChangedButton == MouseButton.Left) {
				_isAttached = false;
				OnDetach(e);
			}
		}


		public bool IsAttached {
			get {
				return _isAttached;
			}
			set {
				_isAttached = value;
				if (_isAttached) {
					Arrow.Visibility = Visibility.Visible;
					ConfirmArrow.Visibility = Visibility.Visible;
				} else {
					Arrow.Visibility = Visibility.Collapsed;
					ConfirmArrow.Visibility = Visibility.Collapsed;
				}
			}
		}

		public void UpdateView() {
			IdDetailName.Text = _identity.Name;
			IdDetailName.ToolTip = _identity.Name;
			IdDetailToggle.Enabled = _identity.IsEnabled;
			IdentityName.Value = _identity.Name;
			IdentityNetwork.Value = _identity.ControllerUrl;
			IdentityEnrollment.Value = _identity.Status;
			IdentityStatus.Value = _identity.IsEnabled ? "active" : "disabled";
			ServiceList.Children.Clear();
			if (_identity.Services.Count>0) {
				for (int i = 0; i < _identity.Services.Count; i++) {
					ServiceInfo editor = new ServiceInfo();
					editor.Label = _identity.Services[i].Name;
					editor.Value = _identity.Services[i].Url;
					editor.Warning = _identity.Services[i].Warning;
					editor.IsLocked = true;
					ServiceList.Children.Add(editor);
				}
				double newHeight = MainHeight - 300;
				ServiceRow.Height = new GridLength((double)newHeight);
				MainDetailScroll.MaxHeight = newHeight;
				MainDetailScroll.Height = newHeight;
				MainDetailScroll.Visibility = Visibility.Visible;
				ServiceTitle.Content = _identity.Services.Count + " SERVICES";
			} else {
				ServiceRow.Height = new GridLength((double)0.0);
				MainDetailScroll.Visibility = Visibility.Collapsed;
				ServiceTitle.Content = "NO SERVICES AVAILABLE";
			}
		}

		async private void IdToggle(bool on) {
			DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
			await client.IdentityOnOffAsync(_identity.Fingerprint, on);
			SelectedIdentity.ToggleSwitch.Enabled = on;
			_identity.IsEnabled = on;
			IdentityStatus.Value = _identity.IsEnabled ? "active" : "disabled";
		}

		public IdentityDetails() {
			InitializeComponent();
		}
		private void HideMenu(object sender, MouseButtonEventArgs e) {
			this.Visibility = Visibility.Collapsed;
		}

		public void SetHeight(double height) {
			MainDetailScroll.Height = height;
		}

		private void ForgetIdentity(object sender, MouseButtonEventArgs e) {
			if (this.Visibility==Visibility.Visible) {
				ConfirmView.Visibility = Visibility.Visible;
			}
		}

		private void CancelConfirmButton_Click(object sender, RoutedEventArgs e) {
			ConfirmView.Visibility = Visibility.Collapsed;
		}

		async private void ConfirmButton_Click(object sender, RoutedEventArgs e) {
			this.Visibility = Visibility.Collapsed;
			DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
			try {
				await client.RemoveIdentityAsync(_identity.Fingerprint);

				ZitiIdentity forgotten = new ZitiIdentity();
				foreach (var id in identities) {
					if (id.Fingerprint == _identity.Fingerprint) {
						forgotten = id;
						identities.Remove(id);
						break;
					}
				}

				if (OnForgot != null) {
					OnForgot(forgotten);
				}
			} catch (DataStructures.ServiceException se) {
				OnError(se.Message);
			} catch (Exception ex) {
				OnError(ex.Message);
			}
		}
	}
}
