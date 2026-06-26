using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    public abstract class PlanetaryStellarObjectGeneratorBase : PlanetGenerator
    {
        public override float GetWeight(in PlanetGenerationContext context)
        {
            if (context.CentralMassKg <= 0.0 ||
                context.IlluminationLuminosityWatts <= 0.0000001)
            {
                return 0.0f;
            }

            var snowLineAu = GetSnowLineAu(
                context.IlluminationLuminosityWatts);
            var orbitDistanceAu = context.Orbit.SemiMajorAxisMeters /
                                  StellarObjectGenerationUtility
                                      .AstronomicalUnitMeters;
            var normalizedDistance = snowLineAu <= 0.0
                ? 0.0
                : orbitDistanceAu / snowLineAu;

            return context.GetSelectionWeight(
                GetRelativeWeight(normalizedDistance));
        }

        protected abstract float GetRelativeWeight(double normalizedDistance);

        protected static StellarObjectData CreatePlanet(
            double massEarths,
            double densityKgPerCubicMeter,
            double equilibriumTemperatureKelvin,
            double atmospherePressurePascals,
            double waterCoverage,
            double iceCoverage,
            double volcanicActivity,
            byte satelliteCount,
            bool hasAtmosphere,
            bool hasRings,
            in PlanetGenerationContext context)
        {
            var massKg = massEarths * StellarObjectGenerationUtility.EarthMassKg;
            var radiusMeters = StellarObjectGenerationUtility.GetRadiusMeters(
                massKg,
                densityKgPerCubicMeter);
            var gravity = StellarObjectGenerationUtility.GetSurfaceGravity(
                massKg,
                radiusMeters);

            return StellarObjectGenerationUtility.CreatePlanet(
                massKg,
                radiusMeters,
                densityKgPerCubicMeter,
                gravity,
                equilibriumTemperatureKelvin,
                atmospherePressurePascals,
                waterCoverage,
                iceCoverage,
                volcanicActivity,
                satelliteCount,
                hasAtmosphere,
                hasRings,
                context.Orbit);
        }

        protected static double GetEquilibriumTemperature(
            in PlanetGenerationContext context)
        {
            var orbitDistanceAu = context.Orbit.SemiMajorAxisMeters /
                                  StellarObjectGenerationUtility
                                      .AstronomicalUnitMeters;
            return StellarObjectGenerationUtility.GetEquilibriumTemperatureKelvin(
                context.IlluminationLuminosityWatts,
                orbitDistanceAu);
        }

        private static double GetSnowLineAu(double luminosityWatts)
        {
            var luminositySolar = luminosityWatts /
                                  SolarConstants.SOLAR_LUMINOSITY_WATTS;
            return Math.Max(0.35, 2.7 * Math.Sqrt(luminositySolar));
        }
    }
}
