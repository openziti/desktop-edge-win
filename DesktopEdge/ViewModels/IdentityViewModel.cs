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
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NLog;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    // One view model per identity, shared by the identity row and the identity details screen.
    public class IdentityViewModel : ViewModelBase {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private const int ServicesPerPage = 50;

        private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x68, 0xF9));
        private static readonly Brush MfaNeededBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0x8B, 0x10));
        private static readonly Brush DisabledBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xA9, 0xA9, 0xA9));

        private ZitiIdentity _identity;

        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.Timer _timingTimer;
        private float countdown = -1;
        private float countdownComplete = -1;
        private int available = 0;

        private ZitiService[] _sortedServices;
        private string _sortBy = "Name";
        private string _sortWay = "Asc";
        private string _filter = "";
        private bool _isLoaded;
        private int _totalServices;
        private string _serviceCountSummary = "0 Services";
        private Visibility _confirmForgetVisibility = Visibility.Collapsed;

        public event Action<ZitiIdentity> IdentityChanged;
        public event Action<ZitiIdentity> IdentityForgotten;
        public event Action<string> RemoveFailed;

        public ActionCommand ShowConfirmForgetCommand { get; }
        public ActionCommand CancelForgetCommand { get; }
        public ActionCommand ConfirmForgetCommand { get; }

        public IdentityViewModel() {
            ShowConfirmForgetCommand = new ActionCommand(() => ConfirmForgetVisibility = Visibility.Visible, () => true);
            CancelForgetCommand = new ActionCommand(() => ConfirmForgetVisibility = Visibility.Collapsed, () => true);
            ConfirmForgetCommand = new ActionCommand(ConfirmForget, () => true);
        }

        public IdentityViewModel(ZitiIdentity identity) : this() {
            Identity = identity;
        }

        public ZitiIdentity Identity {
            get { return _identity; }
            set {
                _identity = value;
                Recompute();
                OnPropertyChanged(nameof(Identity));
            }
        }

        // ---- Identity actions (send commands to ZET) ----

        public async Task SetEnabledAsync(bool on) {
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            await client.IdentityOnOffAsync(_identity.Identifier, on);
            _identity.IsEnabled = on;
            _identity.AuthInProgress = false;
            Refresh();
        }

        public async Task EnableMfaAsync() {
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            await client.EnableMFA(_identity.Identifier);
        }

        public async Task CompleteExternalAuthAsync(string provider) {
            await _identity.PerformExternalAuthEvent((DataClient)Application.Current.Properties["ServiceClient"], provider);
        }

        // Adds a service to the identity. Returns true when a failing MFA posture check means the
        // caller should raise an MFA notification (kept in the view so the OS toast/throttle stays there).
        public bool ApplyServiceAdded(Service added) {
            ZitiService zs = new ZitiService(added);
            ZitiService existing = _identity.Services.Find(s => s.Name == zs.Name);
            if (existing != null) {
                logger.Debug("the service named " + zs.Name + " is already accounted for on this identity.");
                return false;
            }
            logger.Debug("Service Added: " + zs.Name);
            _identity.Services.Add(zs);
            if (!zs.HasFailingPostureCheck()) {
                return false;
            }
            _identity.HasServiceFailingPostureCheck = true;
            bool needsMfaNotification = false;
            foreach (ZitiService service in _identity.Services) {
                if (service != null && service.PostureChecks != null && service.PostureChecks.Any(p => !p.IsPassing && p.QueryType == "MFA")) {
                    _identity.IsMFANeeded = true;
                    needsMfaNotification = true;
                }
            }
            return needsMfaNotification;
        }

        public void ApplyServiceRemoved(Service removed) {
            logger.Debug("removing the service named: {0}", removed.Name);
            _identity.Services.RemoveAll(s => s.Name == removed.Name);
        }

        public void ApplyIdentityUpdate(Identity id) {
            ZitiIdentity zid = ZitiIdentity.FromClient(id);
            if (!string.IsNullOrEmpty(zid.Name)) _identity.Name = zid.Name;
            if (!string.IsNullOrEmpty(zid.ControllerUrl)) _identity.ControllerUrl = zid.ControllerUrl;
            if (!string.IsNullOrEmpty(zid.ContollerVersion)) _identity.ContollerVersion = zid.ContollerVersion;
            _identity.IsEnabled = zid.IsEnabled;
            _identity.IsMFAEnabled = id.MfaEnabled;
            _identity.IsConnected = true;
            _identity.NeedsExtAuth = id.NeedsExtAuth;
            _identity.ExtAuthProviders = id.ExtAuthProviders;
        }

        public void ApplyConnected(Identity id) {
            _identity.IsConnected = true;
            _identity.IsMFANeeded = id.MfaNeeded;
            _identity.NeedsExtAuth = id.NeedsExtAuth;
        }

        public void ApplyDisconnected() {
            _identity.IsConnected = false;
        }

        public void ApplyMfaVerified() {
            _identity.WasNotified = false;
            _identity.WasFullNotified = false;
            _identity.IsMFANeeded = false;
            _identity.IsMFAEnabled = true;
            _identity.IsTimingOut = false;
            _identity.LastUpdatedTime = DateTime.Now;
            for (int j = 0; j < _identity.Services.Count; j++) {
                _identity.Services[j].TimeUpdated = DateTime.Now;
                _identity.Services[j].TimeoutRemaining = _identity.Services[j].Timeout;
            }
        }

        public void ApplyMfaRemoved() {
            _identity.WasNotified = false;
            _identity.WasFullNotified = false;
            _identity.IsMFAEnabled = false;
            _identity.IsMFANeeded = false;
            _identity.LastUpdatedTime = DateTime.Now;
            _identity.IsTimingOut = false;
            for (int j = 0; j < _identity.Services.Count; j++) {
                _identity.Services[j].TimeUpdated = DateTime.Now;
                _identity.Services[j].TimeoutRemaining = -1;
            }
        }

        public void ApplyMfaChallenge() {
            _identity.WasNotified = false;
            _identity.WasFullNotified = false;
            _identity.IsMFANeeded = true;
            _identity.IsTimingOut = false;
        }

        public void ApplyMfaAuthStatus(bool successful) {
            _identity.WasNotified = false;
            _identity.WasFullNotified = false;
            _identity.IsTimingOut = false;
            _identity.IsMFANeeded = !successful;
            _identity.LastUpdatedTime = DateTime.Now;
            for (int j = 0; j < _identity.Services.Count; j++) {
                _identity.Services[j].TimeUpdated = DateTime.Now;
                _identity.Services[j].TimeoutRemaining = _identity.Services[j].Timeout;
            }
        }

        // ---- Row display state (bound by IdentityItem) ----

        public string Name => _identity.Name;

        public string ControllerLabel =>
            string.IsNullOrEmpty(_identity.ContollerVersion)
                ? _identity.ControllerUrl
                : _identity.ControllerUrl + " at " + _identity.ContollerVersion;

        public bool IsEnabled => _identity.IsEnabled;
        public bool IsMFAEnabled => _identity.IsMFAEnabled;
        public string ControllerUrl => _identity.ControllerUrl;

        private bool _showProviderConfig;

        public bool ShowProviderConfig {
            get { return _showProviderConfig; }
            set {
                _showProviderConfig = value;
                OnPropertyChanged(nameof(ShowProviderConfig));
                OnPropertyChanged(nameof(ServicesPanelVisibility));
                OnPropertyChanged(nameof(ExternalProviderPanelVisibility));
            }
        }

        private bool ServicesMode => !_identity.NeedsExtAuth && !_identity.IsMFANeeded && _identity.IsEnabled;

        public Visibility ServicesPanelVisibility =>
            ServicesMode && !_showProviderConfig ? Visibility.Visible : Visibility.Collapsed;

        public Visibility TotpPanelVisibility =>
            !_identity.NeedsExtAuth && _identity.IsMFANeeded ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ExternalProviderPanelVisibility =>
            _identity.NeedsExtAuth || (ServicesMode && _showProviderConfig) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ExternalProviderLabelVisibility =>
            _identity.NeedsExtAuth ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ProviderGearVisibility =>
            ServicesMode && _identity.ExtAuthProviders != null && _identity.ExtAuthProviders.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MfaAuthOffVisibility =>
            _identity.IsMFANeeded && _identity.IsMFAEnabled && _identity.IsEnabled
            && (_identity.Services == null || _identity.Services.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;

        public string NoAuthServicesText =>
            _identity.IsMFAEnabled ? "You must authenticate to access services" : "You must enable MFA to access services";
        public string ToggleStatusText => _identity.IsEnabled ? "ENABLED" : "DISABLED";
        public string ServiceCountText => _identity.IsEnabled ? _identity.Services.Count.ToString() : "-";

        public string ServiceCountLabel { get; private set; }
        public Brush ServiceCountBrush { get; private set; }
        public double MainOpacity { get; private set; }
        public string TimeoutToolTip { get; private set; }
        public Visibility ServiceCountVisibility { get; private set; }
        public Visibility MfaRequiredVisibility { get; private set; }
        public Visibility PostureTimedOutVisibility { get; private set; }
        public Visibility ExtAuthRequiredVisibility { get; private set; }

        public void Refresh() {
            Recompute();
            OnPropertyChanged(string.Empty);
        }

        public void StopTimers() {
            _timer?.Stop();
            _timingTimer?.Stop();
        }

        private void Recompute() {
            StopTimers();
            MainOpacity = 1.0;
            ServiceCountBrush = DefaultBrush;
            ServiceCountVisibility = Visibility.Collapsed;
            MfaRequiredVisibility = Visibility.Collapsed;
            PostureTimedOutVisibility = Visibility.Collapsed;
            ExtAuthRequiredVisibility = Visibility.Collapsed;

            if (_identity.IsEnabled) {
                if (_identity.IsMFAEnabled && _identity.IsMFANeeded) {
                    ServiceCountLabel = "authorize";
                    ShowMfa();
                    StartTimeoutTimers();
                } else if (!_identity.IsMFAEnabled && _identity.IsMFANeeded) {
                    ServiceCountLabel = "enable mfa";
                    ShowBubbles();
                    ServiceCountBrush = MfaNeededBrush;
                } else {
                    ServiceCountLabel = "services";
                    ShowBubbles();
                }
                if (_identity.NeedsExtAuth) {
                    ServiceCountLabel = "authorize IdP";
                    MainOpacity = 0.6;
                    HideMfa();
                    ServiceCountVisibility = Visibility.Collapsed;
                    ExtAuthRequiredVisibility = Visibility.Visible;
                }
            } else {
                ServiceCountLabel = "id disabled";
                ServiceCountBrush = DisabledBrush;
                MainOpacity = 0.6;
                ShowBubbles();
            }
        }

        private void ShowMfa() {
            if (_identity.IsTimedOut) {
                PostureTimedOutVisibility = Visibility.Visible;
            } else {
                MfaRequiredVisibility = Visibility.Visible;
            }
            ServiceCountVisibility = Visibility.Collapsed;
            MainOpacity = 0.6;
        }

        private void HideMfa() {
            PostureTimedOutVisibility = Visibility.Collapsed;
            MfaRequiredVisibility = Visibility.Collapsed;
        }

        private void ShowBubbles() {
            HideMfa();
            ServiceCountVisibility = Visibility.Visible;
        }

        private int GetMaxTimeout() {
            int maxto = -1;
            for (int i = 0; i < _identity.Services.Count; i++) {
                ZitiService info = _identity.Services[i];
                if (info.TimeoutCalculated > -1) {
                    if (info.TimeoutCalculated == 0) {
                        available--;
                    }
                    if (info.TimeoutCalculated > maxto) maxto = info.TimeoutCalculated;
                }
            }
            return maxto;
        }

        private int GetMinTimeout() {
            int minto = int.MaxValue;
            for (int i = 0; i < _identity.Services.Count; i++) {
                ZitiService info = _identity.Services[i];
                if (info.TimeoutCalculated > -1) {
                    if (info.TimeoutCalculated < minto) minto = info.TimeoutCalculated;
                }
            }
            if (minto == int.MaxValue) minto = 0;
            return minto;
        }

        private void StartTimeoutTimers() {
            available = _identity.Services.Count;
            int maxto = GetMaxTimeout();
            if (maxto > -1 && maxto > 0) {
                if (_timer != null) _timer.Stop();
                countdownComplete = maxto;
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 1000;
                _timer.Tick += TimerTicked;
                _timer.Start();
                logger.Info("Timer Started for full timout in " + maxto + "  seconds from identity " + _identity.Name + ".");
            }
            int minto = GetMinTimeout();
            if (minto > -1) {
                if (minto > 0) {
                    if (_timingTimer != null) _timingTimer.Stop();
                    countdown = minto;
                    _timingTimer = new System.Windows.Forms.Timer();
                    _timingTimer.Interval = 1000;
                    _timingTimer.Tick += TimingTimerTick;
                    _timingTimer.Start();
                    logger.Info("Timer Started for first timout in " + minto + " seconds from identity " + _identity.Name + " value with " + _identity.MinTimeout + ".");
                } else {
                    if (maxto > 0) {
                        ShowTimeout();
                    }
                }
            }
        }

        private void TimingTimerTick(object sender, EventArgs e) {
            available = _identity.Services.Count;
            GetMaxTimeout();
            if (countdown > -1) {
                countdown--;
                if (countdown > 0) {
                    TimeSpan t = TimeSpan.FromSeconds(countdown);
                    string answer = t.Seconds + " seconds";
                    if (t.Days > 0) answer = t.Days + " days " + t.Hours + " hours " + t.Minutes + " minutes " + t.Seconds + " seconds";
                    else {
                        if (t.Hours > 0) answer = t.Hours + " hours " + t.Minutes + " minutes " + t.Seconds + " seconds";
                        else {
                            if (t.Minutes > 0) answer = t.Minutes + " minutes " + t.Seconds + " seconds";
                        }
                    }
                    if (countdown <= 1200) {
                        ShowTimeout();
                        if (!_identity.WasNotified) {
                            _identity.WasNotified = true;
                            _identity.ShowMFAToast("The services for " + _identity.Name + " will start to time out in " + answer);
                        }
                    }
                    if (available < _identity.Services.Count) SetTimeoutToolTip((_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.");
                    else SetTimeoutToolTip("Some or all of the services will be timing out in " + answer);
                } else {
                    ShowTimeout();
                    SetTimeoutToolTip((_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.");
                    SetServiceCountLabel(available + "/" + _identity.Services.Count);
                }
            } else {
                ShowTimeout();
                SetTimeoutToolTip("Some or all of the services have timed out.");
                SetServiceCountLabel(available + "/" + _identity.Services.Count);
            }
        }

        private void ShowTimeout() {
            SetServiceCountLabel(available + "/" + _identity.Services.Count);
            if (!_identity.WasNotified) {
                if (available < _identity.Services.Count) {
                    _identity.WasNotified = true;
                    _identity.ShowMFAToast((_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.");
                }
                _identity.IsTimingOut = true;
                IdentityChanged?.Invoke(_identity);
            }
        }

        private void ShowTimedOut() {
            _identity.Mutex.Wait();
            if (!_identity.WasFullNotified) {
                _identity.WasFullNotified = true;
                _identity.ShowMFAToast("All of the services with a timeout set for the identity " + _identity.Name + " have timed out");
                Refresh();
                if (_timer != null) _timer.Stop();
            }
            _identity.Mutex.Release();
        }

        private void TimerTicked(object sender, EventArgs e) {
            if (countdownComplete > -1) {
                countdownComplete--;
                if (countdownComplete <= 0) ShowTimedOut();
            }
        }

        private void SetServiceCountLabel(string value) {
            ServiceCountLabel = value;
            OnPropertyChanged(nameof(ServiceCountLabel));
        }

        private void SetTimeoutToolTip(string value) {
            TimeoutToolTip = value;
            OnPropertyChanged(nameof(TimeoutToolTip));
        }

        // ---- Details screen state (bound by IdentityDetails) ----

        public ObservableCollection<ZitiService> Services { get; } = new ObservableCollection<ZitiService>();

        public Visibility ConfirmForgetVisibility {
            get { return _confirmForgetVisibility; }
            set {
                _confirmForgetVisibility = value;
                OnPropertyChanged(nameof(ConfirmForgetVisibility));
            }
        }

        public string ServiceCountSummary {
            get { return _serviceCountSummary; }
            private set {
                _serviceCountSummary = value;
                OnPropertyChanged(nameof(ServiceCountSummary));
            }
        }

        public int TotalServices {
            get { return _totalServices; }
            private set {
                _totalServices = value;
                OnPropertyChanged(nameof(TotalServices));
            }
        }

        public bool IsLoaded {
            get { return _isLoaded; }
            set {
                _isLoaded = value;
                OnPropertyChanged(nameof(IsLoaded));
            }
        }

        public bool HasSortedServices {
            get { return _sortedServices != null; }
        }

        private async void ConfirmForget() {
            ConfirmForgetVisibility = Visibility.Collapsed;
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            try {
                await client.RemoveIdentityAsync(_identity.Identifier);
                IdentityForgotten?.Invoke(_identity);
            } catch (ServiceException se) {
                logger.Error(se, se.Message);
                RemoveFailed?.Invoke(se.Message);
            } catch (Exception ex) {
                logger.Error(ex, "Unexpected: " + ex.Message);
                RemoveFailed?.Invoke("An unexpected error has occured while removing the identity. Please verify the service is still running and try again.");
            }
        }

        /// <summary>
        /// Resets sort/filter state and loads the first page of services for the current identity.
        /// </summary>
        public void LoadServices() {
            _sortBy = "Name";
            _sortWay = "Asc";
            _filter = "";
            IsLoaded = false;
            TotalServices = _identity.Services.Count;
            ServiceCountSummary = TotalServices + " service" + (TotalServices != 1 ? "s" : "");
            RebuildServiceList();
            IsLoaded = true;
        }

        /// <summary>
        /// Applies a filter/sort change and reloads the first page.
        /// </summary>
        public void ApplyFilter(string filter, string sortBy, string sortWay) {
            _filter = filter;
            _sortBy = sortBy;
            _sortWay = sortWay;
            IsLoaded = false;
            RebuildServiceList();
            IsLoaded = true;
        }

        /// <summary>
        /// Appends the next page of filtered services. Returns the number of items added.
        /// </summary>
        public int LoadNextPage() {
            if (_sortedServices == null) {
                return 0;
            }

            IsLoaded = false;
            string lowerFilter = _filter.ToLower();
            int added = 0;
            int skip = Services.Count;
            int seen = 0;

            foreach (ZitiService zitiSvc in _sortedServices) {
                if (MatchesFilter(zitiSvc, lowerFilter)) {
                    if (seen >= skip) {
                        if (added >= ServicesPerPage) {
                            break;
                        }
                        zitiSvc.TimeUpdated = _identity.LastUpdatedTime;
                        zitiSvc.IsMfaReady = _identity.IsMFAEnabled;
                        Services.Add(zitiSvc);
                        added++;
                    }
                    seen++;
                }
            }

            IsLoaded = true;
            return added;
        }

        public void Clear() {
            Services.Clear();
            _sortedServices = null;
        }

        private void RebuildServiceList() {
            Services.Clear();
            SortServices();

            string lowerFilter = _filter.ToLower();
            int count = 0;
            foreach (ZitiService zitiSvc in _sortedServices) {
                if (count >= ServicesPerPage) {
                    break;
                }
                if (MatchesFilter(zitiSvc, lowerFilter)) {
                    zitiSvc.TimeUpdated = _identity.LastUpdatedTime;
                    zitiSvc.IsMfaReady = _identity.IsMFAEnabled;
                    Services.Add(zitiSvc);
                    count++;
                }
            }
        }

        private void SortServices() {
            switch (_sortBy) {
                case "Name":
                    _sortedServices = _identity.Services.OrderBy(s => s.Name.ToLower()).ToArray();
                    break;
                case "Address":
                    _sortedServices = _identity.Services.OrderBy(s => s.Addresses.ToString()).ToArray();
                    break;
                case "Protocol":
                    _sortedServices = _identity.Services.OrderBy(s => s.Protocols.ToString()).ToArray();
                    break;
                case "Port":
                    _sortedServices = _identity.Services.OrderBy(s => s.Ports.ToString()).ToArray();
                    break;
                default:
                    _sortedServices = _identity.Services.ToArray();
                    break;
            }
            if (_sortWay == "Desc") {
                _sortedServices = _sortedServices.Reverse().ToArray();
            }
        }

        private static bool MatchesFilter(ZitiService service, string lowerFilter) {
            if (string.IsNullOrEmpty(lowerFilter)) {
                return true;
            }
            return service.Name.ToLower().Contains(lowerFilter) || service.ToString().ToLower().Contains(lowerFilter);
        }
    }
}
