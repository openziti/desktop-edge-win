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
using System.Windows.Media.Animation;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;
using ZitiDesktopEdge.DataStructures;

using NLog;
using QRCoder;

namespace ZitiDesktopEdge {
	/// <summary>
	/// Interaction logic for MFA.xaml
	/// </summary>
	public partial class MFAScreen : UserControl {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public delegate void CloseAction(bool isComplete);
		public event CloseAction OnClose;
		private string _url = "";
		public delegate void ErrorOccurred(string message);
		public event ErrorOccurred OnError;
		private string[] _codes = new string[0];
		DataClient serviceClient = (DataClient)Application.Current.Properties["ServiceClient"];
		private ZitiIdentity _identity;

		public MFAScreen() {
			InitializeComponent();
		}

		private void ExecuteClose(object sender, MouseButtonEventArgs e) {
			this.OnClose?.Invoke(false);
		}

		private void ShowError(string message) {
			this.OnError?.Invoke(message);
		}

		private BitmapImage CreateQRFromUrl(string url) {
			QRCodeGenerator qrGenerator = new QRCodeGenerator();
			QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
			QRCode qrCode = new QRCode(qrCodeData);
			System.Drawing.Bitmap qrCodeImage = qrCode.GetGraphic(20);

			MemoryStream ms = new MemoryStream();
			((System.Drawing.Bitmap)qrCodeImage).Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			ms.Seek(0, SeekOrigin.Begin);
			image.StreamSource = ms;
			image.EndInit();
			return image;
		}

		public void ShowSetup(ZitiIdentity identity, string url, string secret) {
			this._identity = identity;
			MFAImage.Visibility = Visibility.Visible;
			SecretCode.Visibility = Visibility.Collapsed;
			SecretButton.Content = "Show Secret";
			IdName.Content = identity.Name;
			Logger.Debug($"MFA Url: {url}");
			
			MFAImage.Source = CreateQRFromUrl(url);
			SecretCode.Text = secret;

			_url = url;
			MFAArea.Height = 515;
			MFAAuthArea.Visibility = Visibility.Collapsed;
			MFASetupArea.Visibility = Visibility.Visible;
			MFARecoveryArea.Visibility = Visibility.Collapsed;
			SeperationColor.Visibility = Visibility.Visible;
			for (int i = 1; i <= 6; i++) (this.FindName("SetupAuth" + i) as TextBox).Text = "";
			SetupAuth1.Focus();
		}

		public void ShowRecovery(string[] codes, ZitiIdentity identity) {
			this._identity = identity;
			MFASetupArea.Visibility = Visibility.Collapsed;
			MFAAuthArea.Visibility = Visibility.Collapsed;
			SeperationColor.Visibility = Visibility.Collapsed;
			MFARecoveryArea.Visibility = Visibility.Visible;
			RecoveryList.Children.Clear();
			_codes = codes;
			MFAArea.Height = 380;
			for (int i=0; i<codes.Length; i++) {
				TextBox label = new TextBox();
				label.Text = codes[i];
				label.BorderThickness = new Thickness(0);
				label.Background = new SolidColorBrush();
				label.Background.Opacity = 0;
				label.HorizontalAlignment = HorizontalAlignment.Center;
				label.HorizontalContentAlignment = HorizontalAlignment.Center;
				label.VerticalAlignment = VerticalAlignment.Center;
				label.VerticalContentAlignment = VerticalAlignment.Center;
				RecoveryList.Children.Add(label);
			}
		}

