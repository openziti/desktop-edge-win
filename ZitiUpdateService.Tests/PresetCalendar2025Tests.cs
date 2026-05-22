/*
    Copyright NetFoundry Inc.

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    https://www.apache.org/licenses/LICENSE-2.0
*/

using System;
using ZitiDesktopEdge.DataStructures;
using ZitiUpdateService.Utils;

namespace ZitiUpdateService.Tests {
    /// <summary>
    /// Full-calendar verification of every compliance preset across all 12 months of 2025.
    /// Each preset has one qualifying day per month; this pins those 72 dates so any drift
    /// between the documented recipe (POLICY-ADMIN-GUIDE.md) and the evaluator math fails
    /// loudly with a named test.
    ///
    /// Dates were computed by hand against the 2025 Gregorian calendar:
    ///   2025 first-day-of-month: Jan=Wed, Feb=Sat, Mar=Sat, Apr=Tue, May=Thu, Jun=Sun,
    ///                            Jul=Tue, Aug=Fri, Sep=Mon, Oct=Wed, Nov=Sat, Dec=Mon.
    ///
    /// Production presets live in `ZitiUpdateService/POLICY-ADMIN-GUIDE.md` "Recommended
    /// settings for regulated fleets" appendix and in the matching `Preset_*` snapshot
    /// tests in MaintenanceWindowEvaluatorTests.cs.
    /// </summary>
    [TestClass]
    public class PresetCalendar2025Tests {

        // CJIS: Monthly + ByWeekday + Third + Sunday. Twelve months of 2025.
        [DataTestMethod]
        [DataRow(2025,  1, 19)]
        [DataRow(2025,  2, 16)]
        [DataRow(2025,  3, 16)]
        [DataRow(2025,  4, 20)]
        [DataRow(2025,  5, 18)]
        [DataRow(2025,  6, 15)]
        [DataRow(2025,  7, 20)]
        [DataRow(2025,  8, 17)]
        [DataRow(2025,  9, 21)]
        [DataRow(2025, 10, 19)]
        [DataRow(2025, 11, 16)]
        [DataRow(2025, 12, 21)]
        public void Preset_CJIS_2025_ThirdSundayEachMonth(int year, int month, int expectedDay) {
            AssertThirdSundayMatches(year, month, expectedDay,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third,
                dayOfWeek: DayOfWeek.Sunday);
        }

        // DISA STIG: Monthly + ByWeekday + Second + Wednesday. Patch Tuesday + 1 day.
        [DataTestMethod]
        [DataRow(2025,  1,  8)]
        [DataRow(2025,  2, 12)]
        [DataRow(2025,  3, 12)]
        [DataRow(2025,  4,  9)]
        [DataRow(2025,  5, 14)]
        [DataRow(2025,  6, 11)]
        [DataRow(2025,  7,  9)]
        [DataRow(2025,  8, 13)]
        [DataRow(2025,  9, 10)]
        [DataRow(2025, 10,  8)]
        [DataRow(2025, 11, 12)]
        [DataRow(2025, 12, 10)]
        public void Preset_DisaStig_2025_SecondWednesdayEachMonth(int year, int month, int expectedDay) {
            AssertThirdSundayMatches(year, month, expectedDay,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Second,
                dayOfWeek: DayOfWeek.Wednesday);
        }

