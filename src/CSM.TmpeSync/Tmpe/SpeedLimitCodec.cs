using System;
using CSM.TmpeSync.Net.Contracts.States;

namespace CSM.TmpeSync.Tmpe
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
            if (speedKmh <= DefaultTolerance)
                return Default();

            if (speedKmh >= UnlimitedKmh - 0.5f)
                return Unlimited();

            int kmhIndex;
            float kmhDiff;
            FindNearest(KmphPalette, speedKmh, out kmhIndex, out kmhDiff);

            var mphValue = speedKmh / KmPerMile;
            int mphIndex;
            float mphDiff;
            FindNearest(MphPalette, mphValue, out mphIndex, out mphDiff);
            var mphDiffKmh = mphDiff * KmPerMile;

            if (kmhDiff <= mphDiffKmh + 0.25f)
                return new SpeedLimitValue { Type = SpeedLimitValueType.KilometresPerHour, Index = (byte)kmhIndex };

            return new SpeedLimitValue { Type = SpeedLimitValueType.MilesPerHour, Index = (byte)mphIndex };
        }

        internal static float DecodeToKmh(SpeedLimitValue value)
        {
            if (value == null || value.Type == SpeedLimitValueType.Default)
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
            return value == null || value.Type == SpeedLimitValueType.Default;
        }

        internal static bool IsUnlimited(SpeedLimitValue value)
        {
            return value != null && value.Type == SpeedLimitValueType.Unlimited;
        }

        internal static SpeedLimitValue Default()
        {
            return new SpeedLimitValue { Type = SpeedLimitValueType.Default };
        }

        internal static SpeedLimitValue Unlimited()
        {
            return new SpeedLimitValue { Type = SpeedLimitValueType.Unlimited };
        }

        internal static string Describe(SpeedLimitValue value)
        {
            if (value == null)
                return "<null>";

            switch (value.Type)
            {
                case SpeedLimitValueType.Default:
                    return "Default";
                case SpeedLimitValueType.Unlimited:
                    return "Unlimited";
                case SpeedLimitValueType.KilometresPerHour:
                    return value.Index < KmphPalette.Length
                        ? $"{KmphPalette[value.Index]} km/h"
                        : $"km/h index {value.Index}";
                case SpeedLimitValueType.MilesPerHour:
                    return value.Index < MphPalette.Length
                        ? $"{MphPalette[value.Index]} mph"
                        : $"mph index {value.Index}";
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
