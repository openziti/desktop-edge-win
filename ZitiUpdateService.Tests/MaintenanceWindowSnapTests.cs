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
    /// Unit tests for SnapToMaintenanceWindow (forward-snap to the next qualifying
    /// install slot) and the underlying IsInWindow hour-bounds check.
    ///
    /// Replaces the manual clock-watching previously needed for test-run.md
    /// Test 2 ("install deferred outside window") and Test 3 ("clearing window
    /// fires the deferred install") expected-time arithmetic. The wiring
    /// (registry-write -> log line -> install action) is still verified in those
    /// manual tests; only the time math moves here.
    ///
    /// Production code: ZitiUpdateService\utils\MaintenanceWindowEvaluator.cs
    /// </summary>
    [TestClass]
    public class MaintenanceWindowSnapTests {

        // -------------------------------------------------------------------------------------
        // IsInWindow: hour-bounds check, including cross-midnight ranges.
        // -------------------------------------------------------------------------------------

        [DataTestMethod]
        // Same-day window 9:00-17:00 (inclusive start, exclusive end).
        [DataRow( 9,  9, 17,  true)]
        [DataRow(12,  9, 17,  true)]
        [DataRow(16,  9, 17,  true)]
        [DataRow(17,  9, 17, false)]  // end is exclusive
        [DataRow( 8,  9, 17, false)]
        [DataRow(18,  9, 17, false)]
        // Cross-midnight window 22:00-06:00.
        [DataRow(22, 22,  6,  true)]
        [DataRow(23, 22,  6,  true)]
        [DataRow( 0, 22,  6,  true)]
        [DataRow( 3, 22,  6,  true)]
        [DataRow( 5, 22,  6,  true)]
        [DataRow( 6, 22,  6, false)]  // end is exclusive
        [DataRow(21, 22,  6, false)]
        public void IsInWindow_BoundsCheck(int hour, int start, int end, bool expected) {
            Assert.AreEqual(expected, MaintenanceWindowEvaluator.IsInWindow(hour, start, end));
        }

        // -------------------------------------------------------------------------------------
        // SnapToMaintenanceWindow: returns unchanged when no window configured, or when
        // already inside a qualifying slot. Snaps forward otherwise.
        // -------------------------------------------------------------------------------------

        [TestMethod]
        public void Snap_NoWindow_ReturnsInputUnchanged() {
            DateTime dt = new DateTime(2026, 5, 14, 12, 0, 0);
            DateTime result = Snap(dt, windowStart: null, windowEnd: null);
            Assert.AreEqual(dt, result);
        }

        [TestMethod]
        public void Snap_AnyTimeWindow_StartEqualsEnd_ReturnsInputUnchanged() {
            DateTime dt = new DateTime(2026, 5, 14, 12, 0, 0);
            DateTime result = Snap(dt, windowStart: 0, windowEnd: 0);
            Assert.AreEqual(dt, result);
        }

        [TestMethod]
        public void Snap_AlreadyInsideWindowOnQualifyingDay_ReturnsInputUnchanged() {
            // Daily window 22:00-06:00. dt is at 23:00, inside the window.
            DateTime dt = new DateTime(2026, 5, 14, 23, 0, 0);
            DateTime result = Snap(dt, windowStart: 22, windowEnd: 6);
            Assert.AreEqual(dt, result);
        }

        [TestMethod]
        public void Snap_BeforeWindowSameDay_ReturnsThatDayWindowStart() {
            // Daily window 22:00-06:00. dt is at 12:00 -> snap to 22:00 same day.
            DateTime dt = new DateTime(2026, 5, 14, 12, 0, 0);
            DateTime result = Snap(dt, windowStart: 22, windowEnd: 6);
            Assert.AreEqual(new DateTime(2026, 5, 14, 22, 0, 0), result);
        }

        [TestMethod]
        public void Snap_AfterWindowSameDay_ReturnsNextDayWindowStart() {
            // Same-day window 9:00-17:00. dt is at 18:00 -> snap to next-day 9:00.
            DateTime dt = new DateTime(2026, 5, 14, 18, 0, 0);
            DateTime result = Snap(dt, windowStart: 9, windowEnd: 17);
            Assert.AreEqual(new DateTime(2026, 5, 15, 9, 0, 0), result);
        }

        [TestMethod]
        public void Snap_CrossMidnightWindow_Hour6Boundary_GoesToNextDay22() {
            // Daily window 22:00-06:00. dt is at 06:00 exactly (end is exclusive)
            // -> snap to same-day 22:00.
            DateTime dt = new DateTime(2026, 5, 14, 6, 0, 0);
            DateTime result = Snap(dt, windowStart: 22, windowEnd: 6);
            Assert.AreEqual(new DateTime(2026, 5, 14, 22, 0, 0), result);
        }

        // -------------------------------------------------------------------------------------
        // Cadence interaction: snap must skip non-qualifying days, not just non-qualifying hours.
        // -------------------------------------------------------------------------------------

        [TestMethod]
        public void Snap_Weekly_OnNonQualifyingDay_SkipsToNextQualifyingDay() {
            // Weekly Sunday window 22:00-06:00. Monday 2026-05-11 at noon ->
            // snap to next Sunday 2026-05-17 at 22:00.
            DateTime dt = new DateTime(2026, 5, 11, 12, 0, 0); // Monday
            DateTime result = Snap(dt,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Weekly,
                dayOfWeek: (int)DayOfWeek.Sunday);
            Assert.AreEqual(new DateTime(2026, 5, 17, 22, 0, 0), result);
            Assert.AreEqual(DayOfWeek.Sunday, result.DayOfWeek);
        }

        [TestMethod]
        public void Snap_WeeklyAnyTime_OnNonQualifyingDay_SkipsToNextQualifyingDayAtMidnight() {
            // Any-time (start==end) frees the hour but the weekly day cadence still applies.
            DateTime dt = new DateTime(2026, 5, 11, 12, 0, 0); // Monday
            DateTime result = Snap(dt,
                windowStart: 0, windowEnd: 0,
                frequency: MaintenanceWindowFrequency.Weekly,
                dayOfWeek: (int)DayOfWeek.Sunday);
            Assert.AreEqual(new DateTime(2026, 5, 17, 0, 0, 0), result);
            Assert.AreEqual(DayOfWeek.Sunday, result.DayOfWeek);
        }

        [TestMethod]
        public void Snap_WeeklyAnyTime_OnQualifyingDay_ReturnsInputUnchanged() {
            DateTime dt = new DateTime(2026, 5, 17, 14, 0, 0); // Sunday
            DateTime result = Snap(dt,
                windowStart: 0, windowEnd: 0,
                frequency: MaintenanceWindowFrequency.Weekly,
                dayOfWeek: (int)DayOfWeek.Sunday);
            Assert.AreEqual(dt, result);
        }

        [TestMethod]
        public void Snap_MonthlyByWeekday_ThirdTuesday_AfterCurrentMonthSlot_GoesToNextMonth() {
            // Third Tuesday of May 2026 is May 19. If dt is May 20 noon -> snap to next month's
            // Third Tuesday (June 16, 2026).
            // June 2026: First Tuesday = 2 -> Third = 16.
            DateTime dt = new DateTime(2026, 5, 20, 12, 0, 0);
            DateTime result = Snap(dt,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Tuesday,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third);
            Assert.AreEqual(new DateTime(2026, 6, 16, 22, 0, 0), result);
        }

        [TestMethod]
        public void Snap_MonthlyByDate_LastDaySentinel_FromMidMonth_GoesToLastOfMonth() {
            // ByDate + LastDay sentinel (32). May 14 noon -> snap to May 31 02:00.
            DateTime dt = new DateTime(2026, 5, 14, 12, 0, 0);
            DateTime result = Snap(dt,
                windowStart: 2, windowEnd: 4,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByDate,
                dayOfMonth: MaintenanceWindowDayOfMonthSentinel.LastDay);
            Assert.AreEqual(new DateTime(2026, 5, 31, 2, 0, 0), result);
        }

        [TestMethod]
        public void Snap_MonthlyByDate_FirstOfMonth_FromAfterFirst_GoesToNextMonth() {
            // ByDate=1, May 14 noon -> snap to June 1, 02:00.
            DateTime dt = new DateTime(2026, 5, 14, 12, 0, 0);
            DateTime result = Snap(dt,
                windowStart: 2, windowEnd: 4,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByDate,
                dayOfMonth: 1);
            Assert.AreEqual(new DateTime(2026, 6, 1, 2, 0, 0), result);
        }

        [TestMethod]
        public void Snap_MonthlyByWeekday_LastFriday_DistinguishesFromFourthFriday() {
            // Demonstrates the operational bug the "Last" ordinal exists to prevent:
            // in a 5-Friday month, "Fourth Friday" fires a week earlier than "Last Friday".
            //
            // Jan 2026: Fridays = 2, 9, 16, 23, 30. Fourth = 23, Last = 30.
            DateTime beforeBothSlots = new DateTime(2026, 1, 22, 12, 0, 0);

            DateTime fourthResult = Snap(beforeBothSlots,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Friday,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Fourth);
            Assert.AreEqual(23, fourthResult.Day);

            DateTime lastResult = Snap(beforeBothSlots,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Friday,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Last);
            Assert.AreEqual(30, lastResult.Day);
            Assert.AreNotEqual(fourthResult, lastResult);
        }

        // -------------------------------------------------------------------------------------
        // Helper
        // -------------------------------------------------------------------------------------

        private static DateTime Snap(
                DateTime dt,
                int? windowStart, int? windowEnd,
                MaintenanceWindowFrequency frequency = MaintenanceWindowFrequency.Daily,
                MaintenanceWindowMonthlyMode monthlyMode = MaintenanceWindowMonthlyMode.ByDate,
                int? dayOfWeek = null, int? dayOfMonth = null,
                MaintenanceWindowMonthlyOrdinal? monthlyOrdinal = null) {
            return MaintenanceWindowEvaluator.SnapToMaintenanceWindow(
                dt, windowStart, windowEnd, frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal);
        }
    }
}
