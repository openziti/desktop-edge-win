using NLog;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    public partial class AddIdentityUrl : UserControl {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public event CommonDelegates.CloseAction OnClose;
        public event Action<EnrollIdentifierPayload, UserControl> OnAddIdentity;
        //public event Action<EnrollIdentifierPayload, string> OnAddIdentity2;

        public CommonDelegates.JoinNetwork JoinNetwork;

        public AddIdentityUrl() {
            InitializeComponent();
        }

        private void JoinNetworkUrl(object sender, MouseButtonEventArgs e) {
            Console.WriteLine("join");

            EnrollIdentifierPayload payload = new EnrollIdentifierPayload();
            payload.ControllerURL = ControllerURL.Text;

            Uri ctrl = new Uri(ControllerURL.Text);
            payload.IdentityFilename = ctrl.Host + "_" + ctrl.Port;
            OnAddIdentity(payload, this);
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
    }
}
