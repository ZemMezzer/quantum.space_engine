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
        fileName = "Ring Galaxy Generator",
        menuName = "Space Engine/Stellar Objects/Galaxies/Generators/Ring")]
    public sealed class RingGalaxyGenerator : ProceduralGalaxyGenerator
    {
        protected override float RelativeWeight => 0.02f;

        public override GalaxyData Generate(
            in GalaxyGenerationContext context,
            ref QuantumRandom random)
        {
            var radius = random.NextDouble(18_000.0, 75_000.0);
            var coreRadius = radius * random.NextDouble(0.035, 0.16);
            var diskThickness = radius * random.NextDouble(0.006, 0.025);
            var ringRadius = radius * random.NextDouble(0.35, 0.75);
            var ringWidth = radius * random.NextDouble(0.04, 0.16);
            var massKg = Math.Pow(radius / 10_000.0, 2.0) *
                         random.NextDouble(1.5e9, 9.0e9) *
                         SolarConstants.SOLAR_MASS_KG;

            return new RingGalaxyData(
                context.UniverseID,
                context.GalaxyID,
                context.Seed,
                context.UniversePositionLightYears,
                random.NextDouble(0.0, math.PI * 2.0),
                radius,
                coreRadius,
                diskThickness,
                massKg,
                random.NextDouble(0.0015, 0.0055),
                random.NextDouble(0.002, 0.040),
                0,
                0.0,
                0.0,
                1.0,
                ringRadius,
                ringWidth,
                random.NextDouble(0.0, 0.12));
        }

        protected override double GetShapeDensity(
            in GalaxyData galaxy,
            double3 position)
        {
            var radius = math.length(new double2(position.x, position.z));
            if (radius > galaxy.RadiusLightYears)
                return 0.0;

            var ring = Gaussian(
                Math.Abs(radius - galaxy.RingRadiusLightYears),
                Math.Max(1.0, galaxy.RingWidthLightYears)) *
                Exponential(
                    Math.Abs(position.y),
                    Math.Max(
                        1.0,
                        galaxy.DiskThicknessLightYears * 0.5));

            var core = Gaussian(
                math.length(position),
                Math.Max(1.0, galaxy.CoreRadiusLightYears));

            var disk = Exponential(
                radius,
                galaxy.RadiusLightYears * 0.45) *
                Exponential(
                    Math.Abs(position.y),
                    Math.Max(
                        1.0,
                        galaxy.DiskThicknessLightYears));

            return Clamp01(ring + core * 0.55 + disk * 0.12);
        }

        protected override double3 GenerateShapeLocalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var width = Math.Max(1.0, galaxy.RingWidthLightYears);
            var radius = math.clamp(
                galaxy.RingRadiusLightYears +
                Bell(ref random) * width,
                Math.Max(
                    galaxy.CoreRadiusLightYears * 0.1,
                    galaxy.RingRadiusLightYears - width * 2.5),
                Math.Min(
                    galaxy.RadiusLightYears * 0.96,
                    galaxy.RingRadiusLightYears + width * 2.5));

            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var thickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.45);

            return new double3(
                Math.Cos(angle) * radius,
                math.clamp(
                    Bell(ref random) * thickness,
                    -thickness,
                    thickness),
                Math.Sin(angle) * radius);
        }

        private static double Bell(ref QuantumRandom random)
        {
            return random.NextDouble() +
                   random.NextDouble() +
                   random.NextDouble() - 1.5;
        }
    }
}
