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
	/// Interaction logic for IdentityDetails.xaml
	/// </summary>
	public partial class IdentityDetails:UserControl {

		private ZitiIdentity _identity;

		public ZitiIdentity Identity {
			get {
				return _identity;
			}
			set {
				_identity = value;
				UpdateView();
			}
		}

		public void UpdateView() {
			IdDetailName.Content = _identity.Name;
			IdDetailToggle.Enabled = _identity.IsEnabled;
			IdentityName.Value = _identity.Name;
			IdentityNetwork.Value = _identity.ControllerUrl;
			IdentityEnrollment.Value = _identity.EnrollmentStatus;
			IdentityStatus.Value = _identity.Status;
			ServiceList.Children.Clear();
			IdDetailToggle.OnToggled += IdToggle;
			for (int i=0; i<_identity.Services.Length; i++) {
				MenuEditItem editor = new MenuEditItem();
				editor.Label = _identity.Services[i].Name;
				editor.Value = _identity.Services[i].Url;
				editor.IsLocked = true;
				ServiceList.Children.Add(editor);
			}
		}

		private void IdToggle(bool on) {
			// Clint, turn me on or turn me off
		}

		public IdentityDetails() {
			InitializeComponent();
		}
		private void HideMenu(object sender, MouseButtonEventArgs e) {
			this.Visibility = Visibility.Collapsed;
		}

		private void ForgetIdentity(object sender, MouseButtonEventArgs e) {
			// Clint Forget and bubble me up to remove
			// this.Visibility = Visibility.Collapsed;
		}
	}
}
