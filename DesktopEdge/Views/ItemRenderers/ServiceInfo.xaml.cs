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

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MenuItem.xaml
    /// </summary>
    public partial class ServiceInfo: UserControl {

		private string _label = "";
		private string _warning = "";

		public string Warning {
			get {
				return _warning;
			}
			set {
				_warning = value;
				if (_warning.Length>0) {
					WarnIcon.ToolTip = _warning;
					WarnIcon.Visibility = Visibility.Visible;
					WarningColumn.Width = new GridLength(30);
				}
			} 
		}

		public string Label {
			get {
				return _label;
			}
			set {
				this._label = value;
				MainLabel.ToolTip = this._label;
				MainLabel.Text = this._label;
			}
		}
		public string Value {
			get {
				return MainEdit.Text; 
			}
			set {
				MainEdit.ToolTip = value;
				MainEdit.Text = value;
			}
		}
		public bool IsLocked {
			get {
				return MainEdit.IsReadOnly;
			} 
			set {
				MainEdit.IsReadOnly = value;
			}
		}

		public ServiceInfo() {
            InitializeComponent();
        }

		private void MainEdit_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
			var textbox = (sender as TextBox);
			textbox.SelectAll();
		}
	}
}
