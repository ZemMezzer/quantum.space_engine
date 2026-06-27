using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Data.SolarSystem.Objects;
using SpaceEngine.Runtime.Utils;

namespace SpaceEngine.Runtime.Content.StellarObjects
{
    internal static class StellarObjectGenerationUtility
    {
        public const double EarthMassKg = 5.9722e24;
        public const double AstronomicalUnitMeters = 149_597_870_700.0;
        public const double MaximumUniverseAgeYears = 13_500_000_000.0;
        private const double GravitationalConstant = 6.67430e-11;
        private const double SpeedOfLightMetersPerSecond = 299_792_458.0;

        public static StarData CreateStar(
            double massKg,
            double radiusMeters,
            double densityKgPerCubicMeter,
            double surfaceGravityMetersPerSecondSquared,
            double surfaceTemperatureKelvin,
            double luminosityWatts,
            double rotationPeriodSeconds,
            double ageYears,
            double metallicity,
            OrbitData orbit)
        {
            return new StarData(
                massKg,
                radiusMeters,
                densityKgPerCubicMeter,
                surfaceGravityMetersPerSecondSquared,
                surfaceTemperatureKelvin,
                luminosityWatts,
                rotationPeriodSeconds,
                ageYears,
                metallicity,
                orbit);
        }

        public static BlackHoleData CreateBlackHole(
            double massKg,
            double radiusMeters,
            double rotationPeriodSeconds,
            double ageYears,
            double metallicity,
            double temperatureKelvin,
            bool hasAccretionDisk,
            OrbitData orbit)
        {
            return new BlackHoleData(
                massKg,
                radiusMeters,
                rotationPeriodSeconds,
                ageYears,
                metallicity,
                temperatureKelvin,
                hasAccretionDisk,
                orbit);
        }

        public static double GetHawkingTemperatureKelvin(double massKg)
        {
            const double reducedPlanckConstant = 1.054571817e-34;
            const double speedOfLightMetersPerSecond = 299_792_458.0;
            const double BoltzmannConstant = 1.380649e-23;

            if (massKg <= 0.0)
                return StellarObjectData.CosmicBackgroundTemperatureKelvin;

            return reducedPlanckConstant *
                   speedOfLightMetersPerSecond *
                   speedOfLightMetersPerSecond *
                   speedOfLightMetersPerSecond /
                   (8.0 * Math.PI * GravitationalConstant * massKg *
                    BoltzmannConstant);
        }

        public static PlanetData CreatePlanet(
            double massKg,
            double radiusMeters,
            double densityKgPerCubicMeter,
            double surfaceGravityMetersPerSecondSquared,
            double surfaceTemperatureKelvin,
            double atmospherePressurePascals,
            double waterCoverage,
            double iceCoverage,
            double volcanicActivity,
            byte satelliteCount,
            bool hasAtmosphere,
            bool hasRings,
            OrbitData orbit)
        {
            return new PlanetData(
                massKg,
                radiusMeters,
                densityKgPerCubicMeter,
                surfaceGravityMetersPerSecondSquared,
                surfaceTemperatureKelvin,
                atmospherePressurePascals,
                waterCoverage,
                iceCoverage,
                volcanicActivity,
                satelliteCount,
                hasAtmosphere,
                hasRings,
                orbit);
        }

        public static double GetLuminositySolar(double massSolar)
        {
            if (massSolar < 0.43)
                return 0.23 * Math.Pow(massSolar, 2.3);

            if (massSolar < 2.0)
                return Math.Pow(massSolar, 4.0);

            return 1.5 * Math.Pow(massSolar, 3.5);
        }

        public static double GetRadiusSolar(double massSolar)
        {
            return massSolar < 1.0
                ? Math.Pow(massSolar, 0.8)
                : Math.Pow(massSolar, 0.57);
        }

        public static double GetMainSequenceLifetimeYears(double massSolar)
        {
            return Math.Min(
                10_000_000_000.0 / Math.Pow(massSolar, 2.5),
                MaximumUniverseAgeYears);
        }

        public static double GetSchwarzschildRadiusMeters(double massKg)
        {
            return 2.0 * GravitationalConstant * massKg /
                   (SpeedOfLightMetersPerSecond *
                    SpeedOfLightMetersPerSecond);
        }

        public static double GetRadiusMeters(
            double massKg,
            double densityKgPerCubicMeter)
        {
            var volume = massKg / densityKgPerCubicMeter;
            return Math.Pow(3.0 * volume / (4.0 * Math.PI), 1.0 / 3.0);
        }

        public static double GetSurfaceGravity(
            double massKg,
            double radiusMeters)
        {
            return GravitationalConstant * massKg /
                   (radiusMeters * radiusMeters);
        }

        public static double GetEquilibriumTemperatureKelvin(
            double totalLuminosityWatts,
            double orbitAu)
        {
            var luminositySolar = totalLuminosityWatts /
                                  SolarConstants.SOLAR_LUMINOSITY_WATTS;
            return 278.5 * Math.Pow(luminositySolar, 0.25) /
                   Math.Sqrt(Math.Max(0.00001, orbitAu));
        }
    }
}
