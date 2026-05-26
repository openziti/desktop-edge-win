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
    /// Unit tests for the pure cadence math used by the maintenance-window evaluator.
    /// Keeps the bulk of test coverage out of the integration-test path, which would
    /// otherwise require setting the system clock on a VM (breaks CRL fetch, AD,
    /// service log timestamps, DST handling) once per test case.
    ///
    /// The matching integration smoke tests live in ZitiUpdateService\windows\gpo\VERIFICATION.md
    /// and only verify the registry-write -> log-line + UI-state wiring, not the math.
    /// </summary>
    [TestClass]
    public class MaintenanceWindowEvaluatorTests {

        // -------------------------------------------------------------------------------------
        // ResolveNthWeekdayOfMonth: generic math coverage.
        //
        // Dates verified by hand against the 2024-2026 calendar. Patch Tuesday reference:
        // 2nd Tuesday of each month. https://learn.microsoft.com/en-us/windows/release-health/
        // -------------------------------------------------------------------------------------

        [DataTestMethod]
        // First / Second / Third / Fourth in a 4-Tuesday month (Feb 2026: Tue = 3, 10, 17, 24)
        [DataRow(2026,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   3)]
        [DataRow(2026,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 10)]
        [DataRow(2026,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  17)]
        [DataRow(2026,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        [DataRow(2026,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Last,   24)]
        // 5-Tuesday month (Dec 2026: Tue = 1, 8, 15, 22, 29). Fourth != Last.
        [DataRow(2026, 12, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]
        [DataRow(2026, 12, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Last,   29)]
        // 5-Friday month (Jan 2026: Fri = 2, 9, 16, 23, 30). Demonstrates the Last/Fourth gap.
        [DataRow(2026,  1, DayOfWeek.Friday,  MaintenanceWindowMonthlyOrdinal.First,   2)]
        [DataRow(2026,  1, DayOfWeek.Friday,  MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        [DataRow(2026,  1, DayOfWeek.Friday,  MaintenanceWindowMonthlyOrdinal.Last,   30)]
        // Sunday (PSAP overnight slot). Jan 2026: Sun = 4, 11, 18, 25. Patch Tuesday: 2nd Tue = 13.
        [DataRow(2026,  1, DayOfWeek.Sunday,  MaintenanceWindowMonthlyOrdinal.First,   4)]
        [DataRow(2026,  1, DayOfWeek.Sunday,  MaintenanceWindowMonthlyOrdinal.Third,  18)]
        [DataRow(2026,  1, DayOfWeek.Sunday,  MaintenanceWindowMonthlyOrdinal.Last,   25)]
        // Wednesday (DISA STIG slot, Patch Tuesday + 1). Jan 2026: Wed = 7, 14, 21, 28.
        [DataRow(2026,  1, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Second, 14)]
        // Leap-year February (Feb 2024). Last day = 29 (Thursday). Last Sunday = 25, Last Friday = 23.
        [DataRow(2024,  2, DayOfWeek.Thursday, MaintenanceWindowMonthlyOrdinal.Last,   29)]
        [DataRow(2024,  2, DayOfWeek.Sunday,   MaintenanceWindowMonthlyOrdinal.Last,   25)]
        [DataRow(2024,  2, DayOfWeek.Friday,   MaintenanceWindowMonthlyOrdinal.Last,   23)]
        // Non-leap February (Feb 2026: Feb 1 is Sunday). Last day = 28.
        [DataRow(2026,  2, DayOfWeek.Saturday, MaintenanceWindowMonthlyOrdinal.Last,   28)]
        // Month where day 1 IS the target weekday (Mar 2026: Mar 1 is Sunday). First Sunday must be day 1.
        [DataRow(2026,  3, DayOfWeek.Sunday,   MaintenanceWindowMonthlyOrdinal.First,   1)]
        // ------------------------------------------------------------------------------------
        // Exhaustive: Ordinal=Last for every (month, dayOfWeek) across all 12 months of 2024
        // (leap year). Verified by hand against the 2024 calendar. 84 rows.
        // ------------------------------------------------------------------------------------
        // 2024 Jan: 1st=Mon, 31 days
        [DataRow(2024,  1, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  1, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  1, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  1, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 31)]
        [DataRow(2024,  1, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  1, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  1, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 27)]
        // 2024 Feb: 1st=Thu, 29 days (leap)
        [DataRow(2024,  2, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  2, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  2, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024,  2, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 28)]
        // Thursday/Sunday/Friday already covered above; Saturday:
        [DataRow(2024,  2, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 24)]
        // 2024 Mar: 1st=Fri, 31 days
        [DataRow(2024,  3, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 31)]
        [DataRow(2024,  3, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  3, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  3, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024,  3, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  3, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  3, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 30)]
        // 2024 Apr: 1st=Mon, 30 days
        [DataRow(2024,  4, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  4, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  4, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  4, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 24)]
        [DataRow(2024,  4, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  4, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  4, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 27)]
        // 2024 May: 1st=Wed, 31 days
        [DataRow(2024,  5, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  5, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024,  5, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  5, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  5, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  5, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 31)]
        [DataRow(2024,  5, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 25)]
        // 2024 Jun: 1st=Sat, 30 days
        [DataRow(2024,  6, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  6, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 24)]
        [DataRow(2024,  6, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  6, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  6, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024,  6, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  6, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 29)]
        // 2024 Jul: 1st=Mon, 31 days
        [DataRow(2024,  7, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  7, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  7, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  7, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 31)]
        [DataRow(2024,  7, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  7, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  7, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 27)]
        // 2024 Aug: 1st=Thu, 31 days
        [DataRow(2024,  8, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  8, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  8, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024,  8, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024,  8, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  8, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  8, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 31)]
        // 2024 Sep: 1st=Sun, 30 days
        [DataRow(2024,  9, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024,  9, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024,  9, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 24)]
        [DataRow(2024,  9, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024,  9, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024,  9, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024,  9, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 28)]
        // 2024 Oct: 1st=Tue, 31 days
        [DataRow(2024, 10, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024, 10, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024, 10, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024, 10, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024, 10, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 31)]
        [DataRow(2024, 10, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024, 10, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 26)]
        // 2024 Nov: 1st=Fri, 30 days
        [DataRow(2024, 11, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 24)]
        [DataRow(2024, 11, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024, 11, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024, 11, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024, 11, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 28)]
        [DataRow(2024, 11, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024, 11, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 30)]
        // 2024 Dec: 1st=Sun, 31 days
        [DataRow(2024, 12, DayOfWeek.Sunday,    MaintenanceWindowMonthlyOrdinal.Last, 29)]
        [DataRow(2024, 12, DayOfWeek.Monday,    MaintenanceWindowMonthlyOrdinal.Last, 30)]
        [DataRow(2024, 12, DayOfWeek.Tuesday,   MaintenanceWindowMonthlyOrdinal.Last, 31)]
        [DataRow(2024, 12, DayOfWeek.Wednesday, MaintenanceWindowMonthlyOrdinal.Last, 25)]
        [DataRow(2024, 12, DayOfWeek.Thursday,  MaintenanceWindowMonthlyOrdinal.Last, 26)]
        [DataRow(2024, 12, DayOfWeek.Friday,    MaintenanceWindowMonthlyOrdinal.Last, 27)]
        [DataRow(2024, 12, DayOfWeek.Saturday,  MaintenanceWindowMonthlyOrdinal.Last, 28)]
        // ------------------------------------------------------------------------------------
        // Exhaustive: Tuesday across all 5 ordinals, all 12 months of 2024. 60 rows.
        // Patch-Tuesday slot lives here; Last/Fourth divergence pinned per month.
        // ------------------------------------------------------------------------------------
        // 2024 Jan Tuesdays: 2,9,16,23,30 (5-Tuesday)
        [DataRow(2024,  1, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   2)]
        [DataRow(2024,  1, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second,  9)]
        [DataRow(2024,  1, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  16)]
        [DataRow(2024,  1, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        // Jan Last=30 already above
        // 2024 Feb Tuesdays: 6,13,20,27 (4-Tuesday; Fourth == Last)
        [DataRow(2024,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   6)]
        [DataRow(2024,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 13)]
        [DataRow(2024,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  20)]
        [DataRow(2024,  2, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 27)]
        // Feb Last covered above; check Last collapses to 27 in 4-Tue month
        // 2024 Mar Tuesdays: 5,12,19,26
        [DataRow(2024,  3, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   5)]
        [DataRow(2024,  3, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 12)]
        [DataRow(2024,  3, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  19)]
        [DataRow(2024,  3, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 26)]
        // 2024 Apr Tuesdays: 2,9,16,23,30 (5-Tuesday)
        [DataRow(2024,  4, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   2)]
        [DataRow(2024,  4, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second,  9)]
        [DataRow(2024,  4, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  16)]
        [DataRow(2024,  4, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        // 2024 May Tuesdays: 7,14,21,28
        [DataRow(2024,  5, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   7)]
        [DataRow(2024,  5, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 14)]
        [DataRow(2024,  5, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  21)]
        [DataRow(2024,  5, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 28)]
        // 2024 Jun Tuesdays: 4,11,18,25
        [DataRow(2024,  6, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   4)]
        [DataRow(2024,  6, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 11)]
        [DataRow(2024,  6, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  18)]
        [DataRow(2024,  6, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 25)]
        // 2024 Jul Tuesdays: 2,9,16,23,30 (5-Tuesday)
        [DataRow(2024,  7, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   2)]
        [DataRow(2024,  7, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second,  9)]
        [DataRow(2024,  7, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  16)]
        [DataRow(2024,  7, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        // 2024 Aug Tuesdays: 6,13,20,27
        [DataRow(2024,  8, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   6)]
        [DataRow(2024,  8, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 13)]
        [DataRow(2024,  8, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  20)]
        [DataRow(2024,  8, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 27)]
        // 2024 Sep Tuesdays: 3,10,17,24
        [DataRow(2024,  9, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   3)]
        [DataRow(2024,  9, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 10)]
        [DataRow(2024,  9, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  17)]
        [DataRow(2024,  9, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        // 2024 Oct Tuesdays: 1,8,15,22,29 (5-Tuesday)
        [DataRow(2024, 10, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   1)]
        [DataRow(2024, 10, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second,  8)]
        [DataRow(2024, 10, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  15)]
        [DataRow(2024, 10, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]
        // 2024 Nov Tuesdays: 5,12,19,26
        [DataRow(2024, 11, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   5)]
        [DataRow(2024, 11, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 12)]
        [DataRow(2024, 11, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  19)]
        [DataRow(2024, 11, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 26)]
        // 2024 Dec Tuesdays: 3,10,17,24,31 (5-Tuesday)
        [DataRow(2024, 12, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.First,   3)]
        [DataRow(2024, 12, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Second, 10)]
        [DataRow(2024, 12, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Third,  17)]
        [DataRow(2024, 12, DayOfWeek.Tuesday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        // ------------------------------------------------------------------------------------
        // 5-Friday months 2024-2026: Fourth vs Last must diverge (the "Last != Fourth" property
        // exists to prevent the documented SCCM bug where 4th Friday fires a week early).
        // ------------------------------------------------------------------------------------
        [DataRow(2024,  3, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]  // 5th Fri=29 (above)
        [DataRow(2024,  5, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]  // 5th Fri=31 (above)
        [DataRow(2024,  8, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]  // 5th Fri=30 (above)
        [DataRow(2024, 11, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]  // 5th Fri=29 (above)
        // 2025 Jan Fridays: 3,10,17,24,31
        [DataRow(2025,  1, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        [DataRow(2025,  1, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   31)]
        // 2025 May Fridays: 2,9,16,23,30
        [DataRow(2025,  5, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        [DataRow(2025,  5, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   30)]
        // 2025 Aug Fridays: 1,8,15,22,29
        [DataRow(2025,  8, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]
        [DataRow(2025,  8, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   29)]
        // 2025 Oct Fridays: 3,10,17,24,31
        [DataRow(2025, 10, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        [DataRow(2025, 10, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   31)]
        // 2026 May Fridays: 1,8,15,22,29
        [DataRow(2026,  5, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]
        [DataRow(2026,  5, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   29)]
        // 2026 Jul Fridays: 3,10,17,24,31
        [DataRow(2026,  7, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        [DataRow(2026,  7, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   31)]
        // 2026 Oct Fridays: 2,9,16,23,30
        [DataRow(2026, 10, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        [DataRow(2026, 10, DayOfWeek.Friday, MaintenanceWindowMonthlyOrdinal.Last,   30)]
        // ------------------------------------------------------------------------------------
        // 5-Sunday months 2024-2026 (NERC CIP "Last Sunday" slot). Fourth vs Last divergence.
        // ------------------------------------------------------------------------------------
        [DataRow(2024,  3, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]  // 5th Sun=31
        [DataRow(2024,  6, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]  // 5th Sun=30
        [DataRow(2024,  9, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]  // 5th Sun=29
        [DataRow(2024, 12, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]  // 5th Sun=29
        // 2025 Mar Sundays: 2,9,16,23,30
        [DataRow(2025,  3, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        [DataRow(2025,  3, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   30)]
        // 2025 Jun Sundays: 1,8,15,22,29
        [DataRow(2025,  6, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]
        [DataRow(2025,  6, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   29)]
        // 2025 Aug Sundays: 3,10,17,24,31
        [DataRow(2025,  8, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        [DataRow(2025,  8, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   31)]
        // 2025 Nov Sundays: 2,9,16,23,30
        [DataRow(2025, 11, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        [DataRow(2025, 11, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   30)]
        // 2026 Mar Sundays: 1,8,15,22,29 (covered above)
        // 2026 May Sundays: 3,10,17,24,31
        [DataRow(2026,  5, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 24)]
        [DataRow(2026,  5, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   31)]
        // 2026 Aug Sundays: 2,9,16,23,30
        [DataRow(2026,  8, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 23)]
        [DataRow(2026,  8, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   30)]
        // 2026 Nov Sundays: 1,8,15,22,29
        [DataRow(2026, 11, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Fourth, 22)]
        [DataRow(2026, 11, DayOfWeek.Sunday, MaintenanceWindowMonthlyOrdinal.Last,   29)]
        // ------------------------------------------------------------------------------------
        // Feb leap-vs-non-leap last-weekday confirmations (Sat/Sun in Feb 2024 leap + 2025).
        // ------------------------------------------------------------------------------------
        [DataRow(2025,  2, DayOfWeek.Saturday, MaintenanceWindowMonthlyOrdinal.Last, 22)]
        [DataRow(2025,  2, DayOfWeek.Sunday,   MaintenanceWindowMonthlyOrdinal.Last, 23)]
        public void ResolveNthWeekdayOfMonth_ReturnsExpectedDay(
                int year, int month, DayOfWeek dow, MaintenanceWindowMonthlyOrdinal ordinal, int expectedDay) {
            DateTime result = MaintenanceWindowEvaluator.ResolveNthWeekdayOfMonth(year, month, dow, ordinal);
            Assert.AreEqual(year,         result.Year);
            Assert.AreEqual(month,        result.Month);
            Assert.AreEqual(expectedDay,  result.Day);
            Assert.AreEqual(dow,          result.DayOfWeek);
        }

        // -------------------------------------------------------------------------------------
        // IsCalendarDayQualifying: feature behavior including the "misconfigured -> Daily" fallback.
        // -------------------------------------------------------------------------------------

        [TestMethod]
        public void Daily_AlwaysQualifies() {
            // Pick a date that is NOT any common slot to prove Daily doesn't accidentally restrict
            DateTime dt = new DateTime(2026, 3, 12); // Thursday
            Assert.IsTrue(Q(dt, MaintenanceWindowFrequency.Daily));
        }

        [TestMethod]
        public void Weekly_QualifiesOnlyOnConfiguredDayOfWeek() {
            // Sunday = 0; Mar 15 2026 is Sunday, Mar 16 is Monday
            Assert.IsTrue (Q(new DateTime(2026, 3, 15), MaintenanceWindowFrequency.Weekly, dayOfWeek: 0));
            Assert.IsFalse(Q(new DateTime(2026, 3, 16), MaintenanceWindowFrequency.Weekly, dayOfWeek: 0));
        }

        [TestMethod]
        public void Weekly_WithoutDayOfWeek_DegradesToDaily() {
            // Misconfigured weekly: any date qualifies. Prevents "no updates forever" footgun.
            Assert.IsTrue(Q(new DateTime(2026, 6, 4), MaintenanceWindowFrequency.Weekly, dayOfWeek: null));
        }

        [TestMethod]
        public void MonthlyByDate_LastDaySentinel_ResolvesPerMonth() {
            // 32 == LastDay. Feb 2026 last = 28; Apr 2026 last = 30; Dec 2026 last = 31.
            Assert.IsTrue (Q(new DateTime(2026, 2, 28), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
            Assert.IsTrue (Q(new DateTime(2026, 4, 30), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
            Assert.IsTrue (Q(new DateTime(2026,12, 31), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
            Assert.IsFalse(Q(new DateTime(2026, 4, 29), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
        }

        [TestMethod]
        public void MonthlyByDate_LastDaySentinel_LeapFebruary() {
            // Feb 2024 last = 29 (leap); Feb 2025 last = 28.
            Assert.IsTrue (Q(new DateTime(2024, 2, 29), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
            Assert.IsFalse(Q(new DateTime(2024, 2, 28), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
            Assert.IsTrue (Q(new DateTime(2025, 2, 28), MaintenanceWindowFrequency.Monthly, monthlyMode: MaintenanceWindowMonthlyMode.ByDate, dayOfMonth: 32));
        }

        [TestMethod]
        public void MonthlyByWeekday_MisconfiguredWithoutOrdinalOrDow_DegradesToDaily() {
            // Missing ordinal -> Daily
            Assert.IsTrue(Q(new DateTime(2026, 5, 12), MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday, dayOfWeek: 2, monthlyOrdinal: null));
            // Missing dayOfWeek -> Daily
            Assert.IsTrue(Q(new DateTime(2026, 5, 12), MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday, dayOfWeek: null,
                monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third));
        }

        // -------------------------------------------------------------------------------------
        // Preset snapshots: each named test pins the documented compliance recipe so that
        // edits to POLICY-ADMIN-GUIDE.md and edits to the evaluator can't silently diverge.
        // Source for each preset: ZitiUpdateService\POLICY-ADMIN-GUIDE.md
        // ("Recommended settings for regulated fleets" appendix).
        //
        // SLA citations (the InstallationCritical numeric bound, not exercised here but linked
        // for cross-reference):
        //   CJIS:     FBI CJIS Security Policy v5.9, section 5.7.1 (30 d critical).
        //   DISA:     DISA General Purpose Operating System STIG, Cat I patch SLA (21 d).
        //   PCI:      PCI-DSS 4.0, Requirement 6.3.3 (1 month for CVSS >=7).
        //   NIST:     NIST SP 800-53 Rev 5, SI-2 + FedRAMP Moderate baseline (30 d).
        //   NERC:     NERC CIP-007-6 R2.3 (35 d for applicable patches).
        //   HITRUST:  HITRUST CSF v11 Control 10.m (30/90 d).
        // -------------------------------------------------------------------------------------

        // CJIS preset (POLICY-ADMIN-GUIDE.md "CJIS-aligned"): Monthly + ByWeekday + Third + Sunday.
        // Targets the PSAP overnight slot common in law-enforcement dispatch deployments.
        [TestMethod]
        public void Preset_CJIS_QualifiesOnlyOnThirdSunday() {
            // Jan 2026: Third Sunday = 18; Feb 2026: Third Sunday = 15; Mar 2026: Third Sunday = 15.
            AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: 0, monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third,
                expected: new[] {
                    new DateTime(2026, 1, 18),
                    new DateTime(2026, 2, 15),
                    new DateTime(2026, 3, 15),
                });
        }

        // DISA STIG preset (POLICY-ADMIN-GUIDE.md "DISA STIG / DoD-aligned"):
        // Monthly + ByWeekday + Second + Wednesday. Patch Tuesday + 1 day cushion for WSUS sync.
        [TestMethod]
        public void Preset_DisaStig_QualifiesOnlySecondWednesday() {
            // Jan 2026: Second Wed = 14; Feb 2026: Second Wed = 11; Mar 2026: Second Wed = 11.
            AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: 3, monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Second,
                expected: new[] {
                    new DateTime(2026, 1, 14),
                    new DateTime(2026, 2, 11),
                    new DateTime(2026, 3, 11),
                });
        }

        // PCI-DSS preset (POLICY-ADMIN-GUIDE.md "PCI-DSS 4.0 aligned"):
        // Monthly + ByDate + day 1. Financial / retail change calendars divorced from MS cadence.
        [TestMethod]
        public void Preset_PciDss_QualifiesOnlyOnFirstOfMonth() {
            AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByDate,
                dayOfMonth: 1,
                expected: new[] {
                    new DateTime(2026, 1, 1),
                    new DateTime(2026, 2, 1),
                    new DateTime(2026, 3, 1),
                });
        }

        // NIST 800-53 / FedRAMP Moderate preset (POLICY-ADMIN-GUIDE.md "NIST 800-53 / FedRAMP Moderate"):
        // Monthly + ByWeekday + Third + Tuesday. The Patch Tuesday slot itself.
        [TestMethod]
        public void Preset_NistFedrampModerate_QualifiesOnlyOnThirdTuesday() {
            // Jan 2026: Third Tue = 20; Feb 2026: Third Tue = 17; Mar 2026: Third Tue = 17.
            // (Patch Tuesday is 2nd Tue; this preset is one week after for soak time.)
            AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: 2, monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third,
                expected: new[] {
                    new DateTime(2026, 1, 20),
                    new DateTime(2026, 2, 17),
                    new DateTime(2026, 3, 17),
                });
        }

        // NERC CIP preset (POLICY-ADMIN-GUIDE.md "NERC CIP-007 R2.3"):
        // Monthly + ByWeekday + Last + Sunday. Electric-utility operations center overnight slot.
        // Specifically exercises the Last ordinal across a 4-Sunday and a 5-Sunday month.
        [TestMethod]
        public void Preset_NercCip_QualifiesOnlyOnLastSunday() {
            // Mar 2026: Sundays = 1, 8, 15, 22, 29 -> Last = 29 (5-Sunday month).
            // Apr 2026: Sundays = 5, 12, 19, 26    -> Last = 26 (4-Sunday month).
            // May 2026: Sundays = 3, 10, 17, 24, 31 -> Last = 31 (5-Sunday month).
            AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: 0, monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Last,
                expected: new[] {
                    new DateTime(2026, 3, 29),
                    new DateTime(2026, 4, 26),
                    new DateTime(2026, 5, 31),
                });
            // Also assert that "Fourth Sunday" in a 5-Sunday month would NOT match Last:
            Assert.IsFalse(Q(new DateTime(2026, 3, 22), MaintenanceWindowFrequency.Monthly,
                monthlyMode: MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: 0, monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Last));
        }

        // HITRUST preset (POLICY-ADMIN-GUIDE.md "HITRUST / HIPAA-aligned"):
        // Monthly + ByWeekday + Third + Tuesday. Same shape as NIST/FedRAMP -- both target
        // mainstream Third-Tuesday cadence with a 30-day backstop.
        [TestMethod]
        public void Preset_Hitrust_QualifiesOnlyOnThirdTuesday() {
            AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency.Monthly, MaintenanceWindowMonthlyMode.ByWeekday,
                dayOfWeek: 2, monthlyOrdinal: MaintenanceWindowMonthlyOrdinal.Third,
                expected: new[] {
                    new DateTime(2026, 1, 20),
                    new DateTime(2026, 2, 17),
                    new DateTime(2026, 3, 17),
                });
        }

        // -------------------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------------------

        // Thin wrapper around the evaluator with named parameters so each test reads as
        // documentation rather than positional-argument soup.
        private static bool Q(
                DateTime dt,
                MaintenanceWindowFrequency frequency,
                MaintenanceWindowMonthlyMode monthlyMode = MaintenanceWindowMonthlyMode.ByDate,
                int? dayOfWeek = null,
                int? dayOfMonth = null,
                MaintenanceWindowMonthlyOrdinal? monthlyOrdinal = null) {
            return MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                dt, frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal);
        }

        // Asserts each expected date qualifies AND that dates +/- 1 day do not.
        // Catches both false-negative (configured slot is rejected) and false-positive
        // (off-by-one in the resolver) drift in a single shot.
        private static void AssertCadenceQualifiesOn(
                MaintenanceWindowFrequency frequency,
                MaintenanceWindowMonthlyMode monthlyMode,
                DateTime[] expected,
                int? dayOfWeek = null,
                int? dayOfMonth = null,
                MaintenanceWindowMonthlyOrdinal? monthlyOrdinal = null) {
            foreach (DateTime hit in expected) {
                Assert.IsTrue(
                    MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                        hit, frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal),
                    $"Expected {hit:yyyy-MM-dd} ({hit.DayOfWeek}) to qualify, but it did not.");
                Assert.IsFalse(
                    MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                        hit.AddDays(-1), frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal),
                    $"Day BEFORE {hit:yyyy-MM-dd} should not qualify.");
                Assert.IsFalse(
                    MaintenanceWindowEvaluator.IsCalendarDayQualifying(
                        hit.AddDays(1), frequency, monthlyMode, dayOfWeek, dayOfMonth, monthlyOrdinal),
                    $"Day AFTER {hit:yyyy-MM-dd} should not qualify.");
            }
        }
    }
}
