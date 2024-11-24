using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using System;
using ZitiDesktopEdge.DataStructures;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZitiDesktopEdge {
    public partial class AddIdentityCA : UserControl {
        public event SharedUserControlDefinitions.CloseAction OnClose;
        public event Action<EnrollIdentifierPayload, UserControl> OnAddIdentity;
        public EnrollIdentifierPayload Payload { get; set; }

        public AddIdentityCA() {
            InitializeComponent();
        }

        private void ExecuteClose(object sender, MouseButtonEventArgs e) {
            this.OnClose?.Invoke(false, this);
        }

        private void BrowseCertificateFile_Click(object sender, RoutedEventArgs e) {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Title = "Select Certificate File",
                Filter = "Certificate Files (*.crt;*.pem)|*.crt;*.pem|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true) {
                CertificateFile.Text = openFileDialog.FileName;
            }
        }

        private void BrowseKeyFile_Click(object sender, RoutedEventArgs e) {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Title = "Select Key File",
                Filter = "Key Files (*.key;*.pem)|*.key;*.pem|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true) {
                KeyFile.Text = openFileDialog.FileName;
            }
        }

        public void Join() {
            throw new System.NotImplementedException();
        }

        private void JoinNetworkUrl(object sender, MouseButtonEventArgs e) {
            Console.WriteLine("join");

            Payload.Certificate = CertificateFile.Text;
            Payload.Key = KeyFile.Text;
            Payload.Alias = Alias.Text;
            OnAddIdentity(Payload, this);
        }
        static string GetCertificateFingerprint(string filePath) {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The file was not found.", filePath);

            // Load the certificate
            var cert = new X509Certificate2(filePath);

            // Compute the SHA-256 fingerprint
            using (var hashAlgorithm = SHA256.Create()) {
                var hash = hashAlgorithm.ComputeHash(cert.RawData);
                return BitConverter.ToString(hash).Replace("-", ":").ToUpper();
            }
        }

        static string GetCertificateCommonName(string filePath) {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

            // Load the certificate
            var cert = new X509Certificate2(filePath);

            // Extract the Subject field
            string subject = cert.Subject;

            // Find and extract the CN part
            const string cnPrefix = "CN=";
            int cnIndex = subject.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);
            if (cnIndex >= 0) {
                int start = cnIndex + cnPrefix.Length;
                int end = subject.IndexOf(',', start);
                return end > 0 ? subject.Substring(start, end - start) : subject.Substring(start);
            }

            return "Enter Alias";
        }

        //private void Toggle_Checked(object sender, RoutedEventArgs e) {
        //    if (WithJwtToggle != null && UrlInputPanel != null) {
        //        if (WithJwtToggle.IsChecked == true) {
        //            JwtInputPanel.Visibility = Visibility.Visible;
        //            UrlInputPanel.Visibility = Visibility.Collapsed;
        //        } else if (WithUrlToggle.IsChecked == true) {
        //            JwtInputPanel.Visibility = Visibility.Collapsed;
        //            UrlInputPanel.Visibility = Visibility.Visible;
        //        } else {
        //            JwtInputPanel.Visibility = Visibility.Collapsed;
        //            UrlInputPanel.Visibility = Visibility.Visible;
        //        }
        //    }
        //}

        private void BrowseJwtFile_Click(object sender, RoutedEventArgs e) {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            if (openFileDialog.ShowDialog() == true) {
                //JwtTextBox.Text = openFileDialog.FileName;
            }
        }

        private void ControllerURL_TextChanged(object sender, TextChangedEventArgs e) {
            //if (ControllerURL.ActualWidth > 0) {
            //    ControllerURL.MaxWidth = ControllerURL.ActualWidth; //disable any expanding
            //}
            //UpdateUrlValidity();
        }

        //private void UpdateUrlValidity() {
        //    bool valid = true;
        //    try {
        //        // check that it looks like a url
        //        Uri ctrl = new Uri(ControllerURL.Text);
        //        if (!ctrl.Host.Contains(".") || ctrl.Host.Length < 3) {
        //            valid = false;
        //        }
        //    } catch {
        //        // not a url -- don't allow it
        //        valid = false;
        //    }
        //    if (valid) {
        //        ControllerURL.Style = (Style)Resources["ValidUrl"];
        //        if (JoinNetworkBtn != null) JoinNetworkBtn.Enable();
        //    } else {
        //        ControllerURL.Style = (Style)Resources["InvalidUrl"];
        //        if (JoinNetworkBtn != null) JoinNetworkBtn.Disable();
        //    }
        //}
    }
}