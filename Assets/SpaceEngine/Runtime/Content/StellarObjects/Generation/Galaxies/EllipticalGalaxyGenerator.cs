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
        fileName = "Elliptical Galaxy Generator",
        menuName = "Space Engine/Stellar Objects/Galaxies/Generators/Elliptical")]
    public sealed class EllipticalGalaxyGenerator : ProceduralGalaxyGenerator
    {
        protected override float RelativeWeight => 0.16f;

        public override GalaxyData Generate(
            in GalaxyGenerationContext context,
            ref QuantumRandom random)
        {
            var radius = random.NextDouble(15_000.0, 140_000.0);
            var coreRadius = radius * random.NextDouble(0.16, 0.38);
            var diskThickness = radius * random.NextDouble(0.35, 0.95);
            var massKg = Math.Pow(radius / 10_000.0, 2.0) *
                         random.NextDouble(1.5e9, 9.0e9) *
                         2.2 *
                         SolarConstants.SOLAR_MASS_KG;

            return new EllipticalGalaxyData(
                context.UniverseID,
                context.GalaxyID,
                context.Seed,
                context.UniversePositionLightYears,
                random.NextDouble(0.0, math.PI * 2.0),
                radius,
                coreRadius,
                diskThickness,
                massKg,
                random.NextDouble(0.0025, 0.0100),
                random.NextDouble(0.002, 0.040),
                0,
                0.0,
                0.0,
                random.NextDouble(0.35, 1.0),
                0.0,
                0.0,
                random.NextDouble(0.0, 0.12));
        }

        protected override double GetShapeDensity(
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

        protected override double3 GenerateShapeLocalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var direction = SampleUnitVector(ref random);
            var radius = galaxy.RadiusLightYears *
                         Math.Pow(random.NextDouble(), 1.85);
            var verticalScale = math.clamp(
                galaxy.Ellipticity,
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
