﻿/*
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
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using System;
using ZitiDesktopEdge.DataStructures;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZitiDesktopEdge {
    public partial class AddIdentityCA : UserControl {
        public event CommonDelegates.CloseAction OnClose;
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
                Filter = "Certificate Files (*.crt;*.cert;*.pem)|*.crt;*.cert;*.pem|All Files (*.*)|*.*"
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

        private void JoinNetworkCA(object sender, MouseButtonEventArgs e) {
            Console.WriteLine("JoinNetworkCA");

            Payload.Certificate = CertificateFile.Text;
            Payload.Key = KeyFile.Text;
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

        private void ControllerURL_TextChanged(object sender, TextChangedEventArgs e) {
            //if (ControllerURL.ActualWidth > 0) {
            //    ControllerURL.MaxWidth = ControllerURL.ActualWidth; //disable any expanding
            //}
            //UpdateUrlValidity();
        }
    }
}