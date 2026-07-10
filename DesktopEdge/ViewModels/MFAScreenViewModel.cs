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
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using NLog;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    public enum MfaMode { Setup, Recovery, Auth }

    public class MFAScreenViewModel : INotifyPropertyChanged {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private MfaMode _mode = MfaMode.Setup;
        private bool _secretShown;
        private bool _hasRecoveryCodes = true;
        private string _idName = "Identity Name";
        private string _setupCode = "";
        private string _authCode = "";
        private bool _executing;

        public string IdName {
            get { return _idName; }
            set { _idName = value; OnPropertyChanged(nameof(IdName)); }
        }

        public ZitiIdentity Identity { get; set; }
        public int Type { get; set; }

        public string SetupCode {
            get { return _setupCode; }
            set { _setupCode = value; OnPropertyChanged(nameof(SetupCode)); }
        }

        public string AuthCode {
            get { return _authCode; }
            set { _authCode = value; OnPropertyChanged(nameof(AuthCode)); }
        }

        public event Action<bool, string, string> LoadRequested;
        public event Action<bool> CloseRequested;
        public event Action<string> ErrorRaised;
        public event Action FocusAuthRequested;

        public ActionCommand AuthSetupCommand { get; }
        public ActionCommand AuthCommand { get; }
        public ActionCommand ToggleSecretCommand { get; }
        public ActionCommand CloseCommand { get; }
        public ActionCommand RegenerateCommand { get; }
        public ActionCommand GoToCommand { get; }
        public string Url { get; set; }

        public MFAScreenViewModel() {
            AuthSetupCommand = new ActionCommand(DoSetupAuthenticate, () => true);
            AuthCommand = new ActionCommand(DoAuthenticate, () => true);
            ToggleSecretCommand = new ActionCommand(ToggleSecret, () => true);
            CloseCommand = new ActionCommand(() => CloseRequested?.Invoke(false), () => true);
            RegenerateCommand = new ActionCommand(Regenerate, () => true);
            GoToCommand = new ActionCommand(GoTo, () => true);
        }

        private void Regenerate() {
            Type = 4;
            AuthCode = "";
            ShowAuthMode();
        }

        private void GoTo() {
            if (!string.IsNullOrEmpty(Url)) {
                Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
            } else {
                ErrorRaised?.Invoke("Invalid MFA Url");
            }
        }

        private async void DoSetupAuthenticate() {
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            SvcResponse resp = await client.VerifyMFA(Identity.Identifier, SetupCode);
            // only close on failure; on success the enrollment_verification event swaps to recovery codes
            if (resp.Code != 0) {
                CloseRequested?.Invoke(false);
            }
        }

        /// <summary>
        /// Type: 1 = authenticate, 2 = get recovery codes, 3 = remove MFA, 4 = regenerate codes.
        /// </summary>
        private async void DoAuthenticate() {
            if (_executing) return;
            _executing = true;
            string code = AuthCode;
            if (code.Trim().Length > 0) {
                DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
                LoadRequested?.Invoke(false, "Authentication", "One Moment Please...");
                if (Type == 1) {
                    SvcResponse authResult = await client.AuthMFA(Identity.Identifier, code);
                    if (authResult?.Code != 0) {
                        Logger.Error("AuthMFA failed. " + authResult.Error);
                        ErrorRaised?.Invoke("Authentication Failed");
                        _executing = false;
                    } else {
                        Identity.IsMFANeeded = true;
                        CloseRequested?.Invoke(true);
                        _executing = false;
                    }
                    LoadRequested?.Invoke(true, "", "");
                } else if (Type == 2) {
                    MfaRecoveryCodesResponse codeResponse = await client.GetMFACodes(Identity.Identifier, code);
                    if (codeResponse?.Code != 0) {
                        Logger.Error("AuthMFA failed. " + codeResponse.Error);
                        AuthCode = "";
                        ErrorRaised?.Invoke("Authentication Failed");
                        _executing = false;
                    } else {
                        Identity.RecoveryCodes = codeResponse.Data.RecoveryCodes;
                        CloseRequested?.Invoke(true);
                        _executing = false;
                    }
                    LoadRequested?.Invoke(true, "", "");
                } else if (Type == 3) {
                    SvcResponse authResult = await client.RemoveMFA(Identity.Identifier, code);
                    if (authResult?.Code != 0) {
                        Logger.Error("AuthMFA failed. " + authResult.Error);
                        AuthCode = "";
                        ErrorRaised?.Invoke("Authentication Failed");
                        _executing = false;
                    } else {
                        CloseRequested?.Invoke(true);
                        _executing = false;
                    }
                    LoadRequested?.Invoke(true, "", "");
                } else if (Type == 4) {
                    MfaRecoveryCodesResponse codeResponse = await client.GenerateMFACodes(Identity.Identifier, code);
                    if (codeResponse?.Code != 0) {
                        Logger.Error("AuthMFA failed. " + codeResponse?.Error);
                        AuthCode = "";
                        ErrorRaised?.Invoke("Authentication Failed");
                    } else {
                        Identity.RecoveryCodes = codeResponse.Data.RecoveryCodes;
                        CloseRequested?.Invoke(true);
                    }
                    _executing = false;
                    LoadRequested?.Invoke(true, "", "");
                }
            }
        }

        public Visibility SetupAreaVisibility => _mode == MfaMode.Setup ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RecoveryAreaVisibility => _mode == MfaMode.Recovery ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AuthAreaVisibility => _mode == MfaMode.Auth ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MainBrushVisibility => _mode != MfaMode.Auth ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AuthBrushVisibility => _mode == MfaMode.Auth ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SeparatorVisibility => _mode == MfaMode.Setup ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CloseBlackVisibility => _mode != MfaMode.Auth ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CloseWhiteVisibility => _mode == MfaMode.Auth ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MfaImageVisibility => _secretShown ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SecretCodeVisibility => _secretShown ? Visibility.Visible : Visibility.Collapsed;
        public string SecretButtonContent => _secretShown ? "Show QR Code" : "Show Secret";
        public Visibility RecoveryListVisibility => _hasRecoveryCodes ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoRecoveryVisibility => _hasRecoveryCodes ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SaveButtonVisibility => _hasRecoveryCodes ? Visibility.Visible : Visibility.Collapsed;

        public void ShowSetupMode() {
            _mode = MfaMode.Setup;
            _secretShown = false;
            RaiseAll();
        }

        public void ShowRecoveryMode(bool hasCodes) {
            _mode = MfaMode.Recovery;
            _hasRecoveryCodes = hasCodes;
            RaiseAll();
        }

        public void ShowAuthMode() {
            _mode = MfaMode.Auth;
            RaiseAll();
            FocusAuthRequested?.Invoke();
        }

        public void ToggleSecret() {
            _secretShown = !_secretShown;
            OnPropertyChanged(nameof(MfaImageVisibility));
            OnPropertyChanged(nameof(SecretCodeVisibility));
            OnPropertyChanged(nameof(SecretButtonContent));
        }

        private void RaiseAll() {
            OnPropertyChanged(nameof(SetupAreaVisibility));
            OnPropertyChanged(nameof(RecoveryAreaVisibility));
            OnPropertyChanged(nameof(AuthAreaVisibility));
            OnPropertyChanged(nameof(MainBrushVisibility));
            OnPropertyChanged(nameof(AuthBrushVisibility));
            OnPropertyChanged(nameof(SeparatorVisibility));
            OnPropertyChanged(nameof(CloseBlackVisibility));
            OnPropertyChanged(nameof(CloseWhiteVisibility));
            OnPropertyChanged(nameof(MfaImageVisibility));
            OnPropertyChanged(nameof(SecretCodeVisibility));
            OnPropertyChanged(nameof(SecretButtonContent));
            OnPropertyChanged(nameof(RecoveryListVisibility));
            OnPropertyChanged(nameof(NoRecoveryVisibility));
            OnPropertyChanged(nameof(SaveButtonVisibility));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
