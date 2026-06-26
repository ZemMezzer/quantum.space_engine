using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    public abstract class CompactStellarObjectGeneratorBase : StellarObjectGenerator
    {
        protected abstract float PrimaryWeight { get; }
        protected abstract double MinimumRotationSeconds { get; }
        protected abstract double MaximumRotationSeconds { get; }
        protected abstract double MinimumTemperatureKelvin { get; }
        protected abstract double MaximumTemperatureKelvin { get; }

        public override float GetWeight(
            in StellarObjectGenerationContext context)
        {
            return context.Coordinates.SolarSystemID == 0L
                ? 0.0f
                : context.GetSelectionWeight(PrimaryWeight);
        }

        public override StellarObjectData Generate(
            in StellarObjectGenerationContext context,
            ref QuantumRandom random)
        {
            var massSolar = random.NextDouble(1.10, 2.20);
            var radiusMeters = random.NextDouble(10_000.0, 14_000.0);
            var temperatureKelvin = random.NextDouble(
                MinimumTemperatureKelvin,
                MaximumTemperatureKelvin);
            var radiusSolar = radiusMeters / SolarConstants.SOLAR_RADIUS_METERS;
            var luminositySolar = radiusSolar * radiusSolar * Math.Pow(
                temperatureKelvin / SolarConstants.SOLAR_TEMPERATURE_KELVIN,
                4.0);

            return StellarObjectGenerationUtility.CreateStar(
                massSolar * SolarConstants.SOLAR_MASS_KG,
                radiusMeters,
                0.0,
                0.0,
                temperatureKelvin,
                luminositySolar * SolarConstants.SOLAR_LUMINOSITY_WATTS,
                random.NextDouble(MinimumRotationSeconds, MaximumRotationSeconds),
                random.NextDouble(
                    10_000_000.0,
                    StellarObjectGenerationUtility.MaximumUniverseAgeYears),
                random.NextDouble(0.01, 1.5),
                context.Orbit);
        }
    }
}
