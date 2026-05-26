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
    /// Pins evaluator behaviour for misconfigured or non-happy-path cadence inputs:
    ///
    ///   1. Missing required field -> degrade to Daily (e.g. Monthly+ByDate with no
    ///      dayOfMonth). Prevents a "never patch" foot-gun.
    ///   2. Cross-field shadowing: fields irrelevant to the current cadence shape are
    ///      silently ignored, not honoured. (e.g. DayOfWeek when Frequency=Daily, or
    ///      DayOfMonth when MonthlyMode=ByWeekday.)
    ///   3. Pathological-config termination: SnapToMaintenanceWindow has a 1-year
    ///      ceiling that guarantees the forward walk terminates on impossible input.
    ///   4. IsInWindow(X, Y, Y) any-time semantics (start==end is unconditionally true).
    ///
    /// Production source: ZitiUpdateService/utils/MaintenanceWindowEvaluator.cs.
    /// </summary>
    [TestClass]
    public class MaintenanceWindowMisconfigurationTests {

        // ---------------------------------------------------------------------------------
        // 1. Misconfiguration -> Daily fallback
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public void MonthlyByDate_WithoutDayOfMonth_DegradesToDaily() {
            // Frequency=Monthly, MonthlyMode=ByDate, but dayOfMonth is null.
            // Expected: every calendar day qualifies (Daily behaviour) so installs can still
            // fire; combined with InstallationCritical this prevents a "never patch" foot-gun.
            DateTime dt = new DateTime(2026, 5, 14); // Thursday, mid-month -- not the 1st
            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                dt, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: null, monthlyOrdinal: null));
        }

        // ---------------------------------------------------------------------------------
        // 2. Explicit non-sentinel day-of-month coverage (the LastDay=32 sentinel was the only
        //    ByDate path previously tested; this pins ordinary integers too).
        // ---------------------------------------------------------------------------------

        [DataTestMethod]
        // Format: (year, month, dayInMonth, configuredDayOfMonth, expectedQualifies)
        // Verify dayOfMonth=N qualifies day N of the month and rejects N+/-1.
        [DataRow(2026, 5,  1,  1, true)]
        [DataRow(2026, 5,  2,  1, false)]
        [DataRow(2026, 5, 15, 15, true)]
        [DataRow(2026, 5, 14, 15, false)]
        [DataRow(2026, 5, 16, 15, false)]
        [DataRow(2026, 5, 28, 28, true)]
        [DataRow(2026, 5, 27, 28, false)]
        [DataRow(2026, 5, 29, 28, false)]
        // Across multiple months: the same dayOfMonth value qualifies day N of each month.
        [DataRow(2026, 1, 15, 15, true)]
        [DataRow(2026, 2, 15, 15, true)]
        [DataRow(2026, 6, 15, 15, true)]
        [DataRow(2026,12, 15, 15, true)]
        public void MonthlyByDate_ExplicitDayOfMonth_QualifiesTargetDayOnly(
                int year, int month, int dayInMonth, int configuredDayOfMonth, bool expected) {
            DateTime dt = new DateTime(year, month, dayInMonth);
            bool actual = MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                dt, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: configuredDayOfMonth, monthlyOrdinal: null);
            Assert.AreEqual(expected, actual);
        }

        // ---------------------------------------------------------------------------------
        // 3. Cross-field shadowing: irrelevant fields are silently ignored, not honoured.
        //    These shouldn't change behaviour, but if the implementation ever accidentally
        //    starts reading the wrong field for the cadence, these tests catch it.
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public void Daily_WithDayOfWeekSet_StillAlwaysQualifies() {
            // Daily cadence: dayOfWeek must be ignored. Pick a day that is NOT the configured
            // dayOfWeek to make the test meaningful.
            DateTime mondayDt = new DateTime(2026, 5, 11); // Monday
            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                mondayDt, MaintenanceWindowFrequency.Daily, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: (int)DayOfWeek.Tuesday,   // <-- shadowed; should be ignored
                dayOfMonth: 15,                       // <-- shadowed
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));  // <-- shadowed
        }

        [TestMethod]
        public void Weekly_WithDayOfMonthAndOrdinalSet_DayOfWeekStillDecides() {
            // Weekly cadence: only dayOfWeek matters. dayOfMonth and ordinal must be ignored.
            DateTime sunday = new DateTime(2026, 5, 17); // Sunday
            DateTime monday = new DateTime(2026, 5, 18); // Monday

            // Configured for Sunday; Sunday qualifies, Monday does not -- regardless of the
            // shadowed dayOfMonth/ordinal values.
            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                sunday, MaintenanceWindowFrequency.Weekly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Sunday,
                dayOfMonth: 1,                        // shadowed
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.First));  // shadowed
            Assert.IsFalse(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                monday, MaintenanceWindowFrequency.Weekly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Sunday,
                dayOfMonth: 18,                       // shadowed (matches today's date but Weekly ignores)
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));
        }

        [TestMethod]
        public void MonthlyByDate_WithDayOfWeekAndOrdinalSet_DayOfMonthStillDecides() {
            // Monthly+ByDate cadence: only dayOfMonth matters. dayOfWeek and ordinal are shadowed.
            DateTime firstOfMonth = new DateTime(2026, 5, 1);
            DateTime middleOfMonth = new DateTime(2026, 5, 15);

            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                firstOfMonth, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: (int)DayOfWeek.Tuesday,    // shadowed; May 1 2026 is actually Friday
                dayOfMonth: 1,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));  // shadowed
            Assert.IsFalse(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                middleOfMonth, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: (int)DayOfWeek.Friday,     // shadowed; May 15 IS a Friday but ByDate ignores
                dayOfMonth: 1,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));
        }

        [TestMethod]
        public void MonthlyByWeekday_WithDayOfMonthSet_OrdinalAndDowStillDecide() {
            // Monthly+ByWeekday: ordinal+dayOfWeek decide; dayOfMonth is shadowed.
            // Third Tuesday of May 2026 is May 19.
            DateTime thirdTuesday = new DateTime(2026, 5, 19);
            DateTime firstOfMonth = new DateTime(2026, 5, 1);

            Assert.IsTrue(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                thirdTuesday, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Tuesday,
                dayOfMonth: 99,                       // shadowed (nonsense value, should not matter)
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));
            Assert.IsFalse(MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                firstOfMonth, MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Tuesday,
                dayOfMonth: 1,                        // shadowed; matches today's date but ByWeekday ignores
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));
        }

        // ---------------------------------------------------------------------------------
        // 4. SnapToMaintenanceWindow 1-year ceiling cap. The forward-walk is bounded so that
        //    a pathological misconfig can't loop forever. When the cap is hit the input is
        //    returned unchanged (a higher-level Logger.Warn fires in the instance overload,
        //    but the math-side behaviour is just "give up and return dt").
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public void Snap_NoQualifyingDayWithinYear_ReturnsInputUnchanged() {
            // Construct a config that, IF the evaluator honored every field strictly, would
            // produce zero qualifying days: Monthly+ByDate, dayOfMonth=29, in a non-leap
            // February... except the search spans a year so it'd still find Mar 29 etc.
            // Truly-unreachable configs require the evaluator to honour an impossible value
            // (e.g. dayOfMonth=99). The evaluator currently doesn't validate inputs at this
            // layer, so a nonsense dayOfMonth=99 means no calendar day qualifies, and Snap
            // walks the full 366 days then returns dt unchanged.
            DateTime dt = new DateTime(2026, 5, 14, 12, 0, 0);
            DateTime result = MaintenanceWindowEvaluator.SnapToMaintenanceWindow(
                dt, windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: 99,      // unreachable -- no month has a 99th day
                monthlyOrdinal: null);
            Assert.AreEqual(dt, result, "Snap should give up after 1 year and return the input unchanged");
        }

        // ---------------------------------------------------------------------------------
        // 5. IsInWindow direct call with start == end. When start==end, the production code
        //    falls into the cross-midnight branch (`hour >= start || hour < end`), and since
        //    start==end that expression simplifies to `hour >= X || hour < X` which is
        //    ALWAYS TRUE for any X. This matches the "any time" semantics used by callers.
        //
        //    Pinning this here means SnapToMaintenanceWindow's start==end short-circuit
        //    (line 112 of the evaluator) and IsInWindow are in agreement: both treat
        //    start==end as "every hour qualifies".
        // ---------------------------------------------------------------------------------

        [DataTestMethod]
        [DataRow( 0)]
        [DataRow(12)]
        [DataRow(23)]
        public void IsInWindow_StartEqualsEnd_ReturnsTrueForAllHours(int hour) {
            // Documented contract: IsInWindow(_, X, X) is unconditionally TRUE -- the
            // cross-midnight branch's OR makes this trivially true for every hour.
            Assert.IsTrue(MaintenanceWindowEvaluator.IsInWindow(hour, 0, 0),
                $"IsInWindow({hour}, 0, 0) should be true (any-time semantics)");
            Assert.IsTrue(MaintenanceWindowEvaluator.IsInWindow(hour, 12, 12),
                $"IsInWindow({hour}, 12, 12) should be true (any-time semantics)");
            Assert.IsTrue(MaintenanceWindowEvaluator.IsInWindow(hour, 23, 23),
                $"IsInWindow({hour}, 23, 23) should be true (any-time semantics)");
        }
    }
}
