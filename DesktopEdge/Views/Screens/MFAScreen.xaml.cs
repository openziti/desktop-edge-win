/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

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
using System.Text.RegularExpressions;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MFA.xaml
    /// </summary>
    public partial class MFAScreen : UserControl {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public delegate void LoadEvent(bool isComplete, string title, string message);
        public event LoadEvent OnLoad;
        public event CommonDelegates.CloseAction OnClose;
        public delegate void ErrorOccurred(string message);
        public event ErrorOccurred OnError;
        private string[] _codes = new string[0];
        private ZitiIdentity zid;
        public int Type {
            get { return MFAScreenViewModel.Type; }
            set { MFAScreenViewModel.Type = value; }
        }

        public ZitiIdentity Identity {
            get {
                return this.zid;
            }
            set {
                this.zid = value;
            }
        }

        public MFAScreenViewModel MFAScreenViewModel { get; } = new MFAScreenViewModel();

        public MFAScreen() {
            InitializeComponent();
            DataContext = MFAScreenViewModel;
            MFAScreenViewModel.LoadRequested += OnVmLoad;
            MFAScreenViewModel.CloseRequested += OnVmClose;
            MFAScreenViewModel.ErrorRaised += OnVmError;
            MFAScreenViewModel.FocusAuthRequested += OnFocusAuth;
        }

        private void OnFocusAuth() {
            AuthCode.Focusable = true;
            AuthCode.Focus();
        }

        private void OnVmLoad(bool complete, string title, string message) {
            this.OnLoad?.Invoke(complete, title, message);
        }

        private void OnVmClose(bool success) {
            this.OnClose?.Invoke(success, this);
        }

        private void OnVmError(string message) {
            this.OnError?.Invoke(message);
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
            MFAScreenViewModel.SetupCode = "";
            this.zid = identity;
            MFAScreenViewModel.Identity = identity;
            MFAScreenViewModel.IdName = identity.Name;
            MFAScreenViewModel.ShowSetupMode();
            Logger.Debug($"MFA Url: {url}");
            MFAImage.Source = CreateQRFromUrl(url);
            SecretCode.Text = secret;
            MFAScreenViewModel.Url = url;
            SetupCode.Focus();
        }

        public void ShowRecovery(string[] codes, ZitiIdentity identity) {
            this.zid = identity;
            MFAScreenViewModel.Identity = identity;
            RecoveryList.Children.Clear();
            _codes = codes;
            if (codes.Length > 0) {
                for (int i = 0; i < codes.Length; i++) {
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
                MFAScreenViewModel.ShowRecoveryMode(true);
            } else {
                ShowMFA(this.zid, 2);
            }
        }

        public void ShowMFA(ZitiIdentity identity, int type) {
            if (identity.IsEnabled) {
                this.Type = type;
                MFAScreenViewModel.AuthCode = "";
                this.zid = identity;
                MFAScreenViewModel.Identity = identity;
                MFAScreenViewModel.ShowAuthMode();
            } else {
                ShowError("Identity disabled, MFA cannot continue.");
            }
        }

        private void SaveCodes(object sender, MouseButtonEventArgs e) {
            string fileText = string.Join("\n", _codes);
            string name = Regex.Replace(this.zid.Name, "[^a-zA-Z0-9]", String.Empty);

            System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog();
            dialog.Filter = "Text Files(*.txt)|*.txt|All(*.*)|*";
            dialog.Title = "Save Recovery Codes";
            dialog.FileName = name + "RecoveryCodes.txt";

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.Cancel) {
                File.WriteAllText(dialog.FileName, fileText);
            }
        }

        private void HandleKey(object sender, KeyEventArgs e) {
            if (e.Key == Key.Return) {
                MFAScreenViewModel.AuthSetupCommand.Execute(null);
            }
        }

        private void AuthCode_KeyUp(object sender, KeyEventArgs e) {
            if (e.Key == Key.Return) {
                MFAScreenViewModel.AuthCommand.Execute(null);
            }
        }
    }
}
