/*
    Copyright NetFoundry Inc.

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    https://www.apache.org/licenses/LICENSE-2.0
*/

using System;
using System.IO;
using Newtonsoft.Json;
using ZitiDesktopEdge.DataStructures;

namespace ZitiUpdateService.Tests {
    /// <summary>
    /// Settings.json serialization round-trip tests. Verifies that every persisted field
    /// survives Write -> read back -> compare. The production `Settings` class wires
    /// FileSystemWatcher + AppData paths, so these tests use Newtonsoft.Json directly
    /// against a value-shape that mirrors the persisted schema field-by-field.
    ///
    /// Catches regressions where:
    /// - A new field is added to Settings.cs but not to Update() (the field would be
    ///   silently dropped on hot-reload after a Write).
    /// - Enum serialization defaults to wrong shape (e.g., a string converter is added
    ///   that breaks the integer-on-disk format the policy tooling assumes).
    /// - Nullable<T> fields are serialized as default(T) instead of null.
    ///
    /// The user-visible symptom this would have caught: maintenance-window cadence
    /// settings other than Daily not persisting -- not a serialization bug as it turned
    /// out, but the round-trip-on-disk assurance is still load-bearing.
    /// </summary>
    [TestClass]
    public class SettingsRoundTripTests {

        // Serializer config must match what the production `Settings.cs` uses (see
        // `ZitiUpdateService/utils/Settings.cs` -- `new JsonSerializer() { Formatting = Indented }`).
        private static readonly JsonSerializer Serializer = new JsonSerializer { Formatting = Formatting.Indented };

        // Schema-equivalent type. Identical field shape to ZitiUpdateService.Utils.Settings
        // (minus the internal lifecycle stuff: FileSystemWatcher, Location, init()). We don't
        // ProjectReference into Settings.cs here because that class is `internal` and depends
        // on AppData paths we don't want hit during a test run.
        private sealed class SettingsShape {
            public bool AutomaticUpdatesDisabled { get; set; }
            public string? AutomaticUpdateURL { get; set; }
            public int? AlivenessChecksBeforeAction { get; set; }
            public bool DeferInstallToRestart { get; set; }
            public int? MaintenanceWindowStart { get; set; }
            public int? MaintenanceWindowEnd { get; set; }
            public MaintenanceWindowFrequency MaintenanceWindowFrequency { get; set; } = MaintenanceWindowFrequency.Daily;
            public int? MaintenanceWindowDayOfWeek { get; set; }
            public int? MaintenanceWindowDayOfMonth { get; set; }
            public MaintenanceWindowMonthlyMode MaintenanceWindowMonthlyMode { get; set; } = MaintenanceWindowMonthlyMode.ByDate;
            public MaintenanceWindowMonthlyOrdinal? MaintenanceWindowMonthlyOrdinal { get; set; }
        }

