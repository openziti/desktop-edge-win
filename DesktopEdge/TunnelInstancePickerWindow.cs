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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Dev-grade picker: enumerates all running ziti-edge-tunnel instances and
    /// lets the user click one to switch ZDEW's view to it. Pure-code WPF
    /// window — intentionally minimal. Replace with a polished MainMenu entry
    /// once the plumbing is proven.
    ///
    /// Opened with Ctrl+Shift+T from MainWindow.
    /// </summary>
    public class TunnelInstancePickerWindow : Window {
        public string SelectedDiscriminator { get; private set; }
        public bool InstanceSelected { get; private set; }

        private readonly StackPanel stack;
        private readonly TextBlock statusLabel;
        private readonly string activeDiscriminator;

        public TunnelInstancePickerWindow(string activeDiscriminator) {
            this.activeDiscriminator = activeDiscriminator;
            Title = "Switch tunneler instance (dev)";
            Width = 420;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x28));

            var root = new DockPanel { Margin = new Thickness(12) };

            statusLabel = new TextBlock {
                Text = "enumerating pipes…",
                Foreground = Brushes.Gainsboro,
                Margin = new Thickness(2, 2, 2, 8),
                FontFamily = new FontFamily("Segoe UI")
            };
            DockPanel.SetDock(statusLabel, Dock.Top);
            root.Children.Add(statusLabel);

            var refreshBtn = new Button {
                Content = "Refresh",
                Margin = new Thickness(2, 0, 2, 8),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            refreshBtn.Click += async (_, __) => await PopulateAsync();
            DockPanel.SetDock(refreshBtn, Dock.Top);
            root.Children.Add(refreshBtn);

            var scroll = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent
            };
            stack = new StackPanel();
            scroll.Content = stack;
            root.Children.Add(scroll);

            Content = root;
            Loaded += async (_, __) => await PopulateAsync();
        }

        private async Task PopulateAsync() {
            stack.Children.Clear();
            statusLabel.Text = "enumerating pipes…";

            IReadOnlyList<TunnelInstanceDiscovery.TunnelInstance> discovered;
            try {
                discovered = await TunnelInstanceDiscovery.EnumerateAsync();
            } catch (Exception ex) {
                statusLabel.Text = "enumeration failed: " + ex.Message;
                return;
            }

            // Assemble the display list: default is always first, synthetic if
            // it's not actually running. Everything else follows in the order
            // discovery returned them.
            var ordered = new List<TunnelInstanceDiscovery.TunnelInstance>();
            var def = discovered.FirstOrDefault(i => string.IsNullOrEmpty(i.Discriminator));
            ordered.Add(def ?? TunnelInstanceDiscovery.OfflineDefault());
            foreach (var inst in discovered) {
                if (!string.IsNullOrEmpty(inst.Discriminator)) ordered.Add(inst);
            }

            int liveCount = ordered.Count(i => i.IsOnline);
            statusLabel.Text = liveCount == 0
                ? "no tunnelers running. start one or click default to retry."
                : (liveCount == 1 ? "1 instance running." : liveCount + " instances running.")
                    + " click to switch.";

            foreach (var inst in ordered) {
                stack.Children.Add(BuildInstanceRow(inst));
            }
        }

        private static readonly Color RowBg = Color.FromRgb(0x2a, 0x2a, 0x38);
        private static readonly Color RowBgHover = Color.FromRgb(0x36, 0x36, 0x48);
        private static readonly Color RowBorder = Color.FromRgb(0x3a, 0x3a, 0x48);
        private static readonly Color ActiveBg = Color.FromRgb(0x23, 0x35, 0x28);
        private static readonly Color ActiveBorder = Color.FromRgb(0x4a, 0xc8, 0x4a);

        private FrameworkElement BuildInstanceRow(TunnelInstanceDiscovery.TunnelInstance inst) {
            bool isActive = string.Equals(inst.Discriminator ?? "", activeDiscriminator ?? "", StringComparison.Ordinal)
                            && inst.IsOnline;

            string suffix = isActive ? "   (active)" : (!inst.IsOnline ? "   (not running)" : "");
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            panel.Children.Add(new TextBlock {
                Text = inst.DisplayLabel + suffix,
                FontWeight = FontWeights.Bold,
                Foreground = inst.IsOnline ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xa0, 0xa0, 0xa8)),
                FontFamily = new FontFamily("Segoe UI")
            });

            string subtitle;
            if (!inst.IsOnline) {
                subtitle = "pipe: " + inst.PipeName + "    — click to attempt connect";
            } else {
                subtitle = "pipe: " + inst.PipeName;
                if (!string.IsNullOrEmpty(inst.TunName)) subtitle += "    tun: " + inst.TunName;
                if (!string.IsNullOrEmpty(inst.Ip)) subtitle += "    ip: " + inst.Ip;
                if (!string.IsNullOrEmpty(inst.Dns)) subtitle += "    dns: " + inst.Dns;
            }
            panel.Children.Add(new TextBlock {
                Text = subtitle,
                Foreground = inst.IsOnline ? Brushes.LightGray : new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x88)),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap
            });

            if (isActive) {
                // Non-interactive — a Border avoids any WPF default hover styling.
                return new Border {
                    Child = panel,
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Background = new SolidColorBrush(ActiveBg),
                    BorderBrush = new SolidColorBrush(ActiveBorder),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(2),
                };
            }

            var btn = new Button {
                Content = panel,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = BuildRowTemplate()
            };
            btn.Click += (_, __) => {
                SelectedDiscriminator = inst.Discriminator;
                InstanceSelected = true;
                DialogResult = true;
                Close();
            };
            return btn;
        }

        /// <summary>
        /// Minimal ControlTemplate for row buttons. Replaces the WPF default
        /// (which flips to a light-blue on hover, unusable against the dark
        /// theme) with a two-state dark-on-darker hover.
        /// </summary>
        private static ControlTemplate BuildRowTemplate() {
            var tpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "RowBorder";
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(RowBg));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(RowBorder));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            tpl.VisualTree = border;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(RowBgHover), "RowBorder"));
            tpl.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ActiveBg), "RowBorder"));
            tpl.Triggers.Add(pressedTrigger);

            return tpl;
        }
    }
}
