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
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using ZitiDesktopEdge.ViewModels;

namespace ZitiDesktopEdge {
    public partial class GetStarted : UserControl {
        public GetStartedViewModel ViewModel { get; }

        /// <summary>Raised when the user clicks the inline "Add by JWT" link.</summary>
        public event EventHandler AddJwtRequested;

        /// <summary>Raised when the user clicks the inline "Add by URL" link.</summary>
        public event EventHandler AddUrlRequested;

        /// <summary>Raised when the user explicitly dismisses the welcome screen (Close button or X).</summary>
        public event EventHandler ClosedByUser;

        public GetStarted() {
            InitializeComponent();
            ViewModel = new GetStartedViewModel();
            DataContext = ViewModel;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, MouseButtonEventArgs e) {
            ViewModel.Close();
            ClosedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void CloseImage_MouseUp(object sender, MouseButtonEventArgs e) {
            ViewModel.Close();
            ClosedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void Logo_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton != MouseButton.Left) return;
            (Application.Current.MainWindow as MainWindow)?.BeginDetachDrag();
        }

        private void Logo_MouseUp(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton != MouseButton.Right) return;
            (Application.Current.MainWindow as MainWindow)?.Reattach();
        }

        private void AddByJwt_Click(object sender, RoutedEventArgs e) {
            AddJwtRequested?.Invoke(this, EventArgs.Empty);
        }

        private void AddByUrl_Click(object sender, RoutedEventArgs e) {
            AddUrlRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
