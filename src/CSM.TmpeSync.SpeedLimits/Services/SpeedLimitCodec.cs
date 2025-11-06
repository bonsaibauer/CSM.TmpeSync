using System;
using CSM.TmpeSync.Messages.States;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    internal static class SpeedLimitCodec
    {
        private static readonly float[] KmphPalette =
        {
            10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f, 130f, 140f
        };

        private static readonly float[] MphPalette =
        {
            5f, 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f
        };

        private const float GameUnitsToKmph = 50f;
        private const float GameUnitsToMph = 31.06f;
        private const float KmPerMile = GameUnitsToKmph / GameUnitsToMph;
        private const float UnlimitedKmh = GameUnitsToKmph * 20f; // SpeedValue.UNLIMITED
        private const float DefaultTolerance = 0.05f;

        internal static SpeedLimitValue Encode(float speedKmh)
        {
            var sanitized = Math.Max(0f, speedKmh);

            if (sanitized <= DefaultTolerance)
                return Default();

            if (sanitized >= UnlimitedKmh - 0.5f)
            {
                var unlimited = Unlimited();
                unlimited.RawSpeedKmh = sanitized;
                return unlimited;
            }

            int kmhIndex;
            float kmhDiff;
            FindNearest(KmphPalette, sanitized, out kmhIndex, out kmhDiff);

            var mphValue = sanitized / KmPerMile;
            int mphIndex;
            float mphDiff;
            FindNearest(MphPalette, mphValue, out mphIndex, out mphDiff);
            var mphDiffKmh = mphDiff * KmPerMile;

            if (kmhDiff <= mphDiffKmh + 0.25f)
            {
                return new SpeedLimitValue
                {
                    Type = SpeedLimitValueType.KilometresPerHour,
                    Index = (byte)kmhIndex,
                    RawSpeedKmh = sanitized
                };
            }

            return new SpeedLimitValue
            {
                Type = SpeedLimitValueType.MilesPerHour,
                Index = (byte)mphIndex,
                RawSpeedKmh = sanitized
            };
        }

        internal static SpeedLimitValue Encode(float speedKmh, float? defaultKmh, bool hasOverride)
        {
            return Encode(speedKmh, defaultKmh, hasOverride, false);
        }

        internal static SpeedLimitValue Encode(float speedKmh, float? defaultKmh, bool hasOverride, bool pending)
        {
            if (!hasOverride)
            {
                var value = Default();
                if (defaultKmh.HasValue && defaultKmh.Value > DefaultTolerance)
                    value.RawSpeedKmh = defaultKmh.Value;
                value.Pending = pending;
                return value;
            }

            if (defaultKmh.HasValue && Math.Abs(speedKmh - defaultKmh.Value) <= DefaultTolerance)
            {
                var value = Default();
                if (defaultKmh.Value > DefaultTolerance)
                    value.RawSpeedKmh = defaultKmh.Value;
                value.Pending = pending;
                return value;
            }

            var encoded = Encode(speedKmh);
            encoded.Pending = pending;
            return encoded;
        }

        internal static float DecodeToKmh(SpeedLimitValue value)
        {
            if (value == null)
                return 0f;

            if (value.RawSpeedKmh > DefaultTolerance)
                return value.RawSpeedKmh;

            if (value.Type == SpeedLimitValueType.Default)
                return 0f;

            switch (value.Type)
            {
                case SpeedLimitValueType.KilometresPerHour:
                    return value.Index < KmphPalette.Length ? KmphPalette[value.Index] : 0f;
                case SpeedLimitValueType.MilesPerHour:
                    return value.Index < MphPalette.Length ? MphPalette[value.Index] * KmPerMile : 0f;
                case SpeedLimitValueType.Unlimited:
                    return UnlimitedKmh;
                default:
                    return 0f;
            }
        }

        internal static float DecodeToGameUnits(SpeedLimitValue value)
        {
            return DecodeToKmh(value) / GameUnitsToKmph;
        }

        internal static bool IsDefault(SpeedLimitValue value)
        {
            return value == null || (value.Type == SpeedLimitValueType.Default && value.RawSpeedKmh <= DefaultTolerance);
        }

        internal static bool IsUnlimited(SpeedLimitValue value)
        {
            return value != null && value.Type == SpeedLimitValueType.Unlimited;
        }

        internal static SpeedLimitValue Default()
        {
            return new SpeedLimitValue { Type = SpeedLimitValueType.Default, RawSpeedKmh = 0f };
        }

        internal static SpeedLimitValue Unlimited()
        {
            return new SpeedLimitValue { Type = SpeedLimitValueType.Unlimited, RawSpeedKmh = UnlimitedKmh };
        }

        internal static string Describe(SpeedLimitValue value)
        {
            if (value == null)
                return "<null>";

            var pendingSuffix = value.Pending ? " (pending)" : string.Empty;

            switch (value.Type)
            {
                case SpeedLimitValueType.Default:
                    return (value.RawSpeedKmh > DefaultTolerance
                        ? $"{value.RawSpeedKmh:0.###} km/h (raw)"
                        : "Default") + pendingSuffix;
                case SpeedLimitValueType.Unlimited:
                    return (value.RawSpeedKmh > DefaultTolerance
                        ? $"Unlimited ({value.RawSpeedKmh:0.###} km/h raw)"
                        : "Unlimited") + pendingSuffix;
                case SpeedLimitValueType.KilometresPerHour:
                    return (value.Index < KmphPalette.Length
                        ? $"{KmphPalette[value.Index]} km/h"
                        : $"km/h index {value.Index}") + pendingSuffix;
                case SpeedLimitValueType.MilesPerHour:
                    return (value.Index < MphPalette.Length
                        ? $"{MphPalette[value.Index]} mph"
                        : $"mph index {value.Index}") + pendingSuffix;
                default:
                    return value.ToString();
            }
        }

        private static void FindNearest(float[] palette, float value, out int index, out float diff)
        {
            if (palette == null || palette.Length == 0)
            {
                index = 0;
                diff = float.MaxValue;
                return;
            }

            var bestIndex = 0;
            var bestDiff = float.MaxValue;

            for (var i = 0; i < palette.Length; i++)
            {
                var currentDiff = Math.Abs(palette[i] - value);
                if (currentDiff < bestDiff)
                {
                    bestDiff = currentDiff;
                    bestIndex = i;
                }
            }

            index = bestIndex;
            diff = bestDiff;
        }
    }
}
