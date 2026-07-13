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

namespace ZitiDesktopEdge {
    public class LogLevelViewModel : ViewModelBase {
        private string _currentLevel = "";
        public string CurrentLevel {
            get { return _currentLevel; }
            set {
                _currentLevel = value;
                OnPropertyChanged(nameof(CurrentLevel));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(IsWarn));
                OnPropertyChanged(nameof(IsInfo));
                OnPropertyChanged(nameof(IsDebug));
                OnPropertyChanged(nameof(IsVerbose));
                OnPropertyChanged(nameof(IsTrace));
            }
        }

        public bool IsError => _currentLevel == "error";
        public bool IsWarn => _currentLevel == "warn";
        public bool IsInfo => _currentLevel == "info";
        public bool IsDebug => _currentLevel == "debug";
        public bool IsVerbose => _currentLevel == "verbose";
        public bool IsTrace => _currentLevel == "trace";
    }
}
