using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for ZdewLink.xaml
    /// </summary>
    public partial class ZdewLink : UserControl {
        public ZdewLink() {
            InitializeComponent();
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(ZdewLink), new PropertyMetadata(string.Empty));

        public string Text {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public Uri NavigateUri {
            get { return Link.NavigateUri; }
            set { Link.NavigateUri = value; }
        }

        public event RequestNavigateEventHandler RequestNavigate {
            add { Link.RequestNavigate += value; }
            remove { Link.RequestNavigate -= value; }
        }
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
