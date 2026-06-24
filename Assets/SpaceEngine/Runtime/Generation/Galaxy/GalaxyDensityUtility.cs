using System;
using SpaceEngine.Runtime.Data.Galaxy;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Returns normalized stellar-system density at a galaxy-local position.
    /// The result is typically in the 0..1 range and is intended for sector generation.
    /// </summary>
    public static class GalaxyDensityUtility
    {
        public static double GetDensity(
            in GalaxyData galaxy,
            double3 galaxyLocalPositionLightYears)
        {
            var position = GalaxyShapeUtility.ToShapeLocalPosition(
                galaxy,
                galaxyLocalPositionLightYears);

            switch (galaxy.Type)
            {
                case GalaxyType.Spiral:
                    return GetSpiralDensity(galaxy, position);

                case GalaxyType.BarredSpiral:
                    return GetBarredSpiralDensity(galaxy, position);

                case GalaxyType.Elliptical:
                    return GetEllipticalDensity(galaxy, position);

                case GalaxyType.Lenticular:
                    return GetLenticularDensity(galaxy, position);

                case GalaxyType.Irregular:
                    return GetIrregularDensity(galaxy, position);

                case GalaxyType.Ring:
                    return GetRingDensity(galaxy, position);

                case GalaxyType.Dwarf:
                    return GetDwarfDensity(galaxy, position);

                default:
                    return 0.0;
            }
        }

        private static double GetSpiralDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var radius = math.length(new double2(position.x, position.z));
            if (radius > galaxy.RadiusLightYears)
                return 0.0;

            var disk = GalaxyShapeUtility.ExponentialFalloff(
                           radius,
                           galaxy.RadiusLightYears * 0.42) *
                       GalaxyShapeUtility.ExponentialFalloff(
                           Math.Abs(position.y),
                           Math.Max(1.0, galaxy.DiskThicknessLightYears * 0.5));

            var core = GalaxyShapeUtility.Gaussian(
                math.length(position),
                Math.Max(1.0, galaxy.CoreRadiusLightYears));

            var arms = GetSpiralArmFactor(galaxy, position, radius);
            return GalaxyShapeUtility.Clamp01(disk * (0.20 + arms * 0.80) + core * 0.85);
        }

        private static double GetBarredSpiralDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var spiral = GetSpiralDensity(galaxy, position);
            var halfBarLength = Math.Max(1.0, galaxy.BarLengthLightYears * 0.5);
            var barThickness = Math.Max(1.0, galaxy.CoreRadiusLightYears * 0.45);

            var barDistance = Math.Sqrt(
                (position.x / halfBarLength) * (position.x / halfBarLength) +
                (position.z / barThickness) * (position.z / barThickness) +
                (position.y / Math.Max(1.0, galaxy.DiskThicknessLightYears)) *
                (position.y / Math.Max(1.0, galaxy.DiskThicknessLightYears)));

            var bar = Math.Exp(-2.0 * barDistance * barDistance);
            return GalaxyShapeUtility.Clamp01(Math.Max(spiral, bar));
        }

        private static double GetEllipticalDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var verticalScale = Math.Max(0.25, galaxy.Ellipticity);
            var ellipsoidalRadius = Math.Sqrt(
                position.x * position.x +
                position.z * position.z +
                (position.y / verticalScale) * (position.y / verticalScale));

            if (ellipsoidalRadius > galaxy.RadiusLightYears)
                return 0.0;

            var core = GalaxyShapeUtility.Gaussian(
                ellipsoidalRadius,
                Math.Max(1.0, galaxy.CoreRadiusLightYears));

            var halo = GalaxyShapeUtility.ExponentialFalloff(
                ellipsoidalRadius,
                galaxy.RadiusLightYears * 0.35);

            return GalaxyShapeUtility.Clamp01(core * 0.95 + halo * 0.55);
        }

        private static double GetLenticularDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var radius = math.length(new double2(position.x, position.z));
            if (radius > galaxy.RadiusLightYears)
                return 0.0;

            var disk = GalaxyShapeUtility.ExponentialFalloff(
                           radius,
                           galaxy.RadiusLightYears * 0.38) *
                       GalaxyShapeUtility.ExponentialFalloff(
                           Math.Abs(position.y),
                           Math.Max(1.0, galaxy.DiskThicknessLightYears * 0.45));

            var bulge = GalaxyShapeUtility.Gaussian(
                math.length(position),
                Math.Max(1.0, galaxy.CoreRadiusLightYears * 1.35));

            return GalaxyShapeUtility.Clamp01(disk * 0.75 + bulge);
        }

        private static double GetIrregularDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var radius = math.length(position);
            if (radius > galaxy.RadiusLightYears)
                return 0.0;

            var broadCloud = GalaxyShapeUtility.Gaussian(
                radius,
                galaxy.RadiusLightYears * 0.55);

            var noiseA = GalaxyShapeUtility.HashNoise3D(
                galaxy.Seed,
                position / Math.Max(30.0, galaxy.RadiusLightYears * 0.10));

            var noiseB = GalaxyShapeUtility.HashNoise3D(
                galaxy.Seed ^ 0x9E3779B97F4A7C15UL,
                position / Math.Max(80.0, galaxy.RadiusLightYears * 0.28));

            var clumps = Math.Max(0.0, noiseA * 0.70 + noiseB * 0.55 - 0.55);
            return GalaxyShapeUtility.Clamp01(broadCloud * (0.25 + clumps * (1.0 + galaxy.Irregularity)));
        }

        private static double GetRingDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var planarRadius = math.length(new double2(position.x, position.z));
            if (planarRadius > galaxy.RadiusLightYears)
                return 0.0;

            var ring = GalaxyShapeUtility.Gaussian(
                           Math.Abs(planarRadius - galaxy.RingRadiusLightYears),
                           Math.Max(1.0, galaxy.RingWidthLightYears)) *
                       GalaxyShapeUtility.ExponentialFalloff(
                           Math.Abs(position.y),
                           Math.Max(1.0, galaxy.DiskThicknessLightYears * 0.5));

            var core = GalaxyShapeUtility.Gaussian(
                math.length(position),
                Math.Max(1.0, galaxy.CoreRadiusLightYears));

            var disk = GalaxyShapeUtility.ExponentialFalloff(
                           planarRadius,
                           galaxy.RadiusLightYears * 0.45) *
                       GalaxyShapeUtility.ExponentialFalloff(
                           Math.Abs(position.y),
                           Math.Max(1.0, galaxy.DiskThicknessLightYears));

            return GalaxyShapeUtility.Clamp01(ring + core * 0.55 + disk * 0.12);
        }

        private static double GetDwarfDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var ellipsoid = GetEllipticalDensity(galaxy, position);
            var irregular = GetIrregularDensity(galaxy, position);

            return GalaxyShapeUtility.Clamp01(
                ellipsoid * (1.0 - galaxy.Irregularity) +
                irregular * galaxy.Irregularity);
        }

        private static double GetSpiralArmFactor(
            in GalaxyData galaxy,
            double3 position,
            double radius)
        {
            if (galaxy.SpiralArmCount == 0 || radius < galaxy.CoreRadiusLightYears)
                return 0.0;

            var angle = Math.Atan2(position.z, position.x);
            var logarithmicRadius = Math.Log(
                Math.Max(1.0, radius / Math.Max(1.0, galaxy.CoreRadiusLightYears)));

            var phase = angle - galaxy.SpiralArmTightness * logarithmicRadius;
            var wave = 0.5 + 0.5 * Math.Cos(phase * galaxy.SpiralArmCount);

            return wave * wave * wave;
        }
    }
}
