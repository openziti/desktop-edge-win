/*
    Copyright NetFoundry Inc.

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    https://www.apache.org/licenses/LICENSE-2.0
*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {

    /// <summary>
    /// Outcome of MaintenanceWindowControl.PersistAsync. On failure, FailedOperation names the
    /// IPC step that bounced (for log context) and ErrorMessage holds the user-facing string.
    /// On success, the captured values let the host update its in-memory settings cache
    /// without re-reading them from the combos (which the PropertyChanged-driven re-render
    /// may have already mutated by the time the host gets control back).
    /// </summary>
    public class MaintenanceWindowPersistResult {
        public bool Success;
        public string FailedOperation;  // null on success
        public string ErrorMessage;     // null on success
        public int StartHour;
        public int EndHour;
        public MaintenanceWindowFrequency Frequency;
        public MaintenanceWindowMonthlyMode MonthlyMode;
        public int? DayOfWeek;
        public int? DayOfMonth;
        public MaintenanceWindowMonthlyOrdinal? MonthlyOrdinal;
    }
    /// <summary>
    /// Self-contained Installation maintenance-window editor. Hosts:
    ///   - Frequency combo (Daily / Weekly / Monthly)
    ///   - Monthly Mode combo (By date / By weekday), visible only for Monthly
    ///   - Day picker(s): DayOfWeek (Weekly), DayOfMonth (Monthly ByDate), or
    ///     Ordinal + DayOfWeek (Monthly ByWeekday)
    ///   - From / To hour combos plus "Any time" checkbox
    ///
    /// API for the host (typically MainMenu):
    ///   - ApplyFromState(...) -- pushes saved values into the combos.
    ///   - Read-only getters -- read by the host's save handler to push to IPC.
    ///   - IsEditable -- enables / disables all combos and dims the heading.
    /// </summary>
    public partial class MaintenanceWindowControl : UserControl {
        // Order of frequency items must match the MaintenanceWindowFrequency enum
        // (Daily=0, Weekly=1, Monthly=2).
        private static readonly string[] FrequencyLabels = { "Daily", "Weekly", "Monthly" };
        // Order matches System.DayOfWeek (Sunday=0 .. Saturday=6) which is what we
        // persist in settings.json / the policy registry.
        private static readonly string[] DayOfWeekLabels = {
            "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
        };
        // Order matches MaintenanceWindowMonthlyMode (ByDate=0, ByWeekday=1).
        private static readonly string[] MonthlyModeLabels = { "By date", "By weekday" };
        // Order matches MaintenanceWindowMonthlyOrdinal (First=1 .. Last=5); we render in
        // that order so SelectedIndex+1 maps directly to the enum value.
        private static readonly string[] OrdinalLabels = { "First", "Second", "Third", "Fourth", "Last" };

        private bool _isEditable = true;

        public MaintenanceWindowControl() {
            InitializeComponent();
            PopulateHourCombos();
            PopulateFrequencyAndDayCombos();
            UpdateFrequencyDayVisibility();
        }

        // ---------------------------------------------------------------------------------
        // Host API: read these in the parent's Save handler.
        // ---------------------------------------------------------------------------------

        /// <summary>Hour of day (0-23) when the maintenance window opens.</summary>
        public int WindowStart => Math.Max(0, MaintenanceWindowStartCombo.SelectedIndex);

        /// <summary>Hour of day (0-23) when the maintenance window closes.</summary>
        public int WindowEnd => Math.Max(0, MaintenanceWindowEndCombo.SelectedIndex);

        /// <summary>Configured cadence.</summary>
        public MaintenanceWindowFrequency Frequency =>
            (MaintenanceWindowFrequency)Math.Max(0, MaintenanceWindowFrequencyCombo.SelectedIndex);

        /// <summary>Monthly sub-mode (only relevant when Frequency == Monthly).</summary>
        public MaintenanceWindowMonthlyMode MonthlyMode =>
            (MaintenanceWindowMonthlyMode)Math.Max(0, MaintenanceWindowMonthlyModeCombo.SelectedIndex);

        /// <summary>
        /// Day-of-week to persist for the current cadence shape. Returns null when the
        /// configured cadence doesn't use a day-of-week (Daily, or Monthly+ByDate).
        /// </summary>
        public int? DayOfWeekToPersist {
            get {
                bool needsDow = Frequency == MaintenanceWindowFrequency.Weekly
                             || (Frequency == MaintenanceWindowFrequency.Monthly
                                 && MonthlyMode == MaintenanceWindowMonthlyMode.ByWeekday);
                if (!needsDow) return null;
                return Math.Max(0, MaintenanceWindowDayOfWeekCombo.SelectedIndex);
            }
        }

        /// <summary>
        /// Day-of-month to persist for the current cadence shape. Returns null unless
        /// Frequency=Monthly and MonthlyMode=ByDate. The "Last day" entry maps to the
        /// MaintenanceWindowDayOfMonthSentinel.LastDay value.
        /// </summary>
        public int? DayOfMonthToPersist {
            get {
                bool needsDom = Frequency == MaintenanceWindowFrequency.Monthly
                             && MonthlyMode == MaintenanceWindowMonthlyMode.ByDate;
                if (!needsDom) return null;
                return DayOfMonthSelectedValue();
            }
        }

        /// <summary>
        /// Ordinal to persist for the current cadence shape. Returns null unless
        /// Frequency=Monthly and MonthlyMode=ByWeekday.
        /// </summary>
        public MaintenanceWindowMonthlyOrdinal? OrdinalToPersist {
            get {
                bool needsOrd = Frequency == MaintenanceWindowFrequency.Monthly
                             && MonthlyMode == MaintenanceWindowMonthlyMode.ByWeekday;
                if (!needsOrd) return null;
                return (MaintenanceWindowMonthlyOrdinal)(Math.Max(0, MaintenanceWindowOrdinalCombo.SelectedIndex) + 1);
            }
        }

        /// <summary>
        /// True when controls should accept user input. When false, every combo + the
        /// "Any time" checkbox are disabled and dimmed, and the heading dims to match.
        /// </summary>
        public bool IsEditable {
            get => _isEditable;
            set {
                _isEditable = value;
                ApplyEditableState();
            }
        }

        /// <summary>
        /// Push saved-state values into the combos. Suspends the SelectionChanged hooks
        /// during the bulk-set so visibility doesn't recompute mid-apply.
        /// </summary>
        public void ApplyFromState(
                int? windowStart, int? windowEnd,
                MaintenanceWindowFrequency frequency,
                MaintenanceWindowMonthlyMode monthlyMode,
                int? dayOfWeek, int? dayOfMonth,
                MaintenanceWindowMonthlyOrdinal? monthlyOrdinal) {
            int start = windowStart ?? 0;
            int end   = windowEnd   ?? 0;
            MaintenanceWindowStartCombo.SelectedIndex = start;
            MaintenanceWindowEndCombo.SelectedIndex   = end;
            bool anyTime = start == 0 && end == 0;
            MaintenanceWindowAnyTime.IsChecked = anyTime;

            MaintenanceWindowFrequencyCombo.SelectedIndex = (int)frequency;
            MaintenanceWindowMonthlyModeCombo.SelectedIndex = (int)monthlyMode;

            int dow = dayOfWeek ?? 0;
            if (dow >= 0 && dow <= 6) {
                MaintenanceWindowDayOfWeekCombo.SelectedIndex = dow;
            }

            if (dayOfMonth.HasValue) {
                if (dayOfMonth.Value == MaintenanceWindowDayOfMonthSentinel.LastDay) {
                    MaintenanceWindowDayOfMonthCombo.SelectedIndex = 28; // "Last day"
                } else if (dayOfMonth.Value >= 1 && dayOfMonth.Value <= 28) {
                    MaintenanceWindowDayOfMonthCombo.SelectedIndex = dayOfMonth.Value - 1;
                }
            }

            var ord = monthlyOrdinal ?? MaintenanceWindowMonthlyOrdinal.Third;
            MaintenanceWindowOrdinalCombo.SelectedIndex = Math.Max(0, Math.Min(4, (int)ord - 1));

            UpdateFrequencyDayVisibility();
        }

        /// <summary>
        /// Snapshot every cadence value and push it through to the monitor service in a single
        /// atomic IPC call. On success the result carries the captured values so the host can
        /// update its in-memory state without re-reading the combos.
        /// </summary>
        public async Task<MaintenanceWindowPersistResult> PersistAsync(MonitorClient monitorClient) {
            var req = new MaintenanceWindowConfigRequest {
                Start          = WindowStart,
                End            = WindowEnd,
                Frequency      = Frequency,
                MonthlyMode    = MonthlyMode,
                DayOfWeek      = DayOfWeekToPersist,
                DayOfMonth     = DayOfMonthToPersist,
                MonthlyOrdinal = OrdinalToPersist,
            };
            var result = new MaintenanceWindowPersistResult {
                StartHour      = req.Start.Value,
                EndHour        = req.End.Value,
                Frequency      = req.Frequency,
                MonthlyMode    = req.MonthlyMode,
                DayOfWeek      = req.DayOfWeek,
                DayOfMonth     = req.DayOfMonth,
                MonthlyOrdinal = req.MonthlyOrdinal,
            };

            SvcResponse r = await monitorClient.SetMaintenanceWindowAsync(req);
            if (r == null || r.Code != 0) {
                result.Success = false;
                result.FailedOperation = "SetMaintenanceWindowAsync";
                result.ErrorMessage = !string.IsNullOrEmpty(r?.Error) ? r.Error : "Could not save maintenance window settings.";
                return result;
            }
            result.Success = true;
            return result;
        }

        // ---------------------------------------------------------------------------------
        // Internal helpers + handlers
        // ---------------------------------------------------------------------------------

        private void PopulateHourCombos() {
            var hours = new List<string>();
            for (int h = 0; h < 24; h++) {
                hours.Add($"{h:D2}:00");
            }
            MaintenanceWindowStartCombo.ItemsSource = hours;
            MaintenanceWindowEndCombo.ItemsSource   = hours;
        }

        private void PopulateFrequencyAndDayCombos() {
            MaintenanceWindowFrequencyCombo.ItemsSource   = FrequencyLabels;
            MaintenanceWindowFrequencyCombo.SelectedIndex = 0;

            MaintenanceWindowDayOfWeekCombo.ItemsSource   = DayOfWeekLabels;
            MaintenanceWindowDayOfWeekCombo.SelectedIndex = 0;

            var domLabels = new List<string>();
            for (int d = 1; d <= 28; d++) {
                domLabels.Add(d.ToString());
            }
            domLabels.Add("Last day"); // sentinel; resolves to LastDay = 32 in DayOfMonthSelectedValue()
            MaintenanceWindowDayOfMonthCombo.ItemsSource   = domLabels;
            MaintenanceWindowDayOfMonthCombo.SelectedIndex = 0;

            MaintenanceWindowMonthlyModeCombo.ItemsSource   = MonthlyModeLabels;
            MaintenanceWindowMonthlyModeCombo.SelectedIndex = 0;

            MaintenanceWindowOrdinalCombo.ItemsSource   = OrdinalLabels;
            MaintenanceWindowOrdinalCombo.SelectedIndex = 2; // default Third (Patch Tuesday)
        }

        private void MaintenanceWindowFrequency_Changed(object sender, SelectionChangedEventArgs e) {
            UpdateFrequencyDayVisibility();
        }

        private void MaintenanceWindowMonthlyMode_Changed(object sender, SelectionChangedEventArgs e) {
            UpdateFrequencyDayVisibility();
        }

        private void UpdateFrequencyDayVisibility() {
            // Guard: WPF can fire SelectionChanged during InitializeComponent before fields are set.
            if (MaintenanceWindowFrequencyCombo == null) return;

            var freq = (MaintenanceWindowFrequency)Math.Max(0, MaintenanceWindowFrequencyCombo.SelectedIndex);
            bool weekly  = freq == MaintenanceWindowFrequency.Weekly;
            bool monthly = freq == MaintenanceWindowFrequency.Monthly;
            var monthlyMode = (MaintenanceWindowMonthlyMode)Math.Max(0, MaintenanceWindowMonthlyModeCombo.SelectedIndex);
            bool monthlyByDate    = monthly && monthlyMode == MaintenanceWindowMonthlyMode.ByDate;
            bool monthlyByWeekday = monthly && monthlyMode == MaintenanceWindowMonthlyMode.ByWeekday;

            MaintenanceWindowMonthlyModeLabel.Visibility = monthly ? Visibility.Visible : Visibility.Collapsed;
            MaintenanceWindowMonthlyModeCombo.Visibility = monthly ? Visibility.Visible : Visibility.Collapsed;

            // The DayOfWeek combo is shown in both Weekly and Monthly+ByWeekday, but its
            // leading "On" label is only used in Weekly -- the Monthly+ByWeekday row uses
            // the OrdinalLabel ("On the") as its leading text.
            bool showDow = weekly || monthlyByWeekday;
            MaintenanceWindowDayOfWeekLabel.Visibility = weekly  ? Visibility.Visible : Visibility.Collapsed;
            MaintenanceWindowDayOfWeekCombo.Visibility = showDow ? Visibility.Visible : Visibility.Collapsed;

            MaintenanceWindowOrdinalLabel.Visibility = monthlyByWeekday ? Visibility.Visible : Visibility.Collapsed;
            MaintenanceWindowOrdinalCombo.Visibility = monthlyByWeekday ? Visibility.Visible : Visibility.Collapsed;

            MaintenanceWindowDayOfMonthLabel.Visibility = monthlyByDate ? Visibility.Visible : Visibility.Collapsed;
            MaintenanceWindowDayOfMonthCombo.Visibility = monthlyByDate ? Visibility.Visible : Visibility.Collapsed;
        }

        // Maps the "Last day" combo entry (index 28) to LastDay sentinel; otherwise returns 1-28.
        private int DayOfMonthSelectedValue() {
            int idx = MaintenanceWindowDayOfMonthCombo.SelectedIndex;
            if (idx == 28) return MaintenanceWindowDayOfMonthSentinel.LastDay;
            return Math.Max(1, idx + 1);
        }

        private void MaintenanceWindowAnyTime_Changed(object sender, RoutedEventArgs e) {
            bool anyTime = MaintenanceWindowAnyTime.IsChecked == true;
            if (anyTime) {
                MaintenanceWindowStartCombo.SelectedIndex = 0;
                MaintenanceWindowEndCombo.SelectedIndex   = 0;
            }
            // Start/End enabling depends on both IsEditable and whether AnyTime is checked.
            MaintenanceWindowStartCombo.IsEnabled = _isEditable && !anyTime;
            MaintenanceWindowEndCombo.IsEnabled   = _isEditable && !anyTime;
        }

        private void ApplyEditableState() {
            double opacity = _isEditable ? 1.0 : 0.3;
            bool anyTime = MaintenanceWindowAnyTime.IsChecked == true;

            MaintenanceWindowHeading.Opacity = opacity;

            MaintenanceWindowAnyTime.IsEnabled = _isEditable;
            MaintenanceWindowAnyTime.Opacity   = opacity;

            MaintenanceWindowStartCombo.IsEnabled = _isEditable && !anyTime;
            MaintenanceWindowStartCombo.Opacity   = opacity;
            MaintenanceWindowEndCombo.IsEnabled   = _isEditable && !anyTime;
            MaintenanceWindowEndCombo.Opacity     = opacity;

            MaintenanceWindowFrequencyLabel.Opacity   = opacity;
            MaintenanceWindowFrequencyCombo.IsEnabled = _isEditable;
            MaintenanceWindowFrequencyCombo.Opacity   = opacity;

            MaintenanceWindowMonthlyModeLabel.Opacity   = opacity;
            MaintenanceWindowMonthlyModeCombo.IsEnabled = _isEditable;
            MaintenanceWindowMonthlyModeCombo.Opacity   = opacity;

            MaintenanceWindowDayOfWeekLabel.Opacity   = opacity;
            MaintenanceWindowDayOfWeekCombo.IsEnabled = _isEditable;
            MaintenanceWindowDayOfWeekCombo.Opacity   = opacity;

            MaintenanceWindowDayOfMonthLabel.Opacity   = opacity;
            MaintenanceWindowDayOfMonthCombo.IsEnabled = _isEditable;
            MaintenanceWindowDayOfMonthCombo.Opacity   = opacity;

            MaintenanceWindowOrdinalLabel.Opacity   = opacity;
            MaintenanceWindowOrdinalCombo.IsEnabled = _isEditable;
            MaintenanceWindowOrdinalCombo.Opacity   = opacity;
        }
    }
}
