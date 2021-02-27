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
using System.Diagnostics;
using System.IO;
using System.Net;

namespace ZitiDesktopEdge {
	/// <summary>
	/// Interaction logic for MFA.xaml
	/// </summary>
	public partial class MFAScreen : UserControl {

		public delegate void CloseAction(bool isComplete);
		public event CloseAction OnClose;
		private string _url = "";
		public delegate void ErrorOccurred(string message);
		public event ErrorOccurred OnError;
		private string[] _codes = new string[0];

		public MFAScreen() {
			InitializeComponent();
		}

		private void ExecuteClose(object sender, MouseButtonEventArgs e) {
			this.OnClose?.Invoke(false);
		}

		private void ShowError(string message) {
			this.OnError?.Invoke(message);
		}

		public void ShowSetup(string idName, string url, string imageUrl) {
			IdName.Content = idName;
			MFAImage.Source = LoadImage(imageUrl);
			_url = url;
			MFAArea.Height = 560;
			MFAAuthArea.Visibility = Visibility.Collapsed;
			MFASetupArea.Visibility = Visibility.Visible;
			MFARecoveryArea.Visibility = Visibility.Collapsed;
			for (int i = 1; i <= 6; i++) (this.FindName("SetupAuth" + i) as TextBox).Text = "";
			SetupAuth1.Focus();
		}

		public void ShowRecovery(string[] codes) {
			MFASetupArea.Visibility = Visibility.Collapsed;
			MFAAuthArea.Visibility = Visibility.Collapsed;
			MFARecoveryArea.Visibility = Visibility.Visible;
			RecoveryList.Children.Clear();
			_codes = codes;
			MFAArea.Height = 380;
			for (int i=0; i<codes.Length; i++) {
				StackPanel panel = new StackPanel();
				panel.Orientation = Orientation.Horizontal;
				Label label = new Label();
				label.Content = codes[i];
				RecoveryList.Children.Add(label);
			}
		}

		public void ShowMFA() {
			MFASetupArea.Visibility = Visibility.Collapsed;
			MFARecoveryArea.Visibility = Visibility.Collapsed;
			MFAAuthArea.Visibility = Visibility.Visible;
			MFAArea.Height = 220;
			for (int i = 1; i <= 6; i++) (this.FindName("Auth" + i) as TextBox).Text = "";
			for (int i = 1; i <= 8; i++) (this.FindName("Rec" + i) as TextBox).Text = "";
			Auth1.Focus();
		}

		private BitmapImage LoadImage(string url) {
			var imgUrl = new Uri(url);
			var imageData = new WebClient().DownloadData(imgUrl);
			var bitmapImage = new BitmapImage { CacheOption = BitmapCacheOption.OnLoad };
			bitmapImage.BeginInit();
			bitmapImage.StreamSource = new MemoryStream(imageData);
			bitmapImage.EndInit();
			return bitmapImage;
		}

		private void GoTo(object sender, MouseButtonEventArgs e) {
			Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
		}

		private void GoNextSetup(object sender, KeyEventArgs e) {
			TextBox sentFrom = sender as TextBox;
			string name = sentFrom.Name;
			int index = 1;
			if (name == "SetupAuth1") index = 2;
			else if (name == "SetupAuth2") index = 3;
			else if (name == "SetupAuth3") index = 4;
			else if (name == "SetupAuth4") index = 5;
			else if (name == "SetupAuth5") index = 6;

			TextBox entry = this.FindName("SetupAuth" + index) as TextBox;
			if (sentFrom.Text.Length > 0) {
				entry.Focus();
			}
		}

		private void GoNext(object sender, KeyEventArgs e) {
			TextBox sentFrom = sender as TextBox;
			string name = sentFrom.Name;
			int index = 1;
			if (name == "Auth1") index = 2;
			else if (name == "Auth2") index = 3;
			else if (name == "Auth3") index = 4;
			else if (name == "Auth4") index = 5;
			else if (name == "Auth5") index = 6;

			TextBox entry = this.FindName("Auth"+index) as TextBox;
			if (sentFrom.Text.Length>0) {
				entry.Focus();
			}
		}

		private void GoNextRecovery(object sender, KeyEventArgs e) {
			TextBox sentFrom = sender as TextBox;
			string name = sentFrom.Name;
			int index = 1;
			if (name == "Rec1") index = 2;
			else if (name == "Rec2") index = 3;
			else if (name == "Rec3") index = 4;
			else if (name == "Rec4") index = 5;
			else if (name == "Rec5") index = 6;
			else if (name == "Rec6") index = 7;
			else if (name == "Rec7") index = 8;

			TextBox entry = this.FindName("Rec" + index) as TextBox;
			if (sentFrom.Text.Length > 0) {
				entry.Focus();
			}
		}

		private void ToggleRecovery(object sender, MouseButtonEventArgs e) {
			if (AuthRecoveryArea.Visibility==Visibility.Visible) {
				AuthRecoveryArea.Visibility = Visibility.Collapsed;
				AuthCodeArea.Visibility = Visibility.Visible;
				ToggleType.Content = "Use Recovery Code";
				AuthSubTitle.Content = "Enter your authorization code";
				Auth1.Focus();
			} else {
				AuthRecoveryArea.Visibility = Visibility.Visible;
				AuthCodeArea.Visibility = Visibility.Collapsed;
				ToggleType.Content = "Use Auth Code";
				AuthSubTitle.Content = "Enter a recovery code";
				Rec1.Focus();
			}
		}

		private void SaveCodes(object sender, MouseButtonEventArgs e) {

		}

		private void DoSetupAuthenticate(object sender, MouseButtonEventArgs e) {
			string code = SetupAuth1.Text + SetupAuth2.Text + SetupAuth3.Text + SetupAuth4.Text + SetupAuth5.Text + SetupAuth6.Text;

			// Clint - Execute the MFA with mah code
		}

		private void DoAuthenticate(object sender, MouseButtonEventArgs e) {
			string code = "";
			if (AuthRecoveryArea.Visibility == Visibility.Visible) {
				code = Rec1.Text + Rec2.Text + Rec3.Text + Rec4.Text + Rec5.Text + Rec6.Text + Rec7.Text + Rec8.Text;
				if (code.Length != 8) this.ShowError("You must enter a valid code");
			} else {
				code = Auth1.Text + Auth2.Text + Auth3.Text + Auth4.Text + Auth5.Text + Auth6.Text;
				if (code.Length != 6) this.ShowError("You must enter a valid code");
			}

			// Clint - Execute the MFA with mah code
		}

		private void RegenerateCodes(object sender, MouseButtonEventArgs e) {
			// Clint - Call the regen service
		}
	}
}
