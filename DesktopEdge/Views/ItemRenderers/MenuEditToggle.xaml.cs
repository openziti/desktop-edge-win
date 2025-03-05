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
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MenuItem.xaml
    /// </summary>
    public partial class MenuEditToggle : UserControl {

        public delegate void OnToggle(bool isOn);
        public event OnToggle Toggle;
        public delegate void OnAuthenticate();
        public event OnAuthenticate Authenticate;
        public delegate void OnRecovery();
        public event OnRecovery Recovery;
        private ZitiIdentity _identity;

        private string _label = "";

        public string Label {
            get {
                return _label;
            }
            set {
                this._label = value;
                this.MainLabel.Text = this._label;
            }
        }
        public bool IsOn {
            get {
                return ToggleField.Enabled;
            }
            set {
                ToggleField.Enabled = value;
            }
        }

        public ZitiIdentity Identity {
            get {
                return _identity;
            }
            set {
                _identity = value;
            }
        }

        public MenuEditToggle() {
            InitializeComponent();
        }

        public void Toggled(Boolean isOn) {
            this.ToggleField.Enabled = isOn;
            this.Toggle?.Invoke(isOn);
        }

        private void MFAAuthenticate(object sender, MouseButtonEventArgs e) {
            this.Authenticate?.Invoke();
        }

        private void MFARecovery(object sender, MouseButtonEventArgs e) {
            this.Recovery?.Invoke();
        }
    }
}
