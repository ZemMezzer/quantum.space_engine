using System;
using SpaceEngine.Runtime.Data.Galaxy;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    internal static class GalaxyShapeUtility
    {
        public static double3 ToShapeLocalPosition(
            in GalaxyData galaxy,
            double3 galaxyLocalPositionLightYears)
        {
            var cos = Math.Cos(-galaxy.RotationRadians);
            var sin = Math.Sin(-galaxy.RotationRadians);

            return new double3(
                galaxyLocalPositionLightYears.x * cos -
                galaxyLocalPositionLightYears.z * sin,
                galaxyLocalPositionLightYears.y,
                galaxyLocalPositionLightYears.x * sin +
                galaxyLocalPositionLightYears.z * cos);
        }

        public static double Gaussian(double distance, double width)
        {
            if (width <= 0.0)
                return 0.0;

            var normalized = distance / width;
            return Math.Exp(-0.5 * normalized * normalized);
        }

        public static double ExponentialFalloff(double distance, double scale)
        {
            if (scale <= 0.0)
                return 0.0;

            return Math.Exp(-Math.Max(0.0, distance) / scale);
        }

        public static double Clamp01(double value)
        {
            return math.clamp(value, 0.0, 1.0);
        }

        public static double HashNoise3D(
            ulong seed,
            double3 position)
        {
            var floored = math.floor(position);

            var x = unchecked((ulong)(long)floored.x);
            var y = unchecked((ulong)(long)floored.y);
            var z = unchecked((ulong)(long)floored.z);

            var hash = GalaxyIDUtility.Combine(seed, x);
            hash = GalaxyIDUtility.Combine(hash, y);
            hash = GalaxyIDUtility.Combine(hash, z);

            return (hash >> 11) * (1.0 / (1UL << 53));
        }
    }
}