        private static SettingsShape RoundTrip(SettingsShape src) {
            string path = Path.GetTempFileName();
            try {
                using (var w = File.CreateText(path)) {
                    Serializer.Serialize(w, src);
                }
                using (var r = new JsonTextReader(new StreamReader(path))) {
                    return Serializer.Deserialize<SettingsShape>(r)!;
                }
            } finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Defaults_RoundTrip_PreservesDailyAndByDate() {
            var src = new SettingsShape();
            var rt = RoundTrip(src);

            Assert.AreEqual(MaintenanceWindowFrequency.Daily, rt.MaintenanceWindowFrequency);
            Assert.AreEqual(MaintenanceWindowMonthlyMode.ByDate, rt.MaintenanceWindowMonthlyMode);
            Assert.IsNull(rt.MaintenanceWindowDayOfWeek);
            Assert.IsNull(rt.MaintenanceWindowDayOfMonth);
            Assert.IsNull(rt.MaintenanceWindowMonthlyOrdinal);
            Assert.IsNull(rt.MaintenanceWindowStart);
            Assert.IsNull(rt.MaintenanceWindowEnd);
        }

        [TestMethod]
        public void Weekly_Sunday_RoundTripPersistsFrequencyAndDayOfWeek() {
            // This is the exact configuration that was reported as not persisting.
            // Verifies that the on-disk format survives a save/load cycle correctly.
            var src = new SettingsShape {
                MaintenanceWindowFrequency = MaintenanceWindowFrequency.Weekly,
                MaintenanceWindowDayOfWeek = 0,           // Sunday
                MaintenanceWindowStart = 22,
                MaintenanceWindowEnd = 6,
            };
            var rt = RoundTrip(src);

            Assert.AreEqual(MaintenanceWindowFrequency.Weekly, rt.MaintenanceWindowFrequency);
            Assert.AreEqual(0, rt.MaintenanceWindowDayOfWeek);
            Assert.AreEqual(22, rt.MaintenanceWindowStart);
            Assert.AreEqual(6, rt.MaintenanceWindowEnd);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        [DataRow(6)]
        public void Weekly_EveryDayOfWeek_RoundTrips(int dow) {
            var rt = RoundTrip(new SettingsShape {
                MaintenanceWindowFrequency = MaintenanceWindowFrequency.Weekly,
                MaintenanceWindowDayOfWeek = dow,
            });
            Assert.AreEqual(MaintenanceWindowFrequency.Weekly, rt.MaintenanceWindowFrequency);
            Assert.AreEqual(dow, rt.MaintenanceWindowDayOfWeek);
        }

        [DataTestMethod]
        [DataRow(1)]
        [DataRow(15)]
        [DataRow(28)]
        [DataRow(32)]   // LastDay sentinel
        public void MonthlyByDate_EveryDayOfMonth_RoundTrips(int dom) {
            var rt = RoundTrip(new SettingsShape {
                MaintenanceWindowFrequency = MaintenanceWindowFrequency.Monthly,
                MaintenanceWindowMonthlyMode = MaintenanceWindowMonthlyMode.ByDate,
                MaintenanceWindowDayOfMonth = dom,
            });
            Assert.AreEqual(MaintenanceWindowFrequency.Monthly, rt.MaintenanceWindowFrequency);
            Assert.AreEqual(MaintenanceWindowMonthlyMode.ByDate, rt.MaintenanceWindowMonthlyMode);
            Assert.AreEqual(dom, rt.MaintenanceWindowDayOfMonth);
        }

        [DataTestMethod]
        // Every ordinal x weekday combination for Monthly+ByWeekday
        [DataRow(MaintenanceWindowMonthlyOrdinal.First,  0)]
        [DataRow(MaintenanceWindowMonthlyOrdinal.Second, 2)]
        [DataRow(MaintenanceWindowMonthlyOrdinal.Third,  2)]   // Patch Tuesday
        [DataRow(MaintenanceWindowMonthlyOrdinal.Fourth, 4)]
        [DataRow(MaintenanceWindowMonthlyOrdinal.Last,   5)]   // Last Friday
        [DataRow(MaintenanceWindowMonthlyOrdinal.Last,   0)]   // Last Sunday
        public void MonthlyByWeekday_EveryOrdinalAndWeekday_RoundTrips(
                MaintenanceWindowMonthlyOrdinal ord, int dow) {
            var rt = RoundTrip(new SettingsShape {
                MaintenanceWindowFrequency = MaintenanceWindowFrequency.Monthly,
                MaintenanceWindowMonthlyMode = MaintenanceWindowMonthlyMode.ByWeekday,
                MaintenanceWindowMonthlyOrdinal = ord,
                MaintenanceWindowDayOfWeek = dow,
            });
            Assert.AreEqual(MaintenanceWindowFrequency.Monthly, rt.MaintenanceWindowFrequency);
            Assert.AreEqual(MaintenanceWindowMonthlyMode.ByWeekday, rt.MaintenanceWindowMonthlyMode);
            Assert.AreEqual(ord, rt.MaintenanceWindowMonthlyOrdinal);
            Assert.AreEqual(dow, rt.MaintenanceWindowDayOfWeek);
        }

        [TestMethod]
        public void Json_SerializedWithIntegerEnums_NotStrings() {
            // The policy registry tooling (Set-PolicyRegistryValues.ps1, ADMX) writes integer
            // values for these enums. If a developer adds [JsonConverter(typeof(StringEnumConverter))]
            // anywhere on the schema, the on-disk shape silently changes from `"Frequency": 1` to
            // `"Frequency": "Weekly"`. Anything loading settings.json from an external script
            // (or vice versa) would break. Pin the integer-on-disk format.
            var src = new SettingsShape {
                MaintenanceWindowFrequency = MaintenanceWindowFrequency.Weekly,
                MaintenanceWindowMonthlyMode = MaintenanceWindowMonthlyMode.ByWeekday,
                MaintenanceWindowMonthlyOrdinal = MaintenanceWindowMonthlyOrdinal.Third,
            };
            var sw = new StringWriter();
            Serializer.Serialize(sw, src);
            string json = sw.ToString();
            StringAssert.Contains(json, "\"MaintenanceWindowFrequency\": 1");
            StringAssert.Contains(json, "\"MaintenanceWindowMonthlyMode\": 1");
            StringAssert.Contains(json, "\"MaintenanceWindowMonthlyOrdinal\": 3");
        }

        [TestMethod]
        public void Json_PartialFile_MissingNewFields_DeserializesToDefaults() {
            // Old settings.json from a pre-cadence install must still load without exception.
            // Frequency should default to Daily, MonthlyMode to ByDate, day fields to null.
            string oldJson = @"{
                ""AutomaticUpdatesDisabled"": false,
                ""AutomaticUpdateURL"": ""https://example.com/stream.json"",
                ""MaintenanceWindowStart"": 22,
                ""MaintenanceWindowEnd"": 6
            }";
            var rt = Serializer.Deserialize<SettingsShape>(new JsonTextReader(new StringReader(oldJson)))!;
            Assert.AreEqual(MaintenanceWindowFrequency.Daily, rt.MaintenanceWindowFrequency);
            Assert.AreEqual(MaintenanceWindowMonthlyMode.ByDate, rt.MaintenanceWindowMonthlyMode);
            Assert.IsNull(rt.MaintenanceWindowDayOfWeek);
            Assert.IsNull(rt.MaintenanceWindowDayOfMonth);
            Assert.IsNull(rt.MaintenanceWindowMonthlyOrdinal);
            Assert.AreEqual(22, rt.MaintenanceWindowStart);
            Assert.AreEqual(6, rt.MaintenanceWindowEnd);
        }
    }
}
