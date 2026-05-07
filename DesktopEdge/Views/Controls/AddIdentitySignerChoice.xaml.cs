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
using System.Windows.Controls;
using System.Windows.Input;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    public partial class AddIdentitySignerChoice : UserControl {
        public event CommonDelegates.CloseAction OnClose;
        public event Action<EnrollIdentifierPayload, UserControl> OnAddIdentity;

        public AddIdentitySignerChoice() {
            InitializeComponent();
        }

        public void Prepare(AddIdentityViewModel viewModel) {
            DataContext = viewModel;
        }

        private void JoinNetworkUrl(object sender, MouseButtonEventArgs e) {
            AddIdentityViewModel vm = DataContext as AddIdentityViewModel;
            if (vm == null || !vm.CanJoin) return;
            OnAddIdentity?.Invoke(vm.BuildEnrollPayload(), this);
        }

        private void ExecuteClose(object sender, MouseButtonEventArgs e) {
            this.OnClose?.Invoke(false, this);
        }
    }
}
