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
    public partial class SubMenuItem : UserControl {

		private string _label = "";

		public string Label {
			get {
				return _label;
			}
			set {
				this._label = value;
				MainLabel.Content = this._label;
			}
		}

		public SubMenuItem() {
            InitializeComponent();
        }
    }
}
