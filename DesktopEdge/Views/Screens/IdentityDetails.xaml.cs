using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;
using System.Windows.Media.Animation;

using NLog;
using System.Web;
using System.Windows.Media;
using System.Windows.Data;

namespace ZitiDesktopEdge {
	/// <summary>
	/// Interaction logic for IdentityDetails.xaml
	/// </summary> 
	public partial class IdentityDetails:UserControl {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private bool _isAttached = true;
		public delegate void Forgot(ZitiIdentity forgotten);
		public event Forgot OnForgot;
		public delegate void ErrorOccurred(string message);
		public event ErrorOccurred OnError;
		public delegate void MFAToggled(bool isOn);
		public event MFAToggled OnMFAToggled;
		public delegate void Detched(MouseButtonEventArgs e);
		public event Detched OnDetach;
		public delegate void Mesage(string message);
		public event Mesage OnMessage;
		public delegate void OnAuthenticate(ZitiIdentity identity);
		public event OnAuthenticate Authenticate;
		public delegate void OnRecovery(ZitiIdentity identity);
		public event OnRecovery Recovery;
		public delegate void LoadingEvent(bool isComplete);
		public event LoadingEvent OnLoading;
		public delegate void ShowMFAEvent(ZitiIdentity identity);
		public event ShowMFAEvent OnShowMFA;

		private System.Windows.Forms.Timer _timer;
		public double MainHeight = 500;
		public string filter = "";
		public int Page = 1;
		public int PerPage = 50;
		public int TotalPages = 1;
		public string SortBy = "Name";
		public string SortWay = "Asc";
		private bool _loaded = false;
		private double scrolledTo = 0;
		public int totalServices = 0;
		private ScrollViewer _scroller;
		private ZitiService _info;

		public ObservableCollection<ZitiService> _services = new ObservableCollection<ZitiService>();
		public ObservableCollection<ZitiService> ZitiServices { get { return _services; } }

		internal MainWindow MainWindow { get; set; }

		private List<ZitiIdentity> identities {
			get {
				return (List<ZitiIdentity>)Application.Current.Properties["Identities"];
			}
		}

		private ZitiIdentity _identity;

		public ZitiIdentity Identity {
			get {
				return _identity;
			}
			set {
				_loaded = false;
				FilterServices.Clear();
				scrolledTo = 0;
				_identity = value;
				ServiceCount.Content = _identity.Services.Count + " service"+((_identity.Services.Count>1)?"s":"");
				Page = 1;
				SortBy = "Name";
				SortWay = "Asc";
				filter = "";
				UpdateView();
				IdentityArea.Opacity = 1.0;
				IdentityArea.Visibility = Visibility.Visible;
				this.Visibility = Visibility.Visible;
			}
		}

		public IdentityItem SelectedIdentity { get; set; }
		public MenuIdentityItem SelectedIdentityMenu { get; set; }

		private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
			if (e.ChangedButton == MouseButton.Left) {
				_isAttached = false;
				OnDetach(e);
			}
		}


		public bool IsAttached {
			get {
				return _isAttached;
			}
			set {
				_isAttached = value;
				if (_isAttached) {
					Arrow.Visibility = Visibility.Visible;
					ConfirmArrow.Visibility = Visibility.Visible;
				} else {
					Arrow.Visibility = Visibility.Collapsed;
					ConfirmArrow.Visibility = Visibility.Collapsed;
				}
			}
		}

