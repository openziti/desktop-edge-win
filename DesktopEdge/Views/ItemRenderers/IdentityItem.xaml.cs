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
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;
using Windows.UI.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;
using NLog;

namespace ZitiDesktopEdge {
	/// <summary>
	/// User Control to list Identities and give status
	/// </summary>
	public partial class IdentityItem:UserControl {

		private static readonly Logger logger = LogManager.GetCurrentClassLogger();
		public delegate void StatusChanged(bool attached);
		public event StatusChanged OnStatusChanged;
		public delegate void OnAuthenticate(ZitiIdentity identity);
		public event OnAuthenticate Authenticate;
		public delegate void OnIdentityChanged(ZitiIdentity identity);
		public event OnIdentityChanged IdentityChanged;
		private System.Windows.Forms.Timer _timer;
		private System.Windows.Forms.Timer _timingTimer;
		private float countdown = -1;
		private float countdownComplete = -1;
		private int available = 0;

		public ZitiIdentity _identity;
		public ZitiIdentity Identity {
			get {
				return _identity;
			}
			set {
				_identity = value;
				this.RefreshUI();
			}
		}

		/// <summary>
		/// Object constructor, setup the events for the control
		/// </summary>
		public IdentityItem() {
			InitializeComponent();
			ToggleSwitch.OnToggled += ToggleIdentity;
		}

		public void StopTimers() {
			_timer?.Stop();
			_timingTimer?.Stop();
		}

		public int GetMaxTimeout() {
			int maxto = -1;
			for (int i=0; i<_identity.Services.Count; i++) {
				ZitiService info = _identity.Services[i];

				if (info.TimeoutCalculated > -1) {
					if (info.TimeoutCalculated==0) {
						available--;
					}
					if (info.TimeoutCalculated > maxto) maxto = info.TimeoutCalculated;

				}
				logger.Trace("Max: " + _identity.Name + " "+maxto+" " + info.Name + " " + info.Timeout + " " + info.TimeoutCalculated + " " + info.TimeoutRemaining + " " + info.TimeUpdated+" "+ DateTime.Now);
			}
			return maxto;
		}
		public int GetMinTimeout() {
			int minto = int.MaxValue;
			for (int i = 0; i < _identity.Services.Count; i++) {
				ZitiService info = _identity.Services[i];
				if (info.TimeoutCalculated > -1) {
					if (info.TimeoutCalculated < minto) minto = info.TimeoutCalculated;
				}
				// logger.Trace("Min: " + _identity.Name + " " + minto + " " + info.Name + " " + info.Timeout + " " + info.TimeoutCalculated + " " + info.TimeoutRemaining + " " + info.TimeUpdated+" "+ DateTime.Now);
			}
			if (minto == int.MaxValue) minto = 0;
			return minto;
		}

		public void RefreshUI () {
			if (_identity.IsConnected) {
				this.IsEnabled = true;
				this.Opacity = 1.0;
			} else {
				this.IsEnabled = false;
				this.Opacity = 0.3;
			}
			TimerCountdown.Visibility = Visibility.Collapsed;
			ServiceCountArea.Visibility = Visibility.Collapsed;
			PostureTimedOut.Visibility = Visibility.Collapsed;
			MfaRequired.Visibility = Visibility.Collapsed;
			available = _identity.Services.Count;
			ToggleSwitch.Enabled = _identity.IsEnabled;
			ServiceCountAreaLabel.Content = "services";
			// logger.Info("RefreshUI " + _identity.Name + " MFA: "+ _identity.IsMFAEnabled+" Authenticated: "+_identity.IsAuthenticated);
			if (_identity.IsMFAEnabled) {
				if (_identity.IsAuthenticated) {
					ServiceCountArea.Visibility = Visibility.Visible;
					ServiceCountAreaLabel.Content = "services";
					MainArea.Opacity = 1.0;
					float maxto = GetMaxTimeout();
					if (maxto>-1) {
						if (maxto > 0) {
							if (_timer != null) _timer.Stop();
							countdownComplete = maxto;
							_timer = new System.Windows.Forms.Timer();
							_timer.Interval = 1000;
							_timer.Tick += TimerTicked;
							_timer.Start();
							logger.Info("Timer Started for full timout in "+maxto+"  seconds from identity "+_identity.Name+".");
						} else {
							_identity.IsAuthenticated = false;
							MfaRequired.Visibility = Visibility.Visible;
							ServiceCountAreaLabel.Content = "authorize";
							MainArea.Opacity = 0.6;
							if (maxto == 0) ShowTimedOut();
						}
					}
					float minto = GetMinTimeout();
					logger.Info("Min/Max For " + _identity.Name + " " + minto + " "+maxto);
					if (minto>-1) {
						if (minto>0) {
							if (_timingTimer != null) _timingTimer.Stop();
							countdown = minto;
							_timingTimer = new System.Windows.Forms.Timer();
							_timingTimer.Interval = 1000;
							_timingTimer.Tick += TimingTimerTick;
							_timingTimer.Start();
							logger.Info("Timer Started for first timout in " + minto + " seconds from identity "+_identity.Name+" value with " + _identity.MinTimeout + ".");
						} else {
							if (maxto>0) {
								ShowTimeout();
							}
						}
					}
					logger.Info("RefreshUI " + _identity.Name + " Min: " + minto + " Max: " + maxto);
				} else {
					if (_identity.IsTimedOut) {
						PostureTimedOut.Visibility = Visibility.Visible;
						ServiceCountAreaLabel.Content = "authorize";
						MainArea.Opacity = 1.0;
					} else {
						MfaRequired.Visibility = Visibility.Visible;
						ServiceCountAreaLabel.Content = "authenticate";
						MainArea.Opacity = 0.6;
					}
				}
			} else {
				ServiceCountArea.Visibility = Visibility.Visible;
				ServiceCountAreaLabel.Content = "services";
				MainArea.Opacity = 1.0;
			}
			IdName.Content = _identity.Name;
			IdUrl.Content = _identity.ControllerUrl;
			if (_identity.ContollerVersion != null && _identity.ContollerVersion.Length > 0) IdUrl.Content = _identity.ControllerUrl + " at " + _identity.ContollerVersion;

			if (_identity.IsMFAEnabled && !_identity.IsAuthenticated) {
				ServiceCount.Content = "MFA";
			} else {
				ServiceCount.Content = _identity.Services.Count.ToString();
			}

			ToggleStatus.Content = ((ToggleSwitch.Enabled) ? "ENABLED" : "DISABLED");
		}

