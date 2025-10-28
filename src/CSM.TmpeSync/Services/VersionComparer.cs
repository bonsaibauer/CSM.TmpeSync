using System;
using System.Collections.Generic;

namespace CSM.TmpeSync.Services
{
    internal enum VersionMatchStatus
    {
        Unknown,
        Match,
        AllowedLegacy,
        OlderThanExpected,
        NewerThanExpected,
        ExpectedUnspecified
    }

    internal readonly struct VersionComparisonResult
    {
        internal VersionComparisonResult(
            VersionMatchStatus status,
            Version detected,
            Version expected,
            Version legacyMatch = null)
        {
            Status = status;
            Detected = detected;
            Expected = expected;
            LegacyMatch = legacyMatch;
        }

        internal VersionMatchStatus Status { get; }
        internal Version Detected { get; }
        internal Version Expected { get; }
        internal Version LegacyMatch { get; }
    }

    internal static class VersionComparer
    {
        internal static VersionComparisonResult Compare(
            Version detected,
            Version expected,
            IReadOnlyCollection<Version> allowedLegacy)
        {
            if (expected == null)
                return new VersionComparisonResult(VersionMatchStatus.ExpectedUnspecified, detected, expected);

            if (detected == null)
                return new VersionComparisonResult(VersionMatchStatus.Unknown, detected, expected);

            if (detected.Equals(expected))
                return new VersionComparisonResult(VersionMatchStatus.Match, detected, expected);

            if (allowedLegacy != null)
            {
                foreach (var legacy in allowedLegacy)
                {
                    if (legacy != null && legacy.Equals(detected))
                        return new VersionComparisonResult(VersionMatchStatus.AllowedLegacy, detected, expected, legacy);
                }
            }

            var comparison = detected.CompareTo(expected);
            if (comparison < 0)
                return new VersionComparisonResult(VersionMatchStatus.OlderThanExpected, detected, expected);

            if (comparison > 0)
                return new VersionComparisonResult(VersionMatchStatus.NewerThanExpected, detected, expected);

            return new VersionComparisonResult(VersionMatchStatus.Match, detected, expected);
        }
    }
}
