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

// Replaces the WpfMouseWheelLib.dll, which crashed under rapid
// mouse wheel input. See https://github.com/openziti/desktop-edge-win/issues/823
namespace Ziti.Desktop.Edge.Utils {
    /// <summary>
    /// Attached behavior that makes a <see cref="ScrollViewer"/> advance one
    /// child row per mouse wheel notch by delegating to the ScrollViewer's
    /// LineUp/LineDown commands. The host ScrollViewer must
    /// have CanContentScroll="True"
    /// </summary>
    public static class WheelScroll {
        // Standard Windows wheel notch. One physical detent on a wheel raises
        // MouseWheelEventArgs.Delta by this amount (WHEEL_DELTA in WinUser.h).
        private const int WheelDeltaPerNotch = 120;

        public static readonly DependencyProperty ByItemProperty =
            DependencyProperty.RegisterAttached(
                "ByItem",
                typeof(bool),
                typeof(WheelScroll),
                new PropertyMetadata(false, OnByItemChanged));

        public static bool GetByItem(DependencyObject target) {
            return (bool)target.GetValue(ByItemProperty);
        }

        public static void SetByItem(DependencyObject target, bool value) {
            target.SetValue(ByItemProperty, value);
        }

        private static void OnByItemChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) {
            ScrollViewer scrollViewer = target as ScrollViewer;
            if (scrollViewer == null) return;

            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            if ((bool)args.NewValue) {
                scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args) {
            ScrollViewer scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null || args.Delta == 0) return;

            // Convert wheel delta to whole notches. Trackpads can emit values
            // smaller than one notch; round half away from zero so any motion
            // moves at least one row
            int notches = (int)Math.Round((double)args.Delta / WheelDeltaPerNotch, MidpointRounding.AwayFromZero);
            if (notches == 0) {
                notches = args.Delta > 0 ? 1 : -1;
            }

            // LineUp/LineDown advance by exactly one IScrollInfo line. With
            // CanContentScroll=True over a panel that supports logical
            // scrolling (StackPanel, VirtualizingStackPanel), one line is one
            // child item, which is the behavior we want.
            if (notches > 0) {
                for (int i = 0; i < notches; i++) scrollViewer.LineUp();
            } else {
                for (int i = 0; i < -notches; i++) scrollViewer.LineDown();
            }

            args.Handled = true;
        }
    }
}