		private void TimingTimerTick(object sender, EventArgs e) {
			available = _identity.Services.Count;
			GetMaxTimeout();
			TimerCountdown.Visibility = Visibility.Collapsed;
			if (countdown>-1) {
				countdown--;
				logger.Trace("CountDown " + countdown + " seconds from identity " + _identity.Name + ".");
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
					if (countdown<=1200) {
						ShowTimeout();

						if (!_identity.WasNotified) {
							_identity.WasNotified = true;
							ShowMFAToast("The services for " + _identity.Name + " will start to time out in "+ answer, _identity);
						}
					}
					
					if (available<_identity.Services.Count) MainArea.ToolTip = (_identity.Services.Count-available) + " of " + _identity.Services.Count + " services have timed out.";
					else MainArea.ToolTip = "Some or all of the services will be timing out in " + answer;
				} else {
					ShowTimeout();
					MainArea.ToolTip = (_identity.Services.Count - available) + " of " + _identity.Services.Count+" services have timed out.";
					ServiceCountAreaLabel.Content = available + "/" + _identity.Services.Count;
				}
			} else {
				ShowTimeout();
				MainArea.ToolTip = "Some or all of the services have timed out.";
				ServiceCountAreaLabel.Content = available + "/" + _identity.Services.Count;
			}
		}

		private void ShowTimeout() {
			TimerCountdown.Visibility = Visibility.Visible;
			ServiceCountArea.Visibility = Visibility.Collapsed;
			MfaRequired.Visibility = Visibility.Collapsed;
			ServiceCountAreaLabel.Content = available + "/" + _identity.Services.Count;
			if (!_identity.WasNotified) {
				if (available < _identity.Services.Count) { 
					_identity.WasNotified = true;
					ShowMFAToast((_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.", _identity);
				}
				_identity.IsTimingOut = true;

				this.IdentityChanged?.Invoke(_identity);
			}
		}

		private void ShowTimedOut() {
			if (!_identity.WasFullNotified) {
				_identity.WasFullNotified = true;
				_identity.IsAuthenticated = false;
				_identity.IsTimedOut = true;
				ShowMFAToast("All of the services with a timeout set for the identity " + _identity.Name + " have timed out", _identity);
				RefreshUI();
				if (_timer != null) _timer.Stop();
			}
		}

		private void TimerTicked(object sender, EventArgs e) {
			if (countdownComplete > -1) {
				countdownComplete--;
				if (countdownComplete <= 0) ShowTimedOut();
			}
		}

		private void ShowMFAToast(string message, ZitiIdentity identity) {
			logger.Info("Showing Notification from identity " + _identity.Name + " " + message + ".");
			new ToastContentBuilder()
				.AddText(_identity.Name+" Service Access Warning")
				.AddText(message)
				.AddArgument("identifier", identity.Identifier)
				.SetBackgroundActivation()
				.Show();
		}
		async private void ToggleIdentity(bool on) {
			try {
				if (OnStatusChanged != null) {
					OnStatusChanged(on);
				}
				DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
				DataStructures.Identity id = await client.IdentityOnOffAsync(_identity.Identifier, on);
				this.Identity.IsEnabled = on;
				if (on) {
					ToggleStatus.Content = "ENABLED";
				} else {
					ToggleStatus.Content = "DISABLED";
				}
			} catch (DataStructures.ServiceException se) {
				MessageBox.Show(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				MessageBox.Show("Error", ex.Message);
			}
		}

		private void Canvas_MouseEnter(object sender, MouseEventArgs e) {
			OverState.Opacity = 0.2;
		}

		private void Canvas_MouseLeave(object sender, MouseEventArgs e) {
			OverState.Opacity = 0;
		}

		private void OpenDetails(object sender, MouseButtonEventArgs e) {
			IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
			deets.SelectedIdentity = this;
			deets.Identity = this.Identity;
		}

		private void MFAAuthenticate(object sender, MouseButtonEventArgs e) {
			this.Authenticate?.Invoke(_identity);
		}

		private void ToggledSwitch(object sender, MouseButtonEventArgs e) {
			ToggleSwitch.Toggle();
		}

		private void DoMFAOrOpen(object sender, MouseButtonEventArgs e) {
			if (MfaRequired.Visibility==Visibility.Visible || TimerCountdown.Visibility == Visibility.Visible || PostureTimedOut.Visibility == Visibility.Visible) {
				MFAAuthenticate(sender, e);
			} else {
				OpenDetails(sender, e);
			}
		}
	}
}