		public void UpdateView() {
			if (_scroller==null) {
				_scroller = GetScrollViewer(ServiceList) as ScrollViewer;
			}
			if (_scroller != null) {
				_scroller.InvalidateScrollInfo();
				_scroller.ScrollToVerticalOffset(0);
				_scroller.InvalidateScrollInfo();
			}
			scrolledTo = 0;
			IdDetailName.Text = _identity.Name;
			IdDetailName.ToolTip = _identity.Name;
			IdentityMFA.IsOn = _identity.IsMFAEnabled;
			IdentityMFA.ToggleField.IsEnabled = true;
			IdentityMFA.ToggleField.Opacity = 1;
			IsConnected.Visibility = Visibility.Collapsed;
			IsDisconnected.Visibility = Visibility.Collapsed;
			IsConnected.ToolTip = "Enabled - Click To Disable";
			IsDisconnected.ToolTip = "Disabled - Click to Enable";
			IdServer.Value = _identity.ControllerUrl;
			IdName.Value = _identity.Name;
			if (_identity.IsEnabled) {
				IsConnected.Visibility = Visibility.Visible;
			} else {
				IsDisconnected.Visibility = Visibility.Visible;
			}
			if (_identity.IsMFAEnabled) {
				if (_identity.IsAuthenticated) {
					IdentityMFA.ToggleField.IsEnabled = true;
					IdentityMFA.AuthOn.Visibility = Visibility.Visible;
					IdentityMFA.AuthOff.Visibility = Visibility.Collapsed;
					IdentityMFA.RecoveryButton.Visibility = Visibility.Visible;
				} else {
					IdentityMFA.ToggleField.Opacity = 0.2;
					IdentityMFA.ToggleField.IsEnabled = false;
					IdentityMFA.AuthOn.Visibility = Visibility.Collapsed;
					IdentityMFA.AuthOff.Visibility = Visibility.Visible;
					IdentityMFA.RecoveryButton.Visibility = Visibility.Collapsed;
				}
			} else {
				IdentityMFA.AuthOn.Visibility = Visibility.Collapsed;
				IdentityMFA.AuthOff.Visibility = Visibility.Collapsed;
				IdentityMFA.RecoveryButton.Visibility = Visibility.Collapsed;
			}
			//ServiceList.Children.Clear();

			totalServices = _identity.Services.Count;

			if (_identity.Services.Count>0) {
				int index = 0;
				int total = 0;
				ZitiService[] services = new ZitiService[0];
				_services = null;
				_services = new ObservableCollection<ZitiService>(); 
				if (SortBy == "Name") services = _identity.Services.OrderBy(s => s.Name.ToLower()).ToArray();
				else if (SortBy == "Address") services = _identity.Services.OrderBy(s => s.Addresses.ToString()).ToArray();
				else if (SortBy == "Protocol") services = _identity.Services.OrderBy(s => s.Protocols.ToString()).ToArray();
				else if (SortBy == "Port") services = _identity.Services.OrderBy(s => s.Ports.ToString()).ToArray();
				if (SortWay == "Desc") services = services.Reverse().ToArray();
				int startIndex = (Page - 1) * PerPage;
				for (int i= startIndex; i<services.Length; i++) {
					ZitiService zitiSvc = services[i];
					total++; 
					if (index<PerPage) {
						if (zitiSvc.Name.ToLower().IndexOf(filter.ToLower()) >= 0 || zitiSvc.ToString().ToLower().IndexOf(filter.ToLower()) >= 0) {
							zitiSvc.TimeUpdated = _identity.LastUpdatedTime;
							zitiSvc.IsMfaReady = _identity.IsMFAEnabled;
							_services.Add(zitiSvc);
							index++;
						}
					}
				}
				ServiceList.ItemsSource = ZitiServices;

				TotalPages = (total / PerPage) + 1;

				double newHeight = MainHeight - 330;
				MainDetailScroll.MaxHeight = newHeight;
				MainDetailScroll.Height = newHeight;
				MainDetailScroll.Visibility = Visibility.Visible;

				AuthMessageBg.Visibility = Visibility.Collapsed;
				AuthMessageLabel.Visibility = Visibility.Collapsed;
				NoAuthServices.Visibility = Visibility.Collapsed;
			} else {
				MainDetailScroll.Visibility = Visibility.Collapsed;
				if (this._identity.IsMFAEnabled&&!this._identity.IsAuthenticated) {
					AuthMessageBg.Visibility = Visibility.Visible;
					AuthMessageLabel.Visibility = Visibility.Visible;
					NoAuthServices.Visibility = Visibility.Visible;
				} else {
					AuthMessageBg.Visibility = Visibility.Collapsed;
					AuthMessageLabel.Visibility = Visibility.Collapsed;
					NoAuthServices.Visibility = Visibility.Collapsed;
				}
			}
			ConfirmView.Visibility = Visibility.Collapsed;
			_loaded = true;
		}

