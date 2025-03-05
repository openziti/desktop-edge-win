﻿/*
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