using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    [CreateAssetMenu(
        fileName = "White Dwarf Generator",
        menuName = "Space Engine/Stellar Objects/Stars/Generators/White Dwarf")]
    public sealed class WhiteDwarfStellarObjectGenerator : StellarObjectGenerator
    {
        private const float RELATIVE_WEIGHT = 0.04f;

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
            var massSolar = random.NextDouble(0.50, 1.30);
            var radiusSolar = random.NextDouble(0.008, 0.020);
            var temperatureKelvin = random.NextDouble(4_000.0, 100_000.0);
            var luminositySolar = radiusSolar * radiusSolar * Math.Pow(
                temperatureKelvin / SolarConstants.SOLAR_TEMPERATURE_KELVIN,
                4.0);

            return StellarObjectGenerationUtility.CreateStar(
                massSolar * SolarConstants.SOLAR_MASS_KG,
                radiusSolar * SolarConstants.SOLAR_RADIUS_METERS,
                0.0,
                0.0,
                temperatureKelvin,
                luminositySolar * SolarConstants.SOLAR_LUMINOSITY_WATTS,
                random.NextDouble(3_600.0, 7.0 * 86_400.0),
                random.NextDouble(1_000_000_000.0,
                    StellarObjectGenerationUtility.MaximumUniverseAgeYears),
                random.NextDouble(0.1, 2.0),
                context.Orbit);
        }
    }
}
