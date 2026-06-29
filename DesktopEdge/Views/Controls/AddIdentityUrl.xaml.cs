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
using Windows.Web.Http;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    public partial class AddIdentityUrl : UserControl {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public event CommonDelegates.CloseAction OnClose;
        public event Action<EnrollIdentifierPayload, UserControl> OnAddIdentity;
        public event Action<AddIdentityViewModel, UserControl> OnNeedsSignerChoice;

        public CommonDelegates.JoinNetwork JoinNetwork;

        public AddIdentityViewModel AddIdentityViewModel { get; } = new AddIdentityViewModel();

        public AddIdentityUrl() {
            InitializeComponent();
            DataContext = AddIdentityViewModel;
        }

        private async void JoinNetworkUrl(object sender, MouseButtonEventArgs e) {
            if (!IsUrlSyntacticallyValid()) return;

            bool ok = await RunDiscoveryAsync();
            if (!ok) {
                this.OnClose?.Invoke(false, this);
                return;
            }

            Uri raw = new Uri(ControllerURL.Text);
            AddIdentityViewModel.ControllerBaseUrl = raw.GetLeftPart(UriPartial.Authority);
            AddIdentityViewModel.IdentityFilename = raw.Host + "_" + raw.Port;

            bool needsChoice = AddIdentityViewModel.ShowSignerPicker
                            || AddIdentityViewModel.EnrollModeRadiosVisibility == Visibility.Visible;
            if (needsChoice) {
                OnNeedsSignerChoice?.Invoke(AddIdentityViewModel, this);
                return;
            }

            OnAddIdentity(AddIdentityViewModel.BuildEnrollPayload(), this);
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
                return true;
            } catch (Exception ex) {
                logger.Error(ex, "connecting to the url provided failed");
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
            JoinNetworkBtn.Disable();
        }

        /// <summary>
        /// Reset the textbox for a fresh entry: if the clipboard holds an http(s) URL, paste it;
        /// otherwise restore the placeholder. Either way, focus the field and select all so the
        /// first keystroke replaces it.
        /// </summary>
        public void PrepareForEntry() {
            string clipboardUrl = TryReadHttpUrlFromClipboard();
            ControllerURL.Text = clipboardUrl ?? AddIdentityViewModel.UrlPlaceholder;
            // Defer BOTH the validity refresh and the focus/select to Loaded priority. Two
            // reasons:
            //   1. `Grid_Loaded` runs `JoinNetworkBtn.Disable()` to set the initial state.
            //      On paths where this UserControl is shown for the first time in the session
            //      (e.g. the welcome screen "Add by URL" link on a zero-identities install),
            //      Grid_Loaded fires AFTER ShowJoinByUrl's PrepareForEntry call, so a
            //      synchronous Enable() here gets overwritten by Disable() moments later.
            //   2. TextChanged only fires when the value actually changes, so if the user
            //      reopens the dialog with the same clipboard URL already in the field, the
            //      validator never runs -- the explicit call below covers that case too.
            //   3. SelectAll runs before the TextBox is focusable otherwise and gets dropped.
            Dispatcher.BeginInvoke(new Action(() => {
                UpdateUrlValidity();
                ControllerURL.Focus();
                ControllerURL.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static string TryReadHttpUrlFromClipboard() {
            try {
                if (!Clipboard.ContainsText()) return null;
                string text = Clipboard.GetText()?.Trim();
                if (string.IsNullOrEmpty(text)) return null;
                if (Uri.TryCreate(text, UriKind.Absolute, out Uri uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
                    return text;
                }
                return null;
            } catch (Exception ex) {
                logger.Warn(ex, "could not read clipboard for URL prefill");
                return null;
            }
        }

        private void ControllerURL_TextChanged(object sender, TextChangedEventArgs e) {
            AddIdentityViewModel.Reset();
            UpdateUrlValidity();
        }

        private void UpdateUrlValidity() {
            bool valid = IsUrlSyntacticallyValid() && ControllerURL.Text != AddIdentityViewModel.UrlPlaceholder;
            if (valid) {
                ControllerURL.Style = (Style)Resources["ValidUrl"];
                if (JoinNetworkBtn != null) JoinNetworkBtn.Enable();
            } else {
                ControllerURL.Style = (Style)Resources["InvalidUrl"];
                if (JoinNetworkBtn != null) JoinNetworkBtn.Disable();
            }
        }

        private void HandleEnterKey(object sender, KeyEventArgs e) {
            if (e.Key == Key.Return && JoinNetworkBtn.IsEnabled) {
                e.Handled = true;
                JoinNetworkUrl(sender, null);
            }
        }

    }
}
