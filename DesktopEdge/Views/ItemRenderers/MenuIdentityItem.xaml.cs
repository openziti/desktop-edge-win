/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.DataStructures;
using Ziti.Desktop.Edge.Utils;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MenuItem.xaml
    /// </summary>
    public partial class MenuIdentityItem : UserControl {

        private IdentityViewModel IdentityViewModel => DataContext as IdentityViewModel;
        public ZitiIdentity Identity => IdentityViewModel?.Identity;

        public MenuIdentityItem() {
            InitializeComponent();
            ToggleSwitch.OnToggled += ToggleIdentity;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            IdentityViewModel oldViewModel = e.OldValue as IdentityViewModel;
            if (oldViewModel != null) {
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            IdentityViewModel newViewModel = e.NewValue as IdentityViewModel;
            if (newViewModel != null) {
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
                ToggleSwitch.Enabled = newViewModel.Identity.IsEnabled;
            }
        }

        // Toggler.Enabled is not bindable, so keep it in step with the VM whenever it re-renders.
        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (IdentityViewModel != null) ToggleSwitch.Enabled = IdentityViewModel.Identity.IsEnabled;
        }

        async private void ToggleIdentity(bool on) {
            try {
                await IdentityViewModel.SetEnabledAsync(on);
            } catch (ServiceException se) {
                MessageBox.Show(se.AdditionalInfo, se.Message);
            } catch (Exception ex) {
                MessageBox.Show("Error", ex.Message);
            }
        }

        private void ShowIdentity(object sender, MouseButtonEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
            deets.SelectedIdentityMenu = this;
            deets.Identity = this.Identity;
        }

        private void MainUI_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            UIUtils.ClickedControl = e.Source as UIElement;
        }
    }
}
