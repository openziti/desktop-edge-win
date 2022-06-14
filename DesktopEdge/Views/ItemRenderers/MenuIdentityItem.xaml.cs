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
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MenuItem.xaml
    /// </summary>
    public partial class MenuIdentityItem : UserControl {

		private string _label = "";
		private ZitiIdentity _identity;

		public string Label {
			get {
				return _label;
			}
			set {
				this._label = value;
				MainLabel.Text = this._label;
			}
		}

		public ZitiIdentity Identity {
			get {
				return _identity;
			}
			set {
				_identity = value;
			}
		}

		public MenuIdentityItem() {
            InitializeComponent();
			ToggleSwitch.OnToggled += ToggleIdentity;
		}

		async private void ToggleIdentity(bool on) {
			try {
				DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
				DataStructures.Identity id = await client.IdentityOnOffAsync(_identity.Identifier, on);
				this.Identity.IsEnabled = on;
			} catch (DataStructures.ServiceException se) {
				MessageBox.Show(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				MessageBox.Show("Error", ex.Message);
			}
		}

		private void ShowIdentity(object sender, MouseButtonEventArgs e) {
			IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
			deets.SelectedIdentityMenu = this;
			deets.Identity = this.Identity;
		}
	}
}
