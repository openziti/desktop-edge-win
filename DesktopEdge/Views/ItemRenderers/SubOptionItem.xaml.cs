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
    public partial class SubOptionItem : UserControl {

		private string _label = "";
		private bool _isSelected = false;

		public string Label {
			get {
				return _label;
			}
			set {
				this._label = value;
				MainLabel.Content = this._label;
			}
		}

		public bool IsSelected {
			get {
				return _isSelected;
			}
			set {
				this._isSelected = value;
				if (this._isSelected) SelectedCheck.Visibility = Visibility.Visible;
				else SelectedCheck.Visibility = Visibility.Collapsed;
			}
		}

		public SubOptionItem() {
            InitializeComponent();
        }
    }
}
