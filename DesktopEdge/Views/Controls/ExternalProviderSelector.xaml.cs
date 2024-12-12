using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for ExternalProviderSelector.xaml
    /// </summary>
    public partial class ExternalProviderSelector : UserControl, INotifyPropertyChanged {

        private ObservableCollection<string> providers;
        public ObservableCollection<string> Providers {
            get => providers;
            set {
                if (providers != value) {
                    providers = value;
                    OnPropertyChanged(nameof(Providers));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ExternalProviderSelector() {
            InitializeComponent();
            Providers = new ObservableCollection<string>(); // Initialize collection to prevent null reference errors
        }

        private void UserControl_MouseLeave(object sender, MouseEventArgs e) {
            Debug.WriteLine("Mouse left the user control.");
        }
    }
}