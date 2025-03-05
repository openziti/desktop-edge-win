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
using System.Timers;
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for Filter.xaml
    /// </summary>
    public partial class Filter : UserControl {

        public delegate void OnFilterEvent(FilterData filter);
        public event OnFilterEvent OnFilter;
        public string placeholder = "any text";
        private static Timer timeout;

        private FilterData filter = new FilterData("", "Name", "Asc");
        public Filter() {
            InitializeComponent();
        }

        public void Clear() {
            FilterFor.Text = "";
            filter.SearchFor = "";
            FilterFor.Text = placeholder;
            SortWayField.SelectedIndex = 0;
            SortByField.SelectedIndex = 0;
        }

        private void FilterPressed(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                OnFilter?.Invoke(filter);
            }
        }

        private void FilterChanged(object sender, KeyEventArgs e) {
            string search = FilterFor.Text.Trim();
            if (filter.SearchFor != search) {
                filter.SearchFor = search;
                if (filter.SearchFor == placeholder) filter.SearchFor = "";

                if (e.Key == Key.Enter) {
                    OnFilter?.Invoke(filter);
                } else {
                    if (timeout != null && timeout.Enabled) {
                        timeout.Close();
                    }
                    timeout = new Timer(1000);
                    timeout.Elapsed += OnTimedEvent;
                    timeout.AutoReset = false;
                    timeout.Enabled = true;
                }
            }
        }

        private void SortWayChanged(object sender, SelectionChangedEventArgs e) {
            ComboBoxItem selected = (ComboBoxItem)SortWayField.SelectedValue;
            if (selected != null && selected.Content != null) {
                if (selected.Content.ToString() != filter.SortHow) {
                    filter.SortHow = selected.Content.ToString();
                    this.OnFilter?.Invoke(filter);
                }
            }
        }

        private void SortByChanged(object sender, SelectionChangedEventArgs e) {
            ComboBoxItem selected = (ComboBoxItem)SortByField.SelectedValue;
            if (selected != null && selected.Content != null) {
                if (selected.Content.ToString() != filter.SortBy) {
                    filter.SortBy = selected.Content.ToString();
                    this.OnFilter?.Invoke(filter);
                }
            }
        }

        private void FocusFilter(object sender, RoutedEventArgs e) {
            if (FilterFor.Text == placeholder) {
                FilterFor.Text = "";
            }
        }

        private void FocusLostFilter(object sender, RoutedEventArgs e) {
            if (FilterFor.Text.Trim() == "") {
                FilterFor.Text = placeholder;
            }
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e) {
            this.Dispatcher.Invoke(() => {
                OnFilter?.Invoke(filter);
            });
        }
    }
}
