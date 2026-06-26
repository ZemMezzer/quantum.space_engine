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
        fileName = "Barred Spiral Galaxy Generator",
        menuName = "Space Engine/Stellar Objects/Galaxies/Generators/Barred Spiral")]
    public sealed class BarredSpiralGalaxyGenerator : ProceduralGalaxyGenerator
    {
        protected override float RelativeWeight => 0.20f;

        public override GalaxyData Generate(
            in GalaxyGenerationContext context,
            ref QuantumRandom random)
        {
            var radius = random.NextDouble(25_000.0, 95_000.0);
            var coreRadius = radius * random.NextDouble(0.035, 0.16);
            var diskThickness = radius * random.NextDouble(0.006, 0.025);
            var massKg = Math.Pow(radius / 10_000.0, 2.0) *
                         random.NextDouble(1.5e9, 9.0e9) *
                         SolarConstants.SOLAR_MASS_KG;

            return new BarredSpiralGalaxyData(
                context.UniverseID,
                context.GalaxyID,
                context.Seed,
                context.UniversePositionLightYears,
                random.NextDouble(0.0, math.PI * 2.0),
                radius,
                coreRadius,
                diskThickness,
                massKg,
                random.NextDouble(0.0015, 0.0060),
                random.NextDouble(0.002, 0.040),
                (byte)random.NextInt(2, 6),
                random.NextDouble(1.2, 4.8),
                radius * random.NextDouble(0.25, 0.65),
                1.0,
                0.0,
                0.0,
                random.NextDouble(0.0, 0.12));
        }

        protected override double GetShapeDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var radius = math.length(new double2(position.x, position.z));
            if (radius > galaxy.RadiusLightYears)
                return 0.0;

            var disk = Exponential(
                radius,
                galaxy.RadiusLightYears * 0.42) *
                Exponential(
                    Math.Abs(position.y),
                    Math.Max(
                        1.0,
                        galaxy.DiskThicknessLightYears * 0.5));

            var core = Gaussian(
                math.length(position),
                Math.Max(1.0, galaxy.CoreRadiusLightYears));

            var arms = GetSpiralArmFactor(galaxy, position, radius);
            var spiralDensity = Clamp01(
                disk * (0.20 + arms * 0.80) +
                core * 0.85);

            var halfBarLength = Math.Max(
                1.0,
                galaxy.BarLengthLightYears * 0.5);
            var barThickness = Math.Max(
                1.0,
                galaxy.CoreRadiusLightYears * 0.45);
            var verticalThickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears);

            var barDistance = Math.Sqrt(
                (position.x / halfBarLength) *
                (position.x / halfBarLength) +
                (position.z / barThickness) *
                (position.z / barThickness) +
                (position.y / verticalThickness) *
                (position.y / verticalThickness));

            return Clamp01(Math.Max(
                spiralDensity,
                Math.Exp(-2.0 * barDistance * barDistance)));
        }

        protected override double3 GenerateShapeLocalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return galaxy.BarLengthLightYears > 0.0 &&
                   random.NextDouble() < 0.28
                ? GenerateBarPosition(galaxy, ref random)
                : GenerateDiskPosition(galaxy, ref random);
        }

        private static double GetSpiralArmFactor(
            in GalaxyData galaxy,
            double3 position,
            double radius)
        {
            if (galaxy.SpiralArmCount == 0 ||
                radius < galaxy.CoreRadiusLightYears)
            {
                return 0.0;
            }

            var angle = Math.Atan2(position.z, position.x);
            var logarithmicRadius = Math.Log(Math.Max(
                1.0,
                radius / Math.Max(1.0, galaxy.CoreRadiusLightYears)));
            var phase = angle -
                        galaxy.SpiralArmTightness * logarithmicRadius;
            var wave = 0.5 + 0.5 * Math.Cos(
                phase * galaxy.SpiralArmCount);

            return wave * wave * wave;
        }

        private static double3 GenerateDiskPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = galaxy.RadiusLightYears *
                         Math.Pow(random.NextDouble(), 1.65);

            radius = Math.Max(
                galaxy.CoreRadiusLightYears * 0.08,
                Math.Min(
                    radius,
                    galaxy.RadiusLightYears * 0.94));

            var angle = random.NextDouble(0.0, math.PI * 2.0);

            if (galaxy.SpiralArmCount > 0 &&
                random.NextDouble() < 0.72)
            {
                var arm = random.NextInt(0, galaxy.SpiralArmCount);
                var core = Math.Max(1.0, galaxy.CoreRadiusLightYears);

                angle = math.PI * 2.0 * arm / galaxy.SpiralArmCount +
                        galaxy.SpiralArmTightness *
                        Math.Log(Math.Max(1.0, radius / core)) +
                        Bell(ref random) * 0.28;
            }

            var thickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.42);

            return new double3(
                Math.Cos(angle) * radius,
                math.clamp(
                    Bell(ref random) * thickness,
                    -thickness,
                    thickness),
                Math.Sin(angle) * radius);
        }

        private static double3 GenerateBarPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var halfLength = Math.Max(
                1.0,
                galaxy.BarLengthLightYears * 0.5);
            var halfThickness = Math.Max(
                1.0,
                galaxy.CoreRadiusLightYears * 0.16);
            var verticalThickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.45);

            return new double3(
                random.NextDouble(-halfLength, halfLength),
                math.clamp(
                    Bell(ref random) * verticalThickness,
                    -verticalThickness,
                    verticalThickness),
                math.clamp(
                    Bell(ref random) * halfThickness,
                    -halfThickness,
                    halfThickness));
        }

        private static double Bell(ref QuantumRandom random)
        {
            return random.NextDouble() +
                   random.NextDouble() +
                   random.NextDouble() - 1.5;
        }
    }
}
