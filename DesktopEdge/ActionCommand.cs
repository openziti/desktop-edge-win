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
using System.Windows.Input;

namespace ZitiDesktopEdge {
    public class ActionCommand : ICommand {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public ActionCommand(Action execute, Func<bool> canExecute) {
            _execute = parameter => execute();
            _canExecute = parameter => canExecute();
        }

        public ActionCommand(Action<object> execute, Predicate<object> canExecute) {
            _execute = execute;
            _canExecute = canExecute;
        }

        public void Execute(object parameter) {
            _execute(parameter);
        }

        public bool CanExecute(object parameter) {
            return _canExecute(parameter);
        }

        public event EventHandler CanExecuteChanged {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
