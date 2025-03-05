using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;

namespace ZitiDesktopEdge {
    public partial class ConfirmationDialog : UserControl {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ConfirmationDialog), new PropertyMetadata("Confirmation Title"));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(ConfirmationDialog), new PropertyMetadata("This is the description."));

        public static readonly DependencyProperty OkFuncProperty =
            DependencyProperty.Register(nameof(OkFunc), typeof(ICommand), typeof(ConfirmationDialog));

        public static readonly DependencyProperty CancelFuncProperty =
            DependencyProperty.Register(nameof(CancelFunc), typeof(ICommand), typeof(ConfirmationDialog));

        public string Title {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Description {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public ICommand OkFunc {
            get => (ICommand)GetValue(OkFuncProperty);
            set => SetValue(OkFuncProperty, value);
        }

        public ICommand CancelFunc {
            get => (ICommand)GetValue(CancelFuncProperty);
            set => SetValue(CancelFuncProperty, value);
        }

        public ConfirmationDialog() {
            InitializeComponent();
        }

        private void ExecuteClose(object sender, MouseButtonEventArgs e) {
            CancelFunc.Execute(null);
        }

        private void CancelAction(object sender, MouseButtonEventArgs e) {
            CancelFunc.Execute(null);
        }

        private void ConfirmationAction(object sender, MouseButtonEventArgs e) {
            OkFunc.Execute(null);
        }
    }
}
