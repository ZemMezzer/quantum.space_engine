using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    [CreateAssetMenu(
        fileName = "Red Giant Generator",
        menuName = "Space Engine/Stellar Objects/Stars/Generators/Red Giant")]
    public sealed class RedGiantStellarObjectGenerator : StellarObjectGenerator
    {
        private const float RELATIVE_WEIGHT = 0.02f;

        public override float GetWeight(
            in StellarObjectGenerationContext context)
        {
            return context.Coordinates.SolarSystemID == 0L
                ? 0.0f
                : context.GetSelectionWeight(RELATIVE_WEIGHT);
        }

        public override StellarObjectData Generate(
            in StellarObjectGenerationContext context,
            ref QuantumRandom random)
        {
            var massSolar = random.NextDouble(0.8, 8.0);
            var radiusSolar = random.NextDouble(10.0, 120.0);
            var temperatureKelvin = random.NextDouble(3_000.0, 5_500.0);
            var luminositySolar = radiusSolar * radiusSolar * Math.Pow(
                temperatureKelvin / SolarConstants.SOLAR_TEMPERATURE_KELVIN,
                4.0);
            var maximumLifetimeYears = StellarObjectGenerationUtility
                .GetMainSequenceLifetimeYears(massSolar);

            return StellarObjectGenerationUtility.CreateStar(
                massSolar * SolarConstants.SOLAR_MASS_KG,
                radiusSolar * SolarConstants.SOLAR_RADIUS_METERS,
                0.0,
                0.0,
                temperatureKelvin,
                luminositySolar * SolarConstants.SOLAR_LUMINOSITY_WATTS,
                random.NextDouble(50.0 * 86_400.0, 600.0 * 86_400.0),
                random.NextDouble(
                    Math.Max(500_000_000.0, maximumLifetimeYears * 0.75),
                    Math.Min(StellarObjectGenerationUtility.MaximumUniverseAgeYears,
                        maximumLifetimeYears * 1.10)),
                random.NextDouble(0.1, 2.0),
                context.Orbit);
        }
    }
}
