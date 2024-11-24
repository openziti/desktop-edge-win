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
using System.Windows.Shapes;

namespace ZitiDesktopEdge {
    public partial class UrlEntryDialog : UserControl {
        public string EnteredUrl { get; private set; }
        public event Action<string> OnSubmit;
        public event Action OnCancel;

        public UrlEntryDialog() {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            EnteredUrl = UrlTextBox.Text;
            OnSubmit?.Invoke(EnteredUrl);
            this.Visibility = Visibility.Collapsed;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            OnCancel?.Invoke();
            this.Visibility = Visibility.Collapsed;
        }
    }
}
