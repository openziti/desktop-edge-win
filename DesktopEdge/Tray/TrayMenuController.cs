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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NLog;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;
using Ziti.Desktop.Edge.Models;

namespace ZitiDesktopEdge.Tray {
    /// <summary>
    /// Owns the system-tray <see cref="System.Windows.Forms.ContextMenuStrip"/>: builds it,
    /// keeps its dynamic sections (Identities, Switch Tunneler) in sync, drives the
    /// Check-for-Updates spinner, and applies the feedback-in-progress gate. Actions on items
    /// route through <see cref="ITrayHost"/> back into MainWindow.
    /// </summary>
    internal class TrayMenuController {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const string BrandHeaderTag = "brand-header";
        // Items tagged with this string are disabled when a feedback capture is in flight
        // (anything that hits the MonitorClient RPC channel). Items WITHOUT the tag stay
        // enabled -- e.g. Help links, Open log folder, Show welcome screen, Open ZDEW.
        private const string IpcGatedTag = "ipc-gated";
        private const string TrayCheckForUpdatesDefaultLabel = "&Check for updates now";
        // Cheap ASCII spinner: low risk for cross-font rendering and reads as motion at 120ms.
        private static readonly string[] TrayCheckSpinnerFrames = { "|", "/", "-", "\\" };

        private readonly ITrayHost host;
        private readonly Dispatcher dispatcher;

        private readonly System.Windows.Forms.ContextMenuStrip menu;
        private System.Windows.Forms.ToolStripMenuItem trayLogLevelItem;
        private System.Windows.Forms.ToolStripMenuItem trayCheckUpdatesItem;
        private System.Windows.Forms.ToolStripMenuItem trayUpdateNowItem;
        private System.Windows.Forms.Timer trayCheckSpinnerTimer;
        private int trayCheckSpinnerFrame;
        private bool trayUpdateCheckInFlight;

        // Anchor + tracked-items pair for the dynamic Identities section. Identity items live
        // immediately ABOVE trayIdentitiesAnchor and are tracked so we can remove + rebuild
        // them when the identity list changes.
        private System.Windows.Forms.ToolStripSeparator trayIdentitiesAnchor;
        private readonly List<System.Windows.Forms.ToolStripItem> trayIdentityItems = new List<System.Windows.Forms.ToolStripItem>();

        // Switch-tunneler submenu. Hidden unless more than one instance is running.
        private System.Windows.Forms.ToolStripMenuItem trayTunnelerSubmenu;

        // Inserted at the top of the menu while a feedback capture is in flight, removed when
        // the heartbeat goes stale. Visible cue paired with the disabled-everything-else gate.
        private System.Windows.Forms.ToolStripLabel trayFeedbackStatusLabel;
        private System.Windows.Forms.ToolStripSeparator trayFeedbackStatusSeparator;

        public System.Windows.Forms.ContextMenuStrip Menu => menu;

        public TrayMenuController(ITrayHost host, Dispatcher dispatcher) {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.menu = BuildMenu();
        }

