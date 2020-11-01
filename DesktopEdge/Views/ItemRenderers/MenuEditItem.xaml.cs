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
    public partial class MenuEditItem: UserControl {

		private string _label = "";

		public string Label {
			get {
				return _label;
			}
			set {
				this._label = value;
				MainLabel.Text = this._label;
			}
		}
		public string Value {
			get {
				return MainEdit.Text; 
			}
			set {
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

		public MenuEditItem() {
            InitializeComponent();
        }

		private void MainEdit_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
			var textbox = (sender as TextBox);
			textbox.SelectAll();

		}
	}
}