		public void ShowMFA(ZitiIdentity identity) {
			this._identity = identity;
			MFASetupArea.Visibility = Visibility.Collapsed;
			MFARecoveryArea.Visibility = Visibility.Collapsed;
			SeperationColor.Visibility = Visibility.Collapsed;
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
				entry.Select(0, entry.Text.Length);
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

		async private void ToggleRecovery(object sender, MouseButtonEventArgs e) {
			serviceClient = (DataClient)Application.Current.Properties["ServiceClient"];
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
		async private void ReturnMFACodes(object sender, MouseButtonEventArgs e) {
			string code = Auth1.Text + Auth2.Text + Auth3.Text + Auth4.Text + Auth5.Text + Auth6.Text; //jeremyfix
			Logger.Debug("AuthMFA successful.");
			MfaRecoveryCodesResponse getcodes = await serviceClient.ReturnMFACodes(this._identity.Fingerprint, code);
			if (getcodes.Code != 0) {
				Logger.Error("AuthMFA failed. " + getcodes.Message);
			}
			Logger.Error("PAYLOAD: {0}", getcodes.Payload);
		}
		async private void GenerateMFACodes(object sender, MouseButtonEventArgs e) {
			string code = Rec1.Text + Rec2.Text + Rec3.Text + Rec4.Text + Rec5.Text + Rec6.Text; //jeremyfix
			MfaRecoveryCodesResponse gencodes = await serviceClient.GenerateMFACodes(this._identity.Fingerprint, code);
			if (gencodes.Code != 0) {
				Logger.Error("AuthMFA failed. " + gencodes.Message);
			}
			Logger.Error("PAYLOAD: {0}", gencodes.Payload);
		}

		private void SaveCodes(object sender, MouseButtonEventArgs e) {
			string fileText = string.Join("\n", _codes);

			System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog();
			dialog.Filter = "Text Files(*.txt)|*.txt|All(*.*)|*";
			dialog.Title = "Save Recovery Codes";
			dialog.FileName = "RecoveryCodes.txt";
			dialog.ShowDialog();

			if (dialog.FileName != "") {
				File.WriteAllText(dialog.FileName, fileText);
			}
		}

		async private void DoSetupAuthenticate(object sender, MouseButtonEventArgs e) {
			AuthBgColor.Color = Color.FromRgb(0, 104, 249);
			string code = SetupAuth1.Text + SetupAuth2.Text + SetupAuth3.Text + SetupAuth4.Text + SetupAuth5.Text + SetupAuth6.Text;

			DataClient serviceClient = serviceClient = (DataClient)Application.Current.Properties["ServiceClient"];
			SvcResponse resp = await serviceClient.VerifyMFA(this._identity.Fingerprint, code);
			if (resp.Code != 0) {

            } else {

            }
		}

		async private void DoAuthenticate(object sender, MouseButtonEventArgs e) {
			string code = "";
			if (AuthRecoveryArea.Visibility == Visibility.Visible) {
				code = Rec1.Text + Rec2.Text + Rec3.Text + Rec4.Text + Rec5.Text + Rec6.Text + Rec7.Text + Rec8.Text;
				if (code.Length != 8) this.ShowError("You must enter a valid code");
			} else {
				code = Auth1.Text + Auth2.Text + Auth3.Text + Auth4.Text + Auth5.Text + Auth6.Text;
				if (code.Length != 6) this.ShowError("You must enter a valid code");


				DataClient serviceClient = serviceClient = (DataClient)Application.Current.Properties["ServiceClient"];
				SvcResponse authResult = await serviceClient.AuthMFA(this._identity.Fingerprint, code);
				if (authResult?.Code != 0) {
					Logger.Error("AuthMFA failed. " + authResult.Message);
				} else {
					//success - hide the screen Jeremy
				}
			}
		}

		async private void DoAuthenticateb(object sender, MouseButtonEventArgs e) {
			string code = "";
			if (AuthRecoveryArea.Visibility == Visibility.Visible) {
				code = Rec1.Text + Rec2.Text + Rec3.Text + Rec4.Text + Rec5.Text + Rec6.Text + Rec7.Text + Rec8.Text;
				if (code.Length != 8) this.ShowError("You must enter a valid code");
			} else {
				code = Auth1.Text + Auth2.Text + Auth3.Text + Auth4.Text + Auth5.Text + Auth6.Text;
				if (code.Length != 6) this.ShowError("You must enter a valid code");


				DataClient serviceClient = serviceClient = (DataClient)Application.Current.Properties["ServiceClient"];
				SvcResponse authResult = await serviceClient.VerifyMFA(this._identity.Fingerprint, code);
				if (authResult?.Code != 0) {
					Logger.Error("VerifyMFA failed. " + authResult.Message);
				} else {
					//success - hide the screen Jeremy
				}
			}
		}

		private void RegenerateCodes(object sender, MouseButtonEventArgs e) {
			// Clint - Call the regen service
		}

		/// <summary>
		/// When the button area is entered slowly make it slightly opaque
		/// </summary>
		/// <param name="sender">The button object</param>
		/// <param name="e">The mouse event</param>
		private void HoverAuthSetup(object sender, MouseEventArgs e) {
			AuthSetupBg.Opacity = 0.8;
			AuthSetupBg.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromSeconds(.3)));
		}

		/// <summary>
		/// When the mouse leaves the button ara snap the opacity back to full
		/// </summary>
		/// <param name="sender">The button object</param>
		/// <param name="e">The mouse event</param>
		private void LeaveAuthSetup(object sender, MouseEventArgs e) {
			AuthBgColor.Color = Color.FromRgb(0, 104, 249);
			AuthSetupBg.BeginAnimation(Grid.OpacityProperty, null);
			AuthSetupBg.Opacity = 0.8;
		}

		private void AuthSetupClicked(object sender, MouseButtonEventArgs e) {
			AuthBgColor.Color = Color.FromRgb(126, 180, 255);
		}

		private void ShowSecret(object sender, MouseButtonEventArgs e) {
			if (SecretCode.Visibility==Visibility.Visible) {
				MFAImage.Visibility = Visibility.Visible;
				SecretCode.Visibility = Visibility.Collapsed;
				SecretButton.Content = "Show Secret";
			} else {
				MFAImage.Visibility = Visibility.Collapsed;
				SecretCode.Visibility = Visibility.Visible;
				SecretButton.Content = "Show QR Code";
			}
		}
	}
}