        private System.Windows.Forms.ContextMenuStrip BuildMenu() {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            // Branding header. ToolStripMenuItem (so the icon lands in the standard image
            // margin column), Enabled=true (so text+icon render at full colour, not greyed),
            // tagged so the custom renderer skips its hover background and the Closing event
            // cancels close when it's clicked -- effectively a non-interactive label that still
            // looks vivid.
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            var brandHeader = new System.Windows.Forms.ToolStripMenuItem($"Ziti Desktop Edge - By NetFoundry  v{version}") {
                Image = LoadEmbeddedImage("pack://application:,,,/Assets/Images/netfoundry-icon.png"),
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.SizeToFit,
                Tag = BrandHeaderTag
            };
            menu.Items.Add(brandHeader);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            menu.Renderer = new NonInteractiveHeaderRenderer();

            var openZdew = new System.Windows.Forms.ToolStripMenuItem("&Open Ziti Desktop Edge");
            openZdew.Click += (s, e) => host.BringWindowForward();
            menu.Items.Add(openZdew);

            // Dynamic identities section is inserted just above this anchor by
            // RebuildIdentities. If there are zero identities, nothing renders here.
            trayIdentitiesAnchor = new System.Windows.Forms.ToolStripSeparator();
            menu.Items.Add(trayIdentitiesAnchor);

            // Tunneler picker submenu: only shown when more than one ziti-edge-tunnel
            // instance is detected. Populated lazily via RebuildTunnelersAsync on menu open.
            trayTunnelerSubmenu = new System.Windows.Forms.ToolStripMenuItem("Switch &Tunneler") {
                Visible = false,
                Tag = IpcGatedTag
            };
            menu.Items.Add(trayTunnelerSubmenu);

            var addByJwt = new System.Windows.Forms.ToolStripMenuItem("Add Identity by &JWT...") {
                Tag = IpcGatedTag
            };
            addByJwt.Click += (s, e) => { host.BringWindowForward(); host.ShowAddIdentityByJwt(); };
            menu.Items.Add(addByJwt);

            var addByUrl = new System.Windows.Forms.ToolStripMenuItem("Add Identity by &URL...") {
                Tag = IpcGatedTag
            };
            addByUrl.Click += (s, e) => { host.BringWindowForward(); host.ShowAddIdentityByUrl(); };
            menu.Items.Add(addByUrl);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // Logging submenu: set-level submenu first, then log folder below it.
            var logging = new System.Windows.Forms.ToolStripMenuItem("&Logging");
            trayLogLevelItem = new System.Windows.Forms.ToolStripMenuItem("Set &Log Level") {
                Tag = IpcGatedTag
            };
            foreach (string level in new[] { "error", "warn", "info", "debug", "verbose", "trace" }) {
                string capturedLevel = level;
                var item = new System.Windows.Forms.ToolStripMenuItem(
                    char.ToUpper(level[0]) + level.Substring(1)) {
                    CheckOnClick = false
                };
                item.Click += async (s, e) => {
                    await host.SetLogLevelAsync(capturedLevel);
                    RefreshLogLevelChecks(capturedLevel);
                };
                trayLogLevelItem.DropDownItems.Add(item);
            }
            logging.DropDownItems.Add(trayLogLevelItem);
            logging.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            var openLogs = new System.Windows.Forms.ToolStripMenuItem("Open log &folder");
            openLogs.Click += (s, e) => OpenLogFolder();
            logging.DropDownItems.Add(openLogs);
            menu.Items.Add(logging);

            // Help submenu: welcome reopen, update check + apply, feedback, community/support.
            var help = new System.Windows.Forms.ToolStripMenuItem("&Help");
            var showWelcome = new System.Windows.Forms.ToolStripMenuItem("&Show Welcome screen") {
                // Also gated by the feedback flow: the welcome screen layers over the main
                // window, which during feedback is already showing the LoadingScreen modal.
                Tag = IpcGatedTag
            };
            showWelcome.Click += (s, e) => { host.BringWindowForward(); host.ShowWelcomeScreen(); };
            help.DropDownItems.Add(showWelcome);
            help.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

            trayCheckUpdatesItem = new System.Windows.Forms.ToolStripMenuItem(TrayCheckForUpdatesDefaultLabel) {
                Tag = IpcGatedTag
            };
            trayCheckUpdatesItem.Click += TrayCheckForUpdates_Click;
            help.DropDownItems.Add(trayCheckUpdatesItem);

            trayUpdateNowItem = new System.Windows.Forms.ToolStripMenuItem("&Update Now") {
                Visible = false,
                Tag = IpcGatedTag
            };
            trayUpdateNowItem.Click += TrayUpdateNow_Click;
            help.DropDownItems.Add(trayUpdateNowItem);

            help.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

            var captureFeedback = new System.Windows.Forms.ToolStripMenuItem("Capture &Feedback...") {
                Tag = IpcGatedTag
            };
            captureFeedback.Click += (s, e) => {
                host.BringWindowForward();
                host.StartFeedbackCapture();
            };
            help.DropDownItems.Add(captureFeedback);

            help.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

            var community = new System.Windows.Forms.ToolStripMenuItem("OpenZiti Discourse &Community");
            community.Click += (s, e) => OpenExternalUrl("https://openziti.discourse.group/");
            help.DropDownItems.Add(community);
            var nfSupport = new System.Windows.Forms.ToolStripMenuItem("&NetFoundry Support");
            nfSupport.Click += (s, e) => OpenExternalUrl("https://netfoundry.io/support/");
            help.DropDownItems.Add(nfSupport);
            menu.Items.Add(help);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var closeUi = new System.Windows.Forms.ToolStripMenuItem("&Close UI");
            closeUi.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(closeUi);

            // Refresh the tunneler submenu every time the menu is about to open, and gate
            // every interactive item if a feedback capture is currently in flight (the
            // RPC channel is single-threaded; clicking anything else would either short-
            // circuit with "Feedback in progress" or block on the rpc lock for up to 30 min).
            menu.Opening += (s, e) => {
                ApplyFeedbackGate();
                _ = RebuildTunnelersAsync();
            };

            // Track whether the most-recent ItemClicked targeted the brand header so we can
            // cancel the close that would otherwise fire. ToolStripItem.Click runs BEFORE
            // ContextMenuStrip.Closing, so setting a flag here is safe.
            bool brandHeaderClickedRecently = false;
            brandHeader.Click += (s, e) => brandHeaderClickedRecently = true;

            // Cancel close when: (a) a Check-for-Updates request is in flight (so the user
            // actually sees the result), or (b) the click that triggered the close was on the
            // brand header (which is a label, not an action).
            menu.Closing += (s, e) => {
                if (e.CloseReason == System.Windows.Forms.ToolStripDropDownCloseReason.ItemClicked) {
                    if (brandHeaderClickedRecently) {
                        brandHeaderClickedRecently = false;
                        e.Cancel = true;
                        return;
                    }
                    if (trayUpdateCheckInFlight) {
                        e.Cancel = true;
                    }
                }
            };
            // When the menu finally closes, reset the Check-for-Updates label back to its
            // default so the next open is clean. (Don't reset while in-flight or right after,
            // since that would erase the result the user just saw.)
            menu.Closed += (s, e) => {
                if (trayUpdateCheckInFlight) return;
                if (trayCheckUpdatesItem != null) {
                    trayCheckUpdatesItem.Text = TrayCheckForUpdatesDefaultLabel;
                    trayCheckUpdatesItem.Enabled = true;
                }
            };

            return menu;
        }

