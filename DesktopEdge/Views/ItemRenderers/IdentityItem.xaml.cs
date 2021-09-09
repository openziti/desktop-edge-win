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
		private System.Windows.Forms.Timer _timer;
		private System.Windows.Forms.Timer _timingTimer;
		private float countdown = -1;
		private float countdownComplete = -1;

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

		public float GetMaxTimeout() {
			float maxto = -1;
			for (int i=0; i<_identity.Services.Count; i++) {
				ZitiService info = _identity.Services[i];
				if (info.TimeoutRemaining>-1) {
					TimeSpan t = (DateTime.Now - info.TimeUpdated);
					float timeout = info.TimeoutRemaining - (float)Math.Floor(t.TotalSeconds);
					logger.Info("Max: Service "+info.Name+" Updated " + (float)Math.Floor(t.TotalSeconds) + " seconds ago will timeout in " + timeout + " seconds");
					if (timeout>-1 && timeout>maxto) maxto = timeout;
				}
			}
			return maxto;
		}
		public float GetMinTimeout(int minTimout) {
			float minto = minTimout;
			for (int i = 0; i < _identity.Services.Count; i++) {
				ZitiService info = _identity.Services[i];
				if (info.TimeoutRemaining > -1) {
					TimeSpan t = (DateTime.Now - info.TimeUpdated);
					float timeout = info.TimeoutRemaining - (float)Math.Floor(t.TotalSeconds);
					logger.Info("Min: Service " + info.Name + " Updated " + (float)Math.Floor(t.TotalSeconds) + " seconds ago will timeout in " + timeout + " seconds");
					if (timeout > -1 && timeout<minto) minto = timeout;
				}
			}
			return minto;
		}

		public void RefreshUI () {
			ToggleSwitch.Enabled = _identity.IsEnabled;
			if (_identity.IsMFAEnabled) {
				if (_identity.MFAInfo.IsAuthenticated) {
					ServiceCountArea.Visibility = Visibility.Visible;
					MfaRequired.Visibility = Visibility.Collapsed;
					ServiceCountAreaLabel.Content = "services";
					MainArea.Opacity = 1.0;
					//if (_identity.MaxTimeout>0) {
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
							_identity.MFAInfo.IsAuthenticated = false;
							ServiceCountArea.Visibility = Visibility.Collapsed;
							MfaRequired.Visibility = Visibility.Visible;
							ServiceCountAreaLabel.Content = "authorize";
							MainArea.Opacity = 0.6;
							if (maxto == 0) ShowTimedOut();
						}
					}
					//}
					float minto = GetMinTimeout(_identity.MinTimeout);
					if (minto>-1) {
						//if (_identity.MinTimeout > 0) {
						if (minto>0) {
							if (_timingTimer != null) _timingTimer.Stop();
							countdown = minto;
							_timingTimer = new System.Windows.Forms.Timer();
							_timingTimer.Interval = 1000;
							_timingTimer.Tick += TimingTimerTick;
							_timingTimer.Start();
							logger.Info("Timer Started for first timout in " + minto + " seconds from identity "+_identity.Name+" value with " + _identity.MinTimeout + ".");
						}
					}
				} else {
					ServiceCountArea.Visibility = Visibility.Collapsed;
					MfaRequired.Visibility = Visibility.Visible;
					ServiceCountAreaLabel.Content = "authorize";
					MainArea.Opacity = 0.6;
				}
			} else {
				ServiceCountArea.Visibility = Visibility.Visible;
				MfaRequired.Visibility = Visibility.Collapsed;
				ServiceCountAreaLabel.Content = "services";
				MainArea.Opacity = 1.0;
			}
			IdName.Content = _identity.Name;
			IdUrl.Content = _identity.ControllerUrl;
			if (_identity.IsMFAEnabled && !_identity.MFAInfo.IsAuthenticated) {
				ServiceCount.Content = "MFA";
			} else {
				ServiceCount.Content = _identity.Services.Count.ToString();
			}
			TimerCountdown.ToolTip = _identity.TimeoutMessage;
			if (TimerCountdown.ToolTip.ToString().Length == 0) TimerCountdown.ToolTip = "Some or all of the services have timed out.";
			TimerCountdown.Visibility = _identity.IsTimingOut ? Visibility.Visible : Visibility.Collapsed;
			if (ToggleSwitch.Enabled) {
				ToggleStatus.Content = "ENABLED";
			} else {
				ToggleStatus.Content = "DISABLED";
			}
		}

		private void TimingTimerTick(object sender, EventArgs e) {
			if (countdown>-1) {
				countdown--;
				logger.Info("CountDown " + countdown + " seconds from identity " + _identity.Name + ".");
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
						if (countdown<1200) {
							_identity.IsTimingOut = true;
							if (!_identity.WasNotified) {
								_identity.WasNotified = true;
								ShowMFAToast("The services for " + _identity.Name + " will start to time out in "+ answer, _identity);
							}
						}
						TimerCountdown.ToolTip = "Some or all of the services will be timing out in " + answer;
					} else {
						TimerCountdown.ToolTip = "Some or all of the services have timed out.";
					}
					TimerCountdown.Visibility = _identity.IsTimingOut ? Visibility.Visible : Visibility.Collapsed;
			} else {
				TimerCountdown.ToolTip = "Some or all of the services have timed out.";
			}
		}

		private void ShowTimedOut() {
			if (!_identity.WasFullNotified) {
				_identity.WasFullNotified = true;
				_identity.MFAInfo.IsAuthenticated = false;
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
			new ToastContentBuilder()
				.AddText("Service Access Timed Out")
				.AddText(message)
				.AddArgument("fingerprint", identity.Fingerprint)
				.SetBackgroundActivation()
				.Show();
		}

		public IdentityItem() {
			InitializeComponent();
			ToggleSwitch.OnToggled += ToggleIdentity;
		}

		async private void ToggleIdentity(bool on) {
			try {
				if (OnStatusChanged != null) {
					OnStatusChanged(on);
				}
				DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
				DataStructures.Identity id = await client.IdentityOnOffAsync(_identity.Fingerprint, on);
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
			if (MfaRequired.Visibility==Visibility.Visible) {
				MFAAuthenticate(sender, e);
			} else {
				OpenDetails(sender, e);
			}
		}
	}
}
