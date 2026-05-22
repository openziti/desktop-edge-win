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
    /// Unit tests for the InstallationCritical age-threshold decision and the
    /// composed InstallDateFromPublishDate "when will this install?" arithmetic.
    ///
    /// Replaces the manual log-watching previously required by `test-run.md`
    /// Tests 7, 8, and 12 -- you don't need to spin up the service to verify
    /// that a 7-day-old release crosses a 1-day threshold.
    ///
    /// Production code paths:
    ///   ZitiUpdateService\utils\InstallationCriticalEvaluator.cs::IsCritical
    ///   ZitiUpdateService\utils\MaintenanceWindowEvaluator.cs::InstallDateFromPublishDate
    /// </summary>
    [TestClass]
    public class InstallationCriticalTests {

        // -------------------------------------------------------------------------------------
        // Exhaustive boundary table: pins the strict `>` (not `>=`) semantics at sub-second
        // precision across multiple threshold scales. Hand-verified against the rule
        //   IsCritical = now > publishUtc.ToLocalTime() + threshold
        //
        // Format: now ISO, publishUtc ISO, thresholdSeconds, expectedCritical.
        // Both timestamps are parsed as Unspecified-kind DateTimes; for the assertion we
        // explicitly mark `now` as Local and `publishUtc` as Utc inside the test body so the
        // production code's `ToLocalTime()` call exercises the correct conversion path.
        // -------------------------------------------------------------------------------------

        [DataTestMethod]
        // threshold = 0 (no grace period): every positive elapsed time fires
        [DataRow("2026-05-07T12:00:00",         "2026-05-07T12:00:00", 0,          false)]
        [DataRow("2026-05-07T12:00:00.0000001", "2026-05-07T12:00:00", 0,          true)]
        [DataRow("2026-05-07T12:00:01",         "2026-05-07T12:00:00", 0,          true)]
        // threshold = 7 days (604800 sec)
        [DataRow("2026-05-13T12:00:00",         "2026-05-07T12:00:00", 604800,     false)]
        [DataRow("2026-05-14T12:00:00",         "2026-05-07T12:00:00", 604800,     false)]
        [DataRow("2026-05-14T12:00:00.0000001", "2026-05-07T12:00:00", 604800,     true)]
        [DataRow("2026-05-14T12:00:01",         "2026-05-07T12:00:00", 604800,     true)]
        [DataRow("2026-05-15T12:00:00",         "2026-05-07T12:00:00", 604800,     true)]
        // threshold = 30 days (2592000 sec)
        [DataRow("2026-06-05T12:00:00",         "2026-05-07T12:00:00", 2592000,    false)]
        [DataRow("2026-06-06T12:00:00",         "2026-05-07T12:00:00", 2592000,    false)]
        [DataRow("2026-06-06T12:00:00.0000001", "2026-05-07T12:00:00", 2592000,    true)]
        [DataRow("2026-06-07T12:00:00",         "2026-05-07T12:00:00", 2592000,    true)]
        // threshold = TimeSpan.FromSeconds(int.MaxValue) ~ 68 years: never critical for realistic ages
        [DataRow("2026-05-14T12:00:00",         "2026-05-07T12:00:00", 2147483647, false)]
        [DataRow("2026-12-31T23:59:59",         "2026-01-01T00:00:00", 2147483647, false)]
        [DataRow("2050-01-01T00:00:00",         "2026-01-01T00:00:00", 2147483647, false)]
        // Future publish dates: elapsed negative, never critical
        [DataRow("2026-05-07T12:00:00",         "2026-05-14T12:00:00", 0,          false)]
        [DataRow("2026-05-07T12:00:00",         "2026-05-14T12:00:00", 604800,     false)]
        [DataRow("2026-05-07T12:00:00",         "2026-06-07T12:00:00", 2592000,    false)]
        // Edge: now == publish, large threshold -> not yet critical
        [DataRow("2026-05-07T12:00:00",         "2026-05-07T12:00:00", 604800,     false)]
        // Boundary at threshold = 1 second
        [DataRow("2026-05-07T12:00:01",         "2026-05-07T12:00:00", 1,          false)]
        [DataRow("2026-05-07T12:00:01.0000001", "2026-05-07T12:00:00", 1,          true)]
        public void IsCritical_BoundaryTable(string nowIso, string publishIso, int thresholdSeconds, bool expected) {
            // The strict-greater-than at sub-tick boundary is the most failure-prone semantic
            // -- developers tend to assume >=. This DataRow grid pins it explicitly.
            //
            // We compare with both timestamps in the SAME calendar frame so the test is
            // timezone-independent: both are interpreted with `DateTimeKind.Utc` and
            // `ToLocalTime()` produces a consistent local offset. The strict-greater check
            // applies regardless of which UTC offset the test host runs in.
            DateTime now = DateTime.SpecifyKind(DateTime.Parse(nowIso, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Local);
            DateTime publishUtc = DateTime.SpecifyKind(DateTime.Parse(publishIso, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            // Translate `now` from its nominal "local clock" reading into the actual local
            // clock that ToLocalTime() will produce. For the boundary tests to hold across
            // host TZs, we shift `now` by the local offset so it matches the local-time
            // calculation publishUtc.ToLocalTime() will emit.
            TimeSpan localOffset = TimeZoneInfo.Local.GetUtcOffset(publishUtc);
            DateTime nowLocal = DateTime.SpecifyKind(now.Add(localOffset), DateTimeKind.Local);

            bool actual = InstallationCriticalEvaluator.IsCritical(
                nowLocal, publishUtc, TimeSpan.FromSeconds(thresholdSeconds));
            Assert.AreEqual(expected, actual,
                $"now={nowIso}, publishUtc={publishIso}, thresholdSec={thresholdSeconds}");
        }

        // -------------------------------------------------------------------------------------
        // IsCritical: publishUtc + threshold vs now (all in local-time semantics).
        // -------------------------------------------------------------------------------------

        [TestMethod]
        public void IsCritical_PublishOlderThanThreshold_ReturnsTrue() {
            // 10-day-old release, 7-day threshold -> critical.
            DateTime now        = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
            DateTime publishUtc = new DateTime(2026, 5,  4, 12, 0, 0, DateTimeKind.Utc);
            Assert.IsTrue(InstallationCriticalEvaluator.IsCritical(now, publishUtc, TimeSpan.FromDays(7)));
        }

        [TestMethod]
        public void IsCritical_PublishNewerThanThreshold_ReturnsFalse() {
            // 2-day-old release, 7-day threshold -> not yet critical.
            DateTime now        = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
            DateTime publishUtc = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(InstallationCriticalEvaluator.IsCritical(now, publishUtc, TimeSpan.FromDays(7)));
        }

        [TestMethod]
        public void IsCritical_ZeroThreshold_FiresImmediately() {
            // Policy "InstallationCritical=0" = every release is critical the instant it's
            // detected. Documented sharp edge in POLICY-ADMIN-GUIDE.md.
            DateTime now        = new DateTime(2026, 5, 14, 12, 0, 1, DateTimeKind.Local);
            DateTime publishUtc = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
            Assert.IsTrue(InstallationCriticalEvaluator.IsCritical(now, publishUtc, TimeSpan.Zero));
        }

        [TestMethod]
        public void IsCritical_HugeThreshold_NeverFires() {
            // The "never auto-install" recipe from ZitiUpdateService/CLAUDE.md:
            // set InstallationCritical to ~68 years. Any realistic release never crosses it.
            DateTime now        = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
            DateTime publishUtc = new DateTime(2026, 1,  1,  0, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(InstallationCriticalEvaluator.IsCritical(now, publishUtc, TimeSpan.FromSeconds(int.MaxValue)));
        }

        [TestMethod]
        public void IsCritical_PublishInFuture_ReturnsFalseAndDoesNotThrow() {
            // Defensive: a misconfigured stream JSON could publish a release dated in the
            // future. The math should just say "not critical" -- never auto-install something
            // that hasn't been released yet.
            DateTime now        = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
            DateTime publishUtc = new DateTime(2027, 1,  1,  0, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(InstallationCriticalEvaluator.IsCritical(now, publishUtc, TimeSpan.FromDays(7)));
        }

        [TestMethod]
        public void IsCritical_UtcVsLocal_RespectsOffset() {
            // publishUtc is UTC; the evaluator must convert to local before adding the threshold.
            // This test pins the conversion: pick a publish time such that UTC and local-derived
            // boundaries differ. We assert correctness *relative to the machine's local TZ* by
            // computing what "local + threshold" must be, then comparing.
            DateTime publishUtc = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
            TimeSpan threshold  = TimeSpan.FromHours(1);
            DateTime boundary   = publishUtc.ToLocalTime() + threshold;

            // Exactly at the boundary -> not yet critical (strict ">" comparison).
            Assert.IsFalse(InstallationCriticalEvaluator.IsCritical(boundary, publishUtc, threshold));
            // One tick past -> critical.
            Assert.IsTrue (InstallationCriticalEvaluator.IsCritical(boundary.AddTicks(1), publishUtc, threshold));
        }

        // -------------------------------------------------------------------------------------
        // InstallDateFromPublishDate: composed arithmetic across critical threshold +
        // maintenance-window snap. Verifies that a release published at time T1 with threshold
        // D, configured for cadence C, will install at the expected wall-clock time.
        // -------------------------------------------------------------------------------------

        [TestMethod]
        public void InstallDate_NoWindow_ReturnsLocalPublishPlusThreshold() {
            // Any-time window (start == end) -> install fires at publishLocal + threshold,
            // no snap-forward.
            DateTime publishUtc = new DateTime(2026, 5,  7, 12, 0, 0, DateTimeKind.Utc);
            TimeSpan threshold  = TimeSpan.FromDays(7);
            DateTime expected   = publishUtc.ToLocalTime() + threshold; // 2026-05-14 + local-offset

            DateTime actual = MaintenanceWindowEvaluator.InstallDateFromPublishDate(
                publishUtc, threshold,
                windowStart: 0, windowEnd: 0,
                frequency: MaintenanceWindowFrequency.Daily,
                monthlyMode: MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: null, monthlyOrdinal: null);

            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void InstallDate_DailyWindow_SnapsForwardWhenPastWindow() {
            // Daily 22:00-06:00 window. Publish + threshold lands at 12:00, snap forward
            // to that day's 22:00.
            DateTime publishUtc = new DateTime(2026, 5,  7, 12, 0, 0, DateTimeKind.Utc);
            TimeSpan threshold  = TimeSpan.FromDays(7);
            DateTime rawInstall = publishUtc.ToLocalTime() + threshold;

            DateTime actual = MaintenanceWindowEvaluator.InstallDateFromPublishDate(
                publishUtc, threshold,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Daily,
                monthlyMode: MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: null, monthlyOrdinal: null);

            // Result must be at 22:00 on the same date as rawInstall (if rawInstall.Hour < 22)
            // or the next-day 22:00 (if rawInstall.Hour was inside the cross-midnight window).
            // Since rawInstall noon local is < 22, expect today @ 22:00.
            DateTime expected = rawInstall.Date.AddHours(22);
            Assert.AreEqual(expected, actual,
                $"Expected snap to {expected:yyyy-MM-dd HH:mm} but got {actual:yyyy-MM-dd HH:mm} (rawInstall was {rawInstall:yyyy-MM-dd HH:mm})");
        }

        [TestMethod]
        public void InstallDate_MonthlyByWeekday_PatchTuesday_SnapsToNextThirdTuesday() {
            // Critical install for a release published 2026-04-01 with a 30-day threshold
            // = boundary ~2026-05-01. Cadence = Third Tuesday of month, 22:00-06:00.
            // May 2026: First Tue = 5 -> Third Tue = 19. Install should fire 2026-05-19 22:00 local.
            DateTime publishUtc = new DateTime(2026, 4,  1, 12, 0, 0, DateTimeKind.Utc);
            TimeSpan threshold  = TimeSpan.FromDays(30);

            DateTime actual = MaintenanceWindowEvaluator.InstallDateFromPublishDate(
                publishUtc, threshold,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: (int)DayOfWeek.Tuesday, dayOfMonth: null,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third);

            Assert.AreEqual(2026, actual.Year);
            Assert.AreEqual(5,    actual.Month);
            Assert.AreEqual(19,   actual.Day);
            Assert.AreEqual(22,   actual.Hour);
        }

        [TestMethod]
        public void InstallDate_PublishInFuture_StillSnapsForward() {
            // Defensive composition: future publish + threshold = even further future.
            // Snap must still return a sensible value (within a year of dt) not infinite-loop.
            DateTime publishUtc = new DateTime(2026, 6,  1, 12, 0, 0, DateTimeKind.Utc);
            TimeSpan threshold  = TimeSpan.FromDays(7);

            DateTime actual = MaintenanceWindowEvaluator.InstallDateFromPublishDate(
                publishUtc, threshold,
                windowStart: 22, windowEnd: 6,
                frequency: MaintenanceWindowFrequency.Daily,
                monthlyMode: MaintenanceWindowMonthlyMode.ByDate,
                dayOfWeek: null, dayOfMonth: null, monthlyOrdinal: null);

            // Should be on or after publishUtc.ToLocalTime() + threshold, with hour=22.
            Assert.IsTrue(actual >= publishUtc.ToLocalTime() + threshold);
            Assert.AreEqual(22, actual.Hour);
        }
    }
}