        // PCI-DSS: Monthly + ByDate + day 1. Twelve months of 2025 all qualify on day 1.
        [DataTestMethod]
        [DataRow(2025,  1, 1)]
        [DataRow(2025,  2, 1)]
        [DataRow(2025,  3, 1)]
        [DataRow(2025,  4, 1)]
        [DataRow(2025,  5, 1)]
        [DataRow(2025,  6, 1)]
        [DataRow(2025,  7, 1)]
        [DataRow(2025,  8, 1)]
        [DataRow(2025,  9, 1)]
        [DataRow(2025, 10, 1)]
        [DataRow(2025, 11, 1)]
        [DataRow(2025, 12, 1)]
        public void Preset_PciDss_2025_FirstOfMonth(int year, int month, int expectedDay) {
            // For ByDate, we use IsCalendarDayQualifying directly with dayOfMonth=1.
            DateTime target = new DateTime(year, month, expectedDay);
            DateTime offByOne = expectedDay < 28
                ? new DateTime(year, month, expectedDay + 1)
                : new DateTime(year, month, expectedDay - 1);
            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                target, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: 1, monthlyOrdinal: null),
                $"{target:yyyy-MM-dd} should qualify as PCI-DSS (1st of month)");
            Assert.IsFalse(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                offByOne, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: 1, monthlyOrdinal: null),
                $"{offByOne:yyyy-MM-dd} should NOT qualify -- only 1st of month does");
        }

        // NIST 800-53 / FedRAMP Moderate: Monthly + ByWeekday + Third + Tuesday.
        [DataTestMethod]
        [DataRow(2025,  1, 21)]
        [DataRow(2025,  2, 18)]
        [DataRow(2025,  3, 18)]
        [DataRow(2025,  4, 15)]
        [DataRow(2025,  5, 20)]
        [DataRow(2025,  6, 17)]
        [DataRow(2025,  7, 15)]
        [DataRow(2025,  8, 19)]
        [DataRow(2025,  9, 16)]
        [DataRow(2025, 10, 21)]
        [DataRow(2025, 11, 18)]
        [DataRow(2025, 12, 16)]
        public void Preset_NistFedrampModerate_2025_ThirdTuesdayEachMonth(int year, int month, int expectedDay) {
            AssertThirdSundayMatches(year, month, expectedDay,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third,
                dayOfWeek: DayOfWeek.Tuesday);
        }

        // NERC CIP: Monthly + ByWeekday + Last + Sunday. Spans 4-Sunday and 5-Sunday months.
        // Mar/Aug 2025 are 5-Sunday months (Last lands on the 30th/31st); the other months
        // have 4 Sundays and Last == Fourth.
        [DataTestMethod]
        [DataRow(2025,  1, 26)]   // 4-Sunday month
        [DataRow(2025,  2, 23)]
        [DataRow(2025,  3, 30)]   // 5-Sunday month -- Last=30
        [DataRow(2025,  4, 27)]
        [DataRow(2025,  5, 25)]
        [DataRow(2025,  6, 29)]
        [DataRow(2025,  7, 27)]
        [DataRow(2025,  8, 31)]   // 5-Sunday month -- Last=31
        [DataRow(2025,  9, 28)]
        [DataRow(2025, 10, 26)]
        [DataRow(2025, 11, 30)]
        [DataRow(2025, 12, 28)]
        public void Preset_NercCip_2025_LastSundayEachMonth(int year, int month, int expectedDay) {
            AssertThirdSundayMatches(year, month, expectedDay,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Last,
                dayOfWeek: DayOfWeek.Sunday);
        }

        // HITRUST: Monthly + ByWeekday + Third + Tuesday (same as NIST). Re-verified
        // explicitly so a future divergence in the recipe (e.g. moving HITRUST to a
        // different ordinal) gets caught by THIS test, not by failure-by-association.
        [DataTestMethod]
        [DataRow(2025,  1, 21)]
        [DataRow(2025,  2, 18)]
        [DataRow(2025,  3, 18)]
        [DataRow(2025,  4, 15)]
        [DataRow(2025,  5, 20)]
        [DataRow(2025,  6, 17)]
        [DataRow(2025,  7, 15)]
        [DataRow(2025,  8, 19)]
        [DataRow(2025,  9, 16)]
        [DataRow(2025, 10, 21)]
        [DataRow(2025, 11, 18)]
        [DataRow(2025, 12, 16)]
        public void Preset_Hitrust_2025_ThirdTuesdayEachMonth(int year, int month, int expectedDay) {
            AssertThirdSundayMatches(year, month, expectedDay,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third,
                dayOfWeek: DayOfWeek.Tuesday);
        }

        // ---------------------------------------------------------------------------------
        // Helper -- Asserts the expected day qualifies, and days +/- 1 (clamped) do not.
        // Named "AssertThirdSundayMatches" historically; reused across all ByWeekday presets.
        // ---------------------------------------------------------------------------------
        private static void AssertThirdSundayMatches(
                int year, int month, int expectedDay,
                MaintenanceWindowMonthlyOrdinal monthlyOrdinal,
                DayOfWeek dayOfWeek) {
            DateTime target = new DateTime(year, month, expectedDay);
            int lastDay = DateTime.DaysInMonth(year, month);
            DateTime dayBefore = expectedDay > 1 ? target.AddDays(-1) : target.AddDays(1);
            DateTime dayAfter  = expectedDay < lastDay ? target.AddDays(1) : target.AddDays(-1);

            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                target, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)dayOfWeek, dayOfMonth: null, monthlyOrdinal: monthlyOrdinal),
                $"{target:yyyy-MM-dd} ({target.DayOfWeek}) should qualify as {monthlyOrdinal} {dayOfWeek}");
            Assert.IsFalse(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                dayBefore, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)dayOfWeek, dayOfMonth: null, monthlyOrdinal: monthlyOrdinal),
                $"day before {target:yyyy-MM-dd} ({dayBefore:yyyy-MM-dd}) should not qualify");
            Assert.IsFalse(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                dayAfter, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)dayOfWeek, dayOfMonth: null, monthlyOrdinal: monthlyOrdinal),
                $"day after {target:yyyy-MM-dd} ({dayAfter:yyyy-MM-dd}) should not qualify");
        }
    }
}
