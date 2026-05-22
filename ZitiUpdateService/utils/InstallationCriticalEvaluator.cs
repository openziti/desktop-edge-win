/*
    Copyright NetFoundry Inc.

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    https://www.apache.org/licenses/LICENSE-2.0
*/

using System;

namespace ZitiUpdateService.Utils {
    /// <summary>
    /// Pure-function evaluator for the "is this release critical yet?" decision.
    /// The release-stream JSON ships <c>published_at</c> in UTC; the threshold from
    /// settings/policy is a <see cref="TimeSpan"/>. The comparison must be done in a
    /// timezone-consistent way -- without the UTC->local conversion, users in
    /// negative UTC offsets would see the critical threshold appear to be in the
    /// future and never auto-install.
    ///
    /// Extracted from UpdateService for unit testing without touching the registry
    /// or DateTime.Now.
    /// </summary>
    public static class InstallationCriticalEvaluator {
        /// <summary>
        /// True when the release published at <paramref name="publishUtc"/> has aged
        /// past the <paramref name="threshold"/> as of <paramref name="now"/>.
        /// </summary>
        /// <param name="now">Local "now" (typically <c>DateTime.Now</c>).</param>
        /// <param name="publishUtc">Release publish timestamp, UTC (from stream JSON).</param>
        /// <param name="threshold">Effective InstallationCritical TimeSpan (from policy/config).</param>
        public static bool IsCritical(DateTime now, DateTime publishUtc, TimeSpan threshold) {
            return now > publishUtc.ToLocalTime() + threshold;
        }
    }
}
