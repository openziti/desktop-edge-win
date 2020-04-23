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

		private List<ZitiIdentity> identities
		{
			get
			{
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
			for (int i=0; i<_identity.Services.Count; i++) {
				MenuEditItem editor = new MenuEditItem();
				editor.Label = _identity.Services[i].Name;
				editor.Value = _identity.Services[i].Url;
				editor.IsLocked = true;
				ServiceList.Children.Add(editor);
			}
		}

		private void IdToggle(bool on) {
			// Jeremy - on and off but it's not wired in yet!
			ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
			client.IdentityOnOff(_identity.Fingerprint, on);
		}

		public IdentityDetails() {
			InitializeComponent();
		}
		private void HideMenu(object sender, MouseButtonEventArgs e) {
			this.Visibility = Visibility.Collapsed;
		}

		private void ForgetIdentity(object sender, MouseButtonEventArgs e) {
			// Jeremy - this works now as long as you pass a fingerprint that's valid!
			this.Visibility = Visibility.Collapsed;
			ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
			try
			{
				client.RemoveIdentity(_identity.Fingerprint);

				foreach(var id in identities)
				{
					if(id.Fingerprint == _identity.Fingerprint)
					{
						identities.Remove(id);
						break;
					}
				}
			}
			catch (ServiceClient.ServiceException se)
			{
				MessageBox.Show(se.AdditionalInfo, se.Message);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Unexpected error 1", ex.Message);
			}
		}
	}
}
