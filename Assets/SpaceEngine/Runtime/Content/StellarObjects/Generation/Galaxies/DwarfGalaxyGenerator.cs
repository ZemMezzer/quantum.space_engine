using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Data.Galaxy.BuiltIn;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies
{
    [CreateAssetMenu(
        fileName = "Dwarf Galaxy Generator",
        menuName = "Space Engine/Stellar Objects/Galaxies/Generators/Dwarf")]
    public sealed class DwarfGalaxyGenerator : ProceduralGalaxyGenerator
    {
        protected override float RelativeWeight => 0.45f;

        public override GalaxyData Generate(
            in GalaxyGenerationContext context,
            ref QuantumRandom random)
        {
            var radius = random.NextDouble(700.0, 12_000.0);
            var coreRadius = radius * random.NextDouble(0.035, 0.16);
            var diskThickness = radius * random.NextDouble(0.15, 0.45);
            var massKg = Math.Pow(radius / 10_000.0, 2.0) *
                         random.NextDouble(1.5e9, 9.0e9) *
                         0.08 *
                         SolarConstants.SOLAR_MASS_KG;

            return new DwarfGalaxyData(
                context.UniverseID,
                context.GalaxyID,
                context.Seed,
                context.UniversePositionLightYears,
                random.NextDouble(0.0, math.PI * 2.0),
                radius,
                coreRadius,
                diskThickness,
                massKg,
                random.NextDouble(0.0002, 0.0020),
                random.NextDouble(0.002, 0.040),
                0,
                0.0,
                0.0,
                random.NextDouble(0.45, 1.0),
                0.0,
                0.0,
                random.NextDouble(0.20, 0.85));
        }

        protected override double GetShapeDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var ellipticalDensity = GetEllipticalDensity(galaxy, position);
            var irregularDensity = GetIrregularDensity(galaxy, position);

            return Clamp01(
                ellipticalDensity * (1.0 - galaxy.Irregularity) +
                irregularDensity * galaxy.Irregularity);
        }

        protected override double3 GenerateShapeLocalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var direction = SampleUnitVector(ref random);
            var radius = galaxy.RadiusLightYears *
                         Math.Pow(random.NextDouble(), 1.45);
            var verticalScale = math.clamp(
                galaxy.DiskThicknessLightYears /
                Math.Max(1.0, galaxy.RadiusLightYears),
                0.25,
                1.0);

            return new double3(
                direction.x * radius,
                direction.y * radius * verticalScale,
                direction.z * radius);
        }

        private static double GetEllipticalDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var verticalScale = Math.Max(0.25, galaxy.Ellipticity);
            var ellipsoidalRadius = Math.Sqrt(
                position.x * position.x +
                position.z * position.z +
                (position.y / verticalScale) *
                (position.y / verticalScale));

            if (ellipsoidalRadius > galaxy.RadiusLightYears)
                return 0.0;

            var core = Gaussian(
                ellipsoidalRadius,
                Math.Max(1.0, galaxy.CoreRadiusLightYears));
            var halo = Exponential(
                ellipsoidalRadius,
                galaxy.RadiusLightYears * 0.35);

            return Clamp01(core * 0.95 + halo * 0.55);
        }

        private static double GetIrregularDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var sphericalRadius = math.length(position);
            if (sphericalRadius > galaxy.RadiusLightYears)
                return 0.0;

            var broadCloud = Gaussian(
                sphericalRadius,
                galaxy.RadiusLightYears * 0.55);

            var noiseA = HashNoise3D(
                galaxy.Seed,
                position / Math.Max(
                    30.0,
                    galaxy.RadiusLightYears * 0.10));

            var noiseB = HashNoise3D(
                galaxy.Seed ^ 0x9E3779B97F4A7C15UL,
                position / Math.Max(
                    80.0,
                    galaxy.RadiusLightYears * 0.28));

            var clumps = Math.Max(
                0.0,
                noiseA * 0.70 + noiseB * 0.55 - 0.55);

            return Clamp01(
                broadCloud * (
                    0.25 +
                    clumps * (1.0 + galaxy.Irregularity)));
        }

        private static double3 SampleUnitVector(ref QuantumRandom random)
        {
            var z = random.NextDouble(-1.0, 1.0);
            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var radial = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));

            return new double3(
                Math.Cos(angle) * radial,
                z,
                Math.Sin(angle) * radial);
        }
    }
}
