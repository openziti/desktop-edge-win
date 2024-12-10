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
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class FilePicker : UserControl {
        public FilePicker() {
            InitializeComponent();
        }

        // Dependency Property for KeyFile Text
        public static readonly DependencyProperty KeyFileTextProperty =
            DependencyProperty.Register("KeyFileText", typeof(string), typeof(FilePicker), new PropertyMetadata(string.Empty));

        public string KeyFileText {
            get => (string)GetValue(KeyFileTextProperty);
            set => SetValue(KeyFileTextProperty, value);
        }

        // Dependency Property for Button Content
        public static readonly DependencyProperty ButtonContentProperty =
            DependencyProperty.Register("ButtonContent", typeof(string), typeof(FilePicker), new PropertyMetadata("Browse"));

        public string ButtonContent {
            get => (string)GetValue(ButtonContentProperty);
            set => SetValue(ButtonContentProperty, value);
        }

        // Dependency Property for Label Content
        public static readonly DependencyProperty LabelContentProperty =
            DependencyProperty.Register("LabelContent", typeof(string), typeof(FilePicker), new PropertyMetadata("Key File"));

        public string LabelContent {
            get => (string)GetValue(LabelContentProperty);
            set => SetValue(LabelContentProperty, value);
        }
    }
}
