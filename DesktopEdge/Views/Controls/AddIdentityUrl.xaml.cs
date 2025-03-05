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
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Windows.Web.Http;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    public partial class AddIdentityUrl : UserControl {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public event CommonDelegates.CloseAction OnClose;
        public event Action<EnrollIdentifierPayload, UserControl> OnAddIdentity;

        public CommonDelegates.JoinNetwork JoinNetwork;

        public AddIdentityUrl() {
            InitializeComponent();
        }

        private async void JoinNetworkUrl(object sender, MouseButtonEventArgs e) {
            EnrollIdentifierPayload payload = new EnrollIdentifierPayload();
            payload.ControllerURL = ControllerURL.Text;

            Uri ctrl = new Uri(ControllerURL.Text);
            payload.IdentityFilename = ctrl.Host + "_" + ctrl.Port;

            var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            Mouse.OverrideCursor = Cursors.Wait;
            try {
                var result = client.GetAsync(ControllerURL.Text).Result;
                OnAddIdentity(payload, this);
            } catch {
                this.OnClose?.Invoke(false, this);
                await ((MainWindow)Application.Current.MainWindow).ShowBlurbAsync("Timed out accessing URL", "");
                logger.Warn("could not connect to url");
            }
            Mouse.OverrideCursor = null;
        }

        private void ExecuteClose(object sender, MouseButtonEventArgs e) {
            this.OnClose?.Invoke(false, this);
        }

        private void Grid_Loaded(object sender, System.Windows.RoutedEventArgs e) {
            ControllerURL.Focus();
        }

        private void ControllerURL_TextChanged(object sender, TextChangedEventArgs e) {
            if (ControllerURL.ActualWidth > 0) {
                ControllerURL.MaxWidth = ControllerURL.ActualWidth; //disable any expanding
            }
            UpdateUrlValidity();
        }

        private void UpdateUrlValidity() {
            bool valid = true;
            try {
                // check that it looks like a url
                Uri ctrl = new Uri(ControllerURL.Text);
                if (!ctrl.Host.Contains(".") || ctrl.Host.Length < 3) {
                    valid = false;
                }
            } catch {
                // not a url -- don't allow it
                valid = false;
            }
            if(valid) {
                ControllerURL.Style = (Style)Resources["ValidUrl"];
                if (JoinNetworkBtn != null) JoinNetworkBtn.Enable();
            } else {
                ControllerURL.Style = (Style)Resources["InvalidUrl"];
                if (JoinNetworkBtn != null) JoinNetworkBtn.Disable();
            }
        }
        private void HandleEnterKey(object sender, KeyEventArgs e) {
            if (e.Key == Key.Return) {
                e.Handled = true;
                this.JoinNetworkUrl(sender, null);
            }
        }
    }
}
