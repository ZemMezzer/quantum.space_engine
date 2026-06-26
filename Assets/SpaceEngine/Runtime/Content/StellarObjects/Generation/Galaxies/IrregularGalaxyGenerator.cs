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
        fileName = "Irregular Galaxy Generator",
        menuName = "Space Engine/Stellar Objects/Galaxies/Generators/Irregular")]
    public sealed class IrregularGalaxyGenerator : ProceduralGalaxyGenerator
    {
        protected override float RelativeWeight => 0.26f;

        public override GalaxyData Generate(
            in GalaxyGenerationContext context,
            ref QuantumRandom random)
        {
            var radius = random.NextDouble(4_000.0, 32_000.0);
            var coreRadius = radius * random.NextDouble(0.035, 0.16);
            var diskThickness = radius * random.NextDouble(0.15, 0.45);
            var massKg = Math.Pow(radius / 10_000.0, 2.0) *
                         random.NextDouble(1.5e9, 9.0e9) *
                         0.35 *
                         SolarConstants.SOLAR_MASS_KG;

            return new IrregularGalaxyData(
                context.UniverseID,
                context.GalaxyID,
                context.Seed,
                context.UniversePositionLightYears,
                random.NextDouble(0.0, math.PI * 2.0),
                radius,
                coreRadius,
                diskThickness,
                massKg,
                random.NextDouble(0.0005, 0.0030),
                random.NextDouble(0.002, 0.040),
                0,
                0.0,
                0.0,
                1.0,
                0.0,
                0.0,
                random.NextDouble(0.55, 1.0));
        }

        protected override double GetShapeDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var radius = math.length(position);
            if (radius > galaxy.RadiusLightYears)
                return 0.0;

            var broadCloud = Gaussian(
                radius,
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
