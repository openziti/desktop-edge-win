using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;
using System.Windows.Media.Animation;

using NLog;

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
		public delegate void Detched(MouseButtonEventArgs e);
		public event Detched OnDetach;
		public double MainHeight = 500;
		public string filter = "";
		public delegate void Mesage(string message);
		public event Mesage OnMessage;

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
				_identity = value;
				this.IdDetailToggle.Enabled = _identity.IsEnabled;
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
			IdDetailName.Text = _identity.Name;
			IdDetailName.ToolTip = _identity.Name;
			IdDetailToggle.Enabled = _identity.IsEnabled;
			IdentityName.Value = _identity.Name;
			IdentityNetwork.Value = _identity.ControllerUrl;
			IdentityEnrollment.Value = _identity.EnrollmentStatus;
			IdentityStatus.Value = _identity.IsEnabled ? "active" : "disabled";
			ServiceList.Children.Clear();
			if (_identity.Services.Count>0) {
				foreach(var zitiSvc in _identity.Services.OrderBy(s => s.Name.ToLower())) {
					if (zitiSvc.Name.ToLower().IndexOf(filter)>=0||zitiSvc.Url.ToLower().IndexOf(filter)>=0) {
						Logger.Debug("painting: " + zitiSvc.Name);
						ServiceInfo info = new ServiceInfo();
						info.Info = zitiSvc;
						info.OnMessage += Info_OnMessage;
						info.OnDetails += Info_OnDetails;
						ServiceList.Children.Add(info);
					}
				}
				double newHeight = MainHeight - 310;
				ServiceRow.Height = new GridLength((double)newHeight);
				MainDetailScroll.MaxHeight = newHeight;
				MainDetailScroll.Height = newHeight;
				MainDetailScroll.Visibility = Visibility.Visible;
				ServiceTitle.Label = _identity.Services.Count + " Services";
				ServiceTitle.MainEdit.Visibility = Visibility.Visible;
			} else {
				ServiceRow.Height = new GridLength((double)0.0);
				MainDetailScroll.Visibility = Visibility.Collapsed;
				ServiceTitle.Label = "No Services";
				ServiceTitle.MainEdit.Text = "";
				ServiceTitle.MainEdit.Visibility = Visibility.Collapsed;
			}
			ConfirmView.Visibility = Visibility.Collapsed;
		}

		private void Info_OnDetails(ZitiService info) {
			DetailName.Text = info.Name;
			DetailUrl.Text = info.Url;
			DetailAddress.Text = info.AssignedIP;
			DetailPorts.Text = info.Port;
			// DetailProtocols.Text = info.Protocols;

			DetailsArea.Visibility = Visibility.Visible;
			DetailsArea.Opacity = 0;
			DetailsArea.Margin = new Thickness(0, 0, 0, 0);
			DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(.3));
			animation.Completed += Animation_Completed;
			DetailsArea.BeginAnimation(Grid.OpacityProperty, animation);
			DetailsArea.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));

			ModalBg.Visibility = Visibility.Visible;
			ModalBg.Opacity = 0;
			ModalBg.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(.8, TimeSpan.FromSeconds(.3)));
		}

		private void Animation_Completed(object sender, EventArgs e) {
			DoubleAnimation animation = new DoubleAnimation(DetailPanel.ActualHeight + 60, TimeSpan.FromSeconds(.3));
			DetailsArea.BeginAnimation(Grid.HeightProperty, animation);
			//DetailsArea.Height = DetailPanel.ActualHeight + 60;
		}

		private void Image_MouseUp(object sender, MouseButtonEventArgs e) {
			DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
			ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
			DoubleAnimation animation2 = new DoubleAnimation(DetailPanel.ActualHeight + 100, TimeSpan.FromSeconds(.3));
			animation.Completed += HideComplete;
			DetailsArea.BeginAnimation(Grid.HeightProperty, animation2);
			DetailsArea.BeginAnimation(Grid.OpacityProperty, animation);
			DetailsArea.BeginAnimation(Grid.MarginProperty, animateThick);
			ModalBg.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(.3)));
		}

		private void HideComplete(object sender, EventArgs e) {
			DetailsArea.Visibility = Visibility.Collapsed;
			ModalBg.Visibility = Visibility.Collapsed;
		}

		private void Info_OnMessage(string message) {
			if (OnMessage != null) OnMessage(message);
		}

		async private void IdToggle(bool on) {
			DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
			await client.IdentityOnOffAsync(_identity.Fingerprint, on);
			if (SelectedIdentity!=null) SelectedIdentity.ToggleSwitch.Enabled = on;
			if (SelectedIdentityMenu != null) SelectedIdentityMenu.ToggleSwitch.Enabled = on;
			_identity.IsEnabled = on;
			IdentityStatus.Value = _identity.IsEnabled ? "active" : "disabled";
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

		private void ForgetIdentity(object sender, MouseButtonEventArgs e) {
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
				await client.RemoveIdentityAsync(_identity.Fingerprint);

				ZitiIdentity forgotten = new ZitiIdentity();
				foreach (var id in identities) {
					if (id.Fingerprint == _identity.Fingerprint) {
						forgotten = id;
						identities.Remove(id);
						break;
					}
				}

				if (OnForgot != null) {
					OnForgot(forgotten);
				}
			} catch (DataStructures.ServiceException se) {
				Logger.Error(se, se.Message);
				OnError(se.Message);
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected: "+ ex.Message);
				OnError("An unexpected error has occured while removing the identity. Please verify the service is still running and try again.");
			}
		}

		private void DoFilter(string filter) {
			this.filter = filter;
			if (this._identity!=null) UpdateView();
		}
	}
}
