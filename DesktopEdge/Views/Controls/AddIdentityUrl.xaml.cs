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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.Web.Http;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    public partial class AddIdentityUrl : UserControl {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public event CommonDelegates.CloseAction OnClose;
        public event Action<EnrollIdentifierPayload, UserControl> OnAddIdentity;

        public CommonDelegates.JoinNetwork JoinNetwork;

        public AddIdentityViewModel AddIdentityViewModel { get; } = new AddIdentityViewModel();

        // Fires RunDiscoveryAsync after the user stops typing in the URL field.
        private readonly DispatcherTimer _discoveryDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };

        public AddIdentityUrl() {
            InitializeComponent();
            DataContext = AddIdentityViewModel;
            _discoveryDebounce.Tick += DiscoveryDebounce_Tick;
        }

        private async void DiscoveryDebounce_Tick(object sender, EventArgs e) {
            _discoveryDebounce.Stop();
            await RunDiscoveryAsync();
        }

        private void JoinNetworkUrl(object sender, MouseButtonEventArgs e) {
            // Button is only enabled after RunDiscoveryAsync succeeds, so we can trust the VM state here.
            Uri raw = new Uri(ControllerURL.Text);
            string controllerBaseUrl = raw.GetLeftPart(UriPartial.Authority);
            EnrollIdentifierPayload payload = new EnrollIdentifierPayload();
            payload.ControllerURL = controllerBaseUrl;
            payload.IdentityFilename = raw.Host + "_" + raw.Port;
            OnAddIdentity(payload, this);
        }

        private async Task<bool> RunDiscoveryAsync() {
            if (!IsUrlSyntacticallyValid()) return false;
            Mouse.OverrideCursor = Cursors.Wait;
            try {
                Uri raw = new Uri(ControllerURL.Text);
                string baseUrl = raw.GetLeftPart(UriPartial.Authority);
                var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var result = await client.GetAsync(baseUrl + "/external-jwt-signers");
                result.EnsureSuccessStatusCode();
                string body = await result.Content.ReadAsStringAsync();
                AddIdentityViewModel.LoadSigners(body);
                JoinNetworkBtn.Enable();
                return true;
            } catch (Exception ex) {
                await ShowConnectionErrorAsync(ex);
                return false;
            } finally {
                Mouse.OverrideCursor = null;
            }
        }

        private async Task ShowConnectionErrorAsync(Exception ex) {
            MainWindow mw = (MainWindow)Application.Current.MainWindow;
            if (ex.InnerException is System.Net.WebException webEx) {
                if (webEx.Status == System.Net.WebExceptionStatus.TrustFailure) {
                    await mw.ShowBlurbAsync("Untrusted certificate or TLS error", "");
                    logger.Warn(ex, "TLS trust issue with URL: {0}", ControllerURL.Text);
                } else if (webEx.Status == System.Net.WebExceptionStatus.NameResolutionFailure) {
                    await mw.ShowBlurbAsync("Invalid or unreachable host name", "");
                    logger.Warn(ex, "Bunk URL or DNS resolution failed: {0}", ControllerURL.Text);
                } else {
                    await mw.ShowBlurbAsync("Unexpected error accessing URL", "");
                    logger.Warn(ex, "Could not connect to URL, status={0}", webEx.Status);
                }
            } else {
                await mw.ShowBlurbAsync("Unexpected error accessing URL", "");
                logger.Warn(ex, "Unexpected exception {0}", ex.Message);
            }
        }

        private bool IsUrlSyntacticallyValid() {
            try {
                Uri ctrl = new Uri(ControllerURL.Text);
                return ctrl.Host.Contains(".") && ctrl.Host.Length >= 3;
            } catch {
                return false;
            }
        }

        private void ExecuteClose(object sender, MouseButtonEventArgs e) {
            this.OnClose?.Invoke(false, this);
        }

        private void Grid_Loaded(object sender, System.Windows.RoutedEventArgs e) {
            ControllerURL.Focus();
            // Remains disabled until RunDiscoveryAsync succeeds.
            JoinNetworkBtn.Disable();         }

        private void ControllerURL_TextChanged(object sender, TextChangedEventArgs e) {
            if (ControllerURL.ActualWidth > 0) {
                ControllerURL.MaxWidth = ControllerURL.ActualWidth; //disable any expanding
            }
            // URL changed: any prior discovery is stale, so invalidate state and disable Join until re-run.
            AddIdentityViewModel.Reset();
            if (JoinNetworkBtn != null) JoinNetworkBtn.Disable();
            UpdateUrlValidity();
            // Restart the debounce. Discovery fires after the user pauses typing.
            // Skip the on-load placeholder so opening the dialog doesn't fire a request.
            _discoveryDebounce.Stop();
            if (IsUrlSyntacticallyValid() && ControllerURL.Text != AddIdentityViewModel.UrlPlaceholder) _discoveryDebounce.Start();
        }

        private void UpdateUrlValidity() {
            if (IsUrlSyntacticallyValid()) {
                ControllerURL.Style = (Style)Resources["ValidUrl"];
            } else {
                ControllerURL.Style = (Style)Resources["InvalidUrl"];
                if (JoinNetworkBtn != null) JoinNetworkBtn.Disable();
            }
        }

        private void HandleEnterKey(object sender, KeyEventArgs e) {
            // Enter submits only once discovery has enabled the button. Before that it does nothing
            // the debounce is already running to trigger discovery.
            if (e.Key == Key.Return && JoinNetworkBtn.IsEnabled) {
                e.Handled = true;
                JoinNetworkUrl(sender, null);
            }
        }

    }
}
