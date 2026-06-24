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
using ZitiDesktopEdge.DataStructures;

namespace ZitiUpdateService.Utils {
    /// <summary>
    /// Pure-function evaluator for the maintenance-window cadence. Extracted from
    /// UpdateService so it can be unit-tested without spinning up the service or
    /// the policy registry. The instance methods on UpdateService delegate here
    /// after gathering the effective policy values from PolicySettings.
    /// </summary>
    public static class MaintenanceWindowEvaluator {
        /// <summary>
        /// True if <paramref name="dt"/>'s calendar day satisfies the cadence.
        /// Daily always qualifies. Weekly requires dayOfWeek match. Monthly
        /// delegates to monthlyMode: ByDate compares dayOfMonth (LastDay sentinel
        /// -> actual last day); ByWeekday resolves (ordinal, dayOfWeek) to a
        /// concrete date for dt's month and compares.
        ///
        /// Misconfigured states (e.g. Weekly with no dayOfWeek) degrade to "Daily"
        /// rather than blocking installs forever -- the InstallationCritical
        /// backstop still applies but operators expect updates to keep flowing.
        /// </summary>
        public static bool IsCalendarDayQualifying(
            DateTime dt,
            MaintenanceWindowFrequency frequency,
            MaintenanceWindowMonthlyMode monthlyMode,
            int? dayOfWeek,
            int? dayOfMonth,
            MaintenanceWindowMonthlyOrdinal? monthlyOrdinal) {
            switch (frequency) {
                case MaintenanceWindowFrequency.Weekly:
                    if (!dayOfWeek.HasValue) return true;
                    return (int)dt.DayOfWeek == dayOfWeek.Value;
                case MaintenanceWindowFrequency.Monthly:
                    if (monthlyMode == MaintenanceWindowMonthlyMode.ByWeekday) {
                        if (!monthlyOrdinal.HasValue || !dayOfWeek.HasValue) return true;
                        DateTime target = ResolveNthWeekdayOfMonth(dt.Year, dt.Month, (DayOfWeek)dayOfWeek.Value, monthlyOrdinal.Value);
                        return dt.Date == target.Date;
                    }
                    if (!dayOfMonth.HasValue) return true;
                    int targetDay = dayOfMonth.Value == MaintenanceWindowDayOfMonthSentinel.LastDay
                        ? DateTime.DaysInMonth(dt.Year, dt.Month)
                        : dayOfMonth.Value;
                    return dt.Day == targetDay;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns the concrete date in (year, month) that matches (ordinal, dow).
        /// "Last" walks back from the final day of the month; First-Fourth count
        /// forward from day 1. Ordinals 1-4 always exist; "Last" handles
        /// 4-vs-5-weekday months automatically.
        /// </summary>
        public static DateTime ResolveNthWeekdayOfMonth(int year, int month, DayOfWeek dow, MaintenanceWindowMonthlyOrdinal ordinal) {
            if (ordinal == MaintenanceWindowMonthlyOrdinal.Last) {
                int lastDayNum = DateTime.DaysInMonth(year, month);
                DateTime lastDay = new DateTime(year, month, lastDayNum);
                int diff = ((int)lastDay.DayOfWeek - (int)dow + 7) % 7;
                return lastDay.AddDays(-diff);
            }
            DateTime first = new DateTime(year, month, 1);
            int forwardDiff = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(forwardDiff + 7 * ((int)ordinal - 1));
        }

        /// <summary>
        /// True if <paramref name="hour"/> falls inside the [windowStart, windowEnd) range,
        /// supporting windows that cross midnight (windowEnd &lt; windowStart, e.g. 22:00-06:00).
        /// Both bounds are hour-of-day 0-23.
        /// </summary>
        public static bool IsInWindow(int hour, int windowStart, int windowEnd) {
            if (windowStart < windowEnd) {
                return hour >= windowStart && hour < windowEnd;
            }
            return hour >= windowStart || hour < windowEnd;
        }

        /// <summary>
        /// Snaps <paramref name="dt"/> forward to the next opening of the maintenance window.
        /// Returns <paramref name="dt"/> unchanged when no window is configured or when the
        /// time already qualifies. Walks day-by-day until a qualifying calendar day is found,
        /// then aligns the hour to <paramref name="windowStart"/>. Capped at one year forward
        /// to guarantee termination even with a pathological config; if no qualifying day is
        /// found inside the cap the input is returned unchanged.
        ///
        /// An "any time" window (windowStart == windowEnd) still honors the Weekly/Monthly
        /// day cadence and lands at 00:00 of the next qualifying day.
        /// </summary>
        public static DateTime SnapToMaintenanceWindow(
                DateTime dt,
                int? windowStart, int? windowEnd,
                MaintenanceWindowFrequency frequency,
                MaintenanceWindowMonthlyMode monthlyMode,
                int? dayOfWeek, int? dayOfMonth,
                MaintenanceWindowMonthlyOrdinal? monthlyOrdinal) {
            if (!windowStart.HasValue || !windowEnd.HasValue) return dt;

            bool anyTime = windowStart.Value == windowEnd.Value;
            bool dayQualifies = IsCalendarDayQualifying(dt, frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal);
            if (dayQualifies && (anyTime || IsInWindow(dt.Hour, windowStart.Value, windowEnd.Value))) return dt;

            int snapHour = anyTime ? 0 : windowStart.Value;
            DateTime candidate = dt.Date.AddHours(snapHour);
            if (candidate <= dt) candidate = candidate.AddDays(1);

            DateTime ceiling = dt.AddYears(1);
            while (candidate <= ceiling) {
                if (IsCalendarDayQualifying(candidate, frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal)) return candidate;
                candidate = candidate.Date.AddDays(1).AddHours(snapHour);
            }
            return dt;
        }

        /// <summary>
        /// Computes when a release published at <paramref name="publishUtc"/> would
        /// auto-install once it crosses the InstallationCritical threshold. Adds the
        /// threshold to the local-time conversion of the publish date, then snaps that
        /// forward to the next qualifying maintenance window.
        /// </summary>
        public static DateTime InstallDateFromPublishDate(
                DateTime publishUtc,
                TimeSpan criticalThreshold,
                int? windowStart, int? windowEnd,
                MaintenanceWindowFrequency frequency,
                MaintenanceWindowMonthlyMode monthlyMode,
                int? dayOfWeek, int? dayOfMonth,
                MaintenanceWindowMonthlyOrdinal? monthlyOrdinal) {
            DateTime raw = publishUtc.ToLocalTime() + criticalThreshold;
            return SnapToMaintenanceWindow(raw, windowStart, windowEnd,
                frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal);
        }
    }
}