        public void RefreshLogLevelChecks(string current) {
            if (trayLogLevelItem == null) return;
            foreach (System.Windows.Forms.ToolStripMenuItem item in trayLogLevelItem.DropDownItems) {
                item.Checked = string.Equals(item.Text, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Rebuild the dynamic Identities section of the tray context menu. Removes any
        /// previously inserted items and inserts a header + one row per identity directly above
        /// <see cref="trayIdentitiesAnchor"/>. Each row shows name, service count, and status;
        /// clicking opens the identity details panel for that identity.
        /// </summary>
        public void RebuildIdentities(ZitiIdentity[] ids) {
            if (menu == null || trayIdentitiesAnchor == null) return;

            // Tear down the previous render.
            foreach (var item in trayIdentityItems) {
                menu.Items.Remove(item);
                item.Dispose();
            }
            trayIdentityItems.Clear();

            if (ids == null || ids.Length == 0) return;

            int anchorIndex = menu.Items.IndexOf(trayIdentitiesAnchor);
            if (anchorIndex < 0) return;

            var header = new System.Windows.Forms.ToolStripMenuItem("Identities") {
                Enabled = false
            };
            menu.Items.Insert(anchorIndex, header);
            trayIdentityItems.Add(header);
            anchorIndex++;

            foreach (var id in ids) {
                ZitiIdentity captured = id;
                int svcCount = id.Services?.Count ?? 0;
                string status = DescribeIdentityStatus(id);
                string text = $"-  {id.Name}    {svcCount} svc  ·  {status}";

                var item = new System.Windows.Forms.ToolStripMenuItem(text) {
                    Tag = IpcGatedTag
                };
                item.Click += (s, e) => {
                    host.BringWindowForward();
                    host.OpenIdentityDetails(captured);
                };
                menu.Items.Insert(anchorIndex, item);
                trayIdentityItems.Add(item);
                anchorIndex++;
            }
        }

        /// <summary>
        /// Reflect the current <see cref="ZDEWViewState.UpdateAvailable"/> in the tray menu --
        /// shows or hides the "Update Now" item and labels it with the pending version.
        /// </summary>
        public void RefreshUpdateState() {
            if (trayUpdateNowItem == null) return;
            var state = Application.Current.Properties["ZDEWViewState"] as ZDEWViewState;
            bool show = state != null && state.UpdateAvailable;
            trayUpdateNowItem.Visible = show;
            if (show) {
                string ver = state.PendingUpdate?.Version;
                trayUpdateNowItem.Text = string.IsNullOrEmpty(ver)
                    ? "&Update Now"
                    : $"&Update Now (v{ver})";
            }
        }

        private async void TrayCheckForUpdates_Click(object sender, EventArgs e) {
            if (trayCheckUpdatesItem == null) return;
            var monitorClient = Application.Current.Properties["MonitorClient"] as MonitorClient;
            // The RPC channel is single-threaded; if a feedback capture is currently holding
            // it, our DoUpdateCheck would block on the lock for up to 30 minutes. Short-circuit
            // with a readable message instead of leaving the spinner running forever.
            if (monitorClient != null && monitorClient.IsServiceCapturingFeedback) {
                trayCheckUpdatesItem.Text = "Busy collecting feedback -- try again shortly";
                return;
            }
            trayUpdateCheckInFlight = true;
            StartSpinner();
            trayCheckUpdatesItem.Enabled = false;
            try {
                if (monitorClient == null) {
                    StopSpinner("Monitor service offline");
                    return;
                }
                var r = await monitorClient.DoUpdateCheck();
                if (r == null) {
                    StopSpinner("Error checking -- see logs");
                    return;
                }
                bool updateAvail = r.UpdateAvailable;
                var state = Application.Current.Properties["ZDEWViewState"] as ZDEWViewState;
                if (state != null) state.UpdateAvailable = updateAvail;
                RefreshUpdateState();
                string pendingVer = state?.PendingUpdate?.Version;
                string result = updateAvail
                    ? (string.IsNullOrEmpty(pendingVer) ? "Update available" : $"Update available: v{pendingVer}")
                    : "No updates available";
                StopSpinner(result);
            } catch (Exception ex) {
                logger.Error(ex, "tray update check failed");
                StopSpinner("Error checking -- see logs");
            } finally {
                trayUpdateCheckInFlight = false;
                // Leave the result label in place. menu.Closed restores the default on next
                // dismissal so the user gets a clean state next time they open the menu.
                trayCheckUpdatesItem.Enabled = true;
            }
        }

        private void StartSpinner() {
            trayCheckSpinnerFrame = 0;
            if (trayCheckSpinnerTimer == null) {
                trayCheckSpinnerTimer = new System.Windows.Forms.Timer { Interval = 120 };
                trayCheckSpinnerTimer.Tick += (s, e) => {
                    if (trayCheckUpdatesItem == null) return;
                    string frame = TrayCheckSpinnerFrames[trayCheckSpinnerFrame % TrayCheckSpinnerFrames.Length];
                    trayCheckUpdatesItem.Text = $"Checking for updates {frame}";
                    trayCheckSpinnerFrame++;
                };
            }
            trayCheckUpdatesItem.Text = "Checking for updates |";
            trayCheckSpinnerTimer.Start();
        }

        private void StopSpinner(string finalLabel) {
            trayCheckSpinnerTimer?.Stop();
            if (trayCheckUpdatesItem != null) {
                trayCheckUpdatesItem.Text = finalLabel;
            }
        }

        private async void TrayUpdateNow_Click(object sender, EventArgs e) {
            if (trayUpdateNowItem == null) return;
            var monitorClient = Application.Current.Properties["MonitorClient"] as MonitorClient;
            if (monitorClient != null && monitorClient.IsServiceCapturingFeedback) {
                trayUpdateNowItem.Text = "Busy collecting feedback -- try again shortly";
                return;
            }
            try {
                trayUpdateNowItem.Enabled = false;
                if (monitorClient == null) {
                    trayUpdateNowItem.Text = "Monitor service offline";
                    return;
                }
                var r = await monitorClient.TriggerUpdate(forceDefer: false);
                if (r == null) {
                    trayUpdateNowItem.Text = "Error requesting update";
                } else if (!string.IsNullOrEmpty(r.Message)) {
                    trayUpdateNowItem.Text = r.Message;
                }
            } catch (Exception ex) {
                logger.Error(ex, "tray update-now failed");
                trayUpdateNowItem.Text = "Error -- see logs";
            } finally {
                trayUpdateNowItem.Enabled = true;
            }
        }

        /// <summary>
        /// Refresh the "Switch Tunneler" submenu. Hidden when there's only one (or zero)
        /// ziti-edge-tunnel instance running, since there's nothing useful to switch to.
        /// Otherwise lists each instance, marks the active one, and wires click handlers
        /// that route through <see cref="ITrayHost.SwitchTunnelerAsync"/>.
        /// </summary>
        private async Task RebuildTunnelersAsync() {
            if (trayTunnelerSubmenu == null) return;
            IReadOnlyList<TunnelInstanceDiscovery.TunnelInstance> discovered;
            try {
                discovered = await TunnelInstanceDiscovery.EnumerateAsync();
            } catch (Exception ex) {
                logger.Debug(ex, "tunneler enumeration failed");
                dispatcher.Invoke(() => trayTunnelerSubmenu.Visible = false);
                return;
            }

            // Same ordering as the picker window: default first (synthetic if not actually
            // online), then everything else in discovery order.
            var ordered = new List<TunnelInstanceDiscovery.TunnelInstance>();
            var def = discovered.FirstOrDefault(i => string.IsNullOrEmpty(i.Discriminator));
            ordered.Add(def ?? TunnelInstanceDiscovery.OfflineDefault());
            foreach (var inst in discovered) {
                if (!string.IsNullOrEmpty(inst.Discriminator)) ordered.Add(inst);
            }

            dispatcher.Invoke(() => {
                // Tear down previous items. ToolStripItem.Dispose() removes the item from its
                // owner's collection, so iterating DropDownItems while disposing skips every
                // other entry -- the source of the "every other click" symptom. Snapshot
                // first, clear the collection in one shot, then dispose the snapshot.
                var oldItems = new System.Windows.Forms.ToolStripItem[trayTunnelerSubmenu.DropDownItems.Count];
                trayTunnelerSubmenu.DropDownItems.CopyTo(oldItems, 0);
                trayTunnelerSubmenu.DropDownItems.Clear();
                foreach (var oldItem in oldItems) {
                    oldItem.Dispose();
                }

                if (ordered.Count <= 1) {
                    trayTunnelerSubmenu.Visible = false;
                    return;
                }

                var serviceClient = Application.Current.Properties["ServiceClient"] as DataClient;
                string activeDisc = serviceClient?.Discriminator ?? "";
                foreach (var inst in ordered) {
                    string instDisc = inst.Discriminator ?? "";
                    bool isActive = string.Equals(instDisc, activeDisc, StringComparison.Ordinal) && inst.IsOnline;
                    string suffix = isActive ? "   (active)" : (!inst.IsOnline ? "   (not running)" : "");

                    var item = new System.Windows.Forms.ToolStripMenuItem(inst.DisplayLabel + suffix) {
                        Checked = isActive,
                        CheckOnClick = false
                    };
                    if (isActive) {
                        // Active row is non-interactive; clicking shouldn't reconnect to self.
                        item.Enabled = false;
                    } else {
                        string capturedDisc = inst.Discriminator;
                        item.Click += async (s, e) => await host.SwitchTunnelerAsync(capturedDisc);
                    }
                    trayTunnelerSubmenu.DropDownItems.Add(item);
                }
                trayTunnelerSubmenu.Visible = true;
            });
        }

        /// <summary>
        /// While a feedback capture is in flight, insert a visible status row at the top of
        /// the menu and disable every interactive item tagged <see cref="IpcGatedTag"/>. Items
        /// without the tag (Help links, Open log folder, Open ZDEW, Close UI) stay enabled.
        /// State is rechecked every menu.Opening so it's always fresh.
        /// </summary>
        private void ApplyFeedbackGate() {
            if (menu == null) return;
            var mc = Application.Current.Properties["MonitorClient"] as MonitorClient;
            bool feedbackInFlight = mc != null && mc.IsServiceCapturingFeedback;
            bool enabled = !feedbackInFlight;

            // Insert / remove the visible "Collecting feedback..." status row right after the
            // brand-header + first separator (so it reads as a peer of the brand row, not
            // buried under enable-toggled items). Both the label and its trailing separator
            // are tracked so removal is precise.
            if (feedbackInFlight && trayFeedbackStatusLabel == null) {
                trayFeedbackStatusLabel = new System.Windows.Forms.ToolStripLabel("Collecting feedback...") {
                    Font = new System.Drawing.Font(System.Drawing.SystemFonts.MenuFont, System.Drawing.FontStyle.Italic),
                    ForeColor = System.Drawing.Color.FromArgb(0xCC, 0x55, 0x00)
                };
                trayFeedbackStatusSeparator = new System.Windows.Forms.ToolStripSeparator();
                // Index 2 = right after brand-header (0) + its separator (1).
                menu.Items.Insert(2, trayFeedbackStatusLabel);
                menu.Items.Insert(3, trayFeedbackStatusSeparator);
            } else if (!feedbackInFlight && trayFeedbackStatusLabel != null) {
                menu.Items.Remove(trayFeedbackStatusLabel);
                menu.Items.Remove(trayFeedbackStatusSeparator);
                trayFeedbackStatusLabel.Dispose();
                trayFeedbackStatusSeparator.Dispose();
                trayFeedbackStatusLabel = null;
                trayFeedbackStatusSeparator = null;
            }

            // Granular gating: only items explicitly tagged with IpcGatedTag (touched the
            // MonitorClient RPC channel one way or another) get disabled. Everything else --
            // Help submenu, Open log folder, Show Welcome, Open ZDEW, external URL links --
            // stays available. Recurses into submenus so deeply-nested items get gated too.
            ToggleIpcGatedItems(menu.Items, enabled);
        }

        private static void ToggleIpcGatedItems(System.Windows.Forms.ToolStripItemCollection items, bool enabled) {
            foreach (System.Windows.Forms.ToolStripItem item in items) {
                if (item.Tag as string == IpcGatedTag) {
                    item.Enabled = enabled;
                }
                if (item is System.Windows.Forms.ToolStripMenuItem mi && mi.HasDropDownItems) {
                    ToggleIpcGatedItems(mi.DropDownItems, enabled);
                }
            }
        }

        private static string DescribeIdentityStatus(ZitiIdentity id) {
            if (!id.IsEnabled) return "Disabled";
            if (id.IsTimedOut) return "Locked";
            if (id.IsTimingOut) return "Timing out";
            if (id.IsMFANeeded) return "MFA needed";
            if (id.NeedsExtAuth) return "Auth needed";
            return "Active";
        }

        private void OpenLogFolder() {
            try {
                Process.Start(new ProcessStartInfo(MainWindow.ExpectedLogPathRoot) { UseShellExecute = true });
            } catch (Exception ex) {
                logger.Warn(ex, "could not open log folder {0}", MainWindow.ExpectedLogPathRoot);
            }
        }

        private void OpenExternalUrl(string url) {
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception ex) {
                logger.Warn(ex, "could not open URL {0}", url);
            }
        }

        /// <summary>
        /// Load a Resource-bundled image into a System.Drawing.Image suitable for use as a
        /// ToolStripItem.Image. The image keeps an internal reference to its source stream so
        /// we deliberately hold the MemoryStream alive for the lifetime of the bitmap.
        /// </summary>
        private static System.Drawing.Image LoadEmbeddedImage(string packUri) {
            try {
                var sri = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
                if (sri == null) return null;
                var ms = new MemoryStream();
                sri.Stream.CopyTo(ms);
                ms.Position = 0;
                return new System.Drawing.Bitmap(ms);
            } catch (Exception ex) {
                logger.Warn(ex, "could not load embedded image {0}", packUri);
                return null;
            }
        }
    }

    /// <summary>
    /// ToolStripProfessionalRenderer that paints no hover/focus highlight for items marked as
    /// non-interactive headers via Tag = "brand-header". Used by the tray ContextMenuStrip so
    /// the "By NetFoundry" row reads as a label even though it's an Enabled ToolStripMenuItem
    /// (Enabled=true keeps the text and icon at full color rather than greyed).
    /// </summary>
    internal class NonInteractiveHeaderRenderer : System.Windows.Forms.ToolStripProfessionalRenderer {
        // Must match TrayMenuController.BrandHeaderTag.
        private const string BrandHeaderTag = "brand-header";

        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e) {
            if (e.Item?.Tag as string == BrandHeaderTag) {
                return;
            }
            base.OnRenderMenuItemBackground(e);
        }
    }
}