		private void ShowDetails(ZitiService info) {
			_info = info;
			DetailName.Text = info.Name;
			DetailProtocols.Text = info.ProtocolString;
			DetailAddress.Text = info.AddressString;
			DetailPorts.Text = info.PortString;
			DetailUrl.Text = info.ToString();

			UpdateClock(info);
			if (_identity.IsMFAEnabled) {
				if (_timer != null) _timer.Stop();
				_timer = new System.Windows.Forms.Timer();
				_timer.Interval = 1000;
				_timer.Tick += Ticked; ;
				_timer.Start();
			}

			DetailsArea.Visibility = Visibility.Visible;
			DetailsArea.Opacity = 0;
			DetailsArea.Margin = new Thickness(0, 0, 0, 0);
			DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(.3));
			animation.Completed += ShowCompleted;
			DetailsArea.BeginAnimation(Grid.OpacityProperty, animation);
			DetailsArea.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));

			ShowModal();
		}

		private void Ticked(object sender, EventArgs e) {
			UpdateClock(_info);
		}

		private void UpdateClock(ZitiService info) {
			try {
				if (_identity.IsMFAEnabled) {
					if (info.TimeoutCalculated > 0) {
						TimeSpan t = TimeSpan.FromSeconds(info.TimeoutCalculated);
						string answer = t.Seconds + " seconds";
						if (t.Days > 0) answer = t.Days + " days " + t.Hours + " hours " + t.Minutes + " minutes " + t.Seconds + " seconds";
						else {
							if (t.Hours > 0) answer = t.Hours + " hours " + t.Minutes + " minutes " + t.Seconds + " seconds";
							else {
								if (t.Minutes > 0) answer = t.Minutes + " minutes " + t.Seconds + " seconds";
							}
						}
						TimeoutDetails.Text = answer;
					} else {
						if (info.TimeoutCalculated == 0) TimeoutDetails.Text = "Timed Out";
						else TimeoutDetails.Text = "Never";
					}
				} else {
					TimeoutDetails.Text = "Never";
				}
			} catch (Exception e) {
				TimeoutDetails.Text = "Never";
				Console.WriteLine("Error: " + e.ToString());
			}
		}

		private void ShowCompleted(object sender, EventArgs e) {
			DoubleAnimation animation = new DoubleAnimation(DetailPanel.ActualHeight + 60, TimeSpan.FromSeconds(.3));
			DetailsArea.BeginAnimation(Grid.HeightProperty, animation);
			//DetailsArea.Height = DetailPanel.ActualHeight + 60;
		}

		private void CloseDetails(object sender, MouseButtonEventArgs e) {
			DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
			ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
			DoubleAnimation animation2 = new DoubleAnimation(DetailPanel.ActualHeight + 100, TimeSpan.FromSeconds(.3));
			animation.Completed += HideComplete;
			DetailsArea.BeginAnimation(Grid.HeightProperty, animation2);
			DetailsArea.BeginAnimation(Grid.OpacityProperty, animation);
			DetailsArea.BeginAnimation(Grid.MarginProperty, animateThick);
			HideModal();
		}

		private void HideComplete(object sender, EventArgs e) {
			DetailsArea.Visibility = Visibility.Collapsed;
			ModalBg.Visibility = Visibility.Collapsed;
		}

		private void Info_OnMessage(string message) {
			OnMessage?.Invoke(message);
		}

		public IdentityDetails() {
			InitializeComponent();
		}
		private void HideMenu(object sender, MouseButtonEventArgs e) {
			this.Visibility = Visibility.Collapsed;
		}

		public void SetHeight(double height) {
			MainDetailScroll.Height = height;
		}

		private void ForgetIdentity() {
			if (this.Visibility==Visibility.Visible&&ConfirmView.Visibility==Visibility.Collapsed) {
				ConfirmView.Visibility = Visibility.Visible;
			}
		}

		private void CancelConfirmButton_Click(object sender, RoutedEventArgs e) {
			ConfirmView.Visibility = Visibility.Collapsed;
		}

		async private void ConfirmButton_Click(object sender, RoutedEventArgs e) {
			this.Visibility = Visibility.Collapsed;
			DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
			try {
				ConfirmView.Visibility = Visibility.Collapsed;
				await client.RemoveIdentityAsync(_identity.Identifier);

				ZitiIdentity forgotten = new ZitiIdentity();
				foreach (var id in identities) {
					if (id.Identifier == _identity.Identifier) {
						forgotten = id;
						identities.Remove(id);
						break;
					}
				}

				OnForgot?.Invoke(forgotten);
			} catch (DataStructures.ServiceException se) {
				Logger.Error(se, se.Message);
				OnError(se.Message);
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected: "+ ex.Message);
				OnError("An unexpected error has occured while removing the identity. Please verify the service is still running and try again.");
			}
		}

		private void ToggleMFA(bool isOn) {
			this.OnMFAToggled?.Invoke(isOn);
		}

		/* Modal UI Background visibility */

		/// <summary>
		/// Show the modal, aniimating opacity
		/// </summary>
		private void ShowModal() {
			ModalBg.Visibility = Visibility.Visible;
			ModalBg.Opacity = 0;
			ModalBg.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(.8, TimeSpan.FromSeconds(.3)));
		}

		/// <summary>
		/// Hide the modal animating the opacity
		/// </summary>
		private void HideModal() {
			DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
			animation.Completed += ModalHideComplete;
			ModalBg.BeginAnimation(Grid.OpacityProperty, animation);
		}

		/// <summary>
		/// When the animation completes, set the visibility to avoid UI object conflicts
		/// </summary>
		/// <param name="sender">The animation</param>
		/// <param name="e">The event</param>
		private void ModalHideComplete(object sender, EventArgs e) {
			ModalBg.Visibility = Visibility.Collapsed;
		}

		private void MFARecovery() {
			this.Recovery?.Invoke(this.Identity);
		}

		private void MFAAuthenticate() {
			this.Authenticate.Invoke(this.Identity);
		}

		private void AuthFromMessage(object sender, MouseButtonEventArgs e) {
			this.Authenticate.Invoke(this.Identity);
		}

		public static DependencyObject GetScrollViewer(DependencyObject o) {
			if (o is ScrollViewer) { return o; }
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) {
				var child = VisualTreeHelper.GetChild(o, i);
				var result = GetScrollViewer(child);
				if (result == null) {
					continue;
				} else {
					return result;
				}
			}
			return null;
		}

		private void Scrolled(object sender, ScrollChangedEventArgs e) {
			if (_loaded) {
				if (_scroller == null) {
					_scroller = GetScrollViewer(ServiceList) as ScrollViewer;
				}
				var verticalOffset = _scroller.VerticalOffset;
				var maxVerticalOffset = _scroller.ScrollableHeight;
			
				if ((maxVerticalOffset < 0 || verticalOffset == maxVerticalOffset) && verticalOffset>0 && scrolledTo<verticalOffset) {
					if (Page < TotalPages) {
						scrolledTo = verticalOffset;
						// scrollViewer.ScrollChanged -= Scrolled;
						Logger.Trace("Paging: " + Page);
						_loaded = false;
						Page += 1;
						int index = 0;
						ZitiService[] services = new ZitiService[0];
						if (SortBy == "Name") services = _identity.Services.OrderBy(s => s.Name.ToLower()).ToArray();
						else if (SortBy == "Address") services = _identity.Services.OrderBy(s => s.Addresses.ToString()).ToArray();
						else if (SortBy == "Protocol") services = _identity.Services.OrderBy(s => s.Protocols.ToString()).ToArray();
						else if (SortBy == "Port") services = _identity.Services.OrderBy(s => s.Ports.ToString()).ToArray();
						if (SortWay == "Desc") services = services.Reverse().ToArray();
						int startIndex = (Page - 1) * PerPage;
						for (int i = startIndex; i < services.Length; i++) {
							ZitiService zitiSvc = services[i];
							if (index < PerPage) {
								if (zitiSvc.Name.ToLower().IndexOf(filter.ToLower()) >= 0 || zitiSvc.ToString().ToLower().IndexOf(filter.ToLower()) >= 0) {
									zitiSvc.TimeUpdated = _identity.LastUpdatedTime;
									ZitiServices.Add(zitiSvc);
									index++;
								}
							}
						}
						double totalOffset = _scroller.VerticalOffset;
						double toNegate = index * 33;
						double scrollTo = (totalOffset - toNegate);
						_scroller.InvalidateScrollInfo();
						_scroller.ScrollToVerticalOffset(verticalOffset);
						_scroller.InvalidateScrollInfo();
						_loaded = true;
						// scrollViewer.ScrollChanged += Scrolled;
					}
				}
			}
		}

		private void DoFilter(FilterData filterData) {
			filter = filterData.SearchFor;
			SortBy = filterData.SortBy;
			SortWay = filterData.SortHow;
			UpdateView();
		}

		async private void DoConnect(object sender, MouseButtonEventArgs e) {
			this.OnLoading?.Invoke(false);
			DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
			await client.IdentityOnOffAsync(_identity.Identifier, true);
			if (SelectedIdentity != null) SelectedIdentity.ToggleSwitch.Enabled = true;
			if (SelectedIdentityMenu != null) SelectedIdentityMenu.ToggleSwitch.Enabled = true;
			_identity.IsEnabled = true;
			IsConnected.Visibility = Visibility.Visible;
			IsDisconnected.Visibility = Visibility.Collapsed;
			this.OnLoading?.Invoke(true);
		}

		async private void DoDisconnect(object sender, MouseButtonEventArgs e) {
			this.OnLoading?.Invoke(false);
			DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
			await client.IdentityOnOffAsync(_identity.Identifier, false);
			if (SelectedIdentity != null) SelectedIdentity.ToggleSwitch.Enabled = false;
			if (SelectedIdentityMenu != null) SelectedIdentityMenu.ToggleSwitch.Enabled = false;
			_identity.IsEnabled = false;
			IsConnected.Visibility = Visibility.Collapsed;
			IsDisconnected.Visibility = Visibility.Visible;
			this.OnLoading?.Invoke(true);
		}

		private void WarnClicked(object sender, MouseButtonEventArgs e) {
			ZitiService item = (ZitiService)(sender as FrameworkElement).DataContext;
			OnMessage?.Invoke(item.WarningMessage);
		}

		private void DetailsClicked(object sender, MouseButtonEventArgs e) {
			ZitiService item = (ZitiService)(sender as FrameworkElement).DataContext;
			ShowDetails(item);
		}

		private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) {
			if (this.Visibility==Visibility.Collapsed) {
				ServiceList.DataContext = null;
				if (_services!=null) {
					_services.Clear();
					_services = null;
				}
			}
		}

		private void DoMFA(object sender, MouseButtonEventArgs e) {
			this.OnShowMFA?.Invoke(this._identity);
		}
	}
}
