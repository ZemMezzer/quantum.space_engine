using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Planet;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;

namespace SpaceEngine.Runtime.Generation.Planet
{
    /// <summary>
    /// Generates deterministic circumbinary or single-star planets.
    /// All generated planets orbit the system barycenter.
    /// </summary>
    internal static class PlanetGenerator
    {
        private const double AstronomicalUnitMeters = 149_597_870_700.0;

        private const double EarthMassKg = 5.9722e24;
        private const double SolarMassKg = 1.98847e30;
        private const double SolarLuminosityWatts = 3.828e26;

        private const double GravitationalConstant = 6.67430e-11;

        internal static FixedList4096Bytes<PlanetData> Generate(
            ref QuantumRandom quantumRandom,
            FixedList512Bytes<StarData> stars)
        {
            var planets = new FixedList4096Bytes<PlanetData>();

            if (stars.Length == 0)
                return planets;

            var totalLuminositySolar = GetTotalLuminositySolar(stars);

            if (totalLuminositySolar <= 0.0000001)
                return planets;

            var totalMassKg = GetTotalMassKg(stars);

            var innerOrbitAu = GetInnerOrbitAu(
                totalLuminositySolar,
                stars);

            var outerOrbitAu = GetOuterOrbitAu(
                totalLuminositySolar,
                totalMassKg,
                innerOrbitAu);

            if (innerOrbitAu >= outerOrbitAu)
                return planets;

            var desiredPlanetCount = quantumRandom.NextInt(3, 11);

            var maximumPlanetCount = Math.Min(
                desiredPlanetCount,
                SolarSystemData.MAX_PLANETS);

            var currentOrbitAu = innerOrbitAu;

            for (var i = 0; i < maximumPlanetCount; i++)
            {
                currentOrbitAu *= quantumRandom.NextDouble(
                    1.35,
                    2.05);

                if (currentOrbitAu > outerOrbitAu)
                    break;

                if (quantumRandom.NextDouble() < 0.12)
                    continue;

                var planet = GeneratePlanet(
                    ref quantumRandom,
                    totalLuminositySolar,
                    currentOrbitAu);

                planets.Add(planet);
            }

            return planets;
        }

        private static PlanetData GeneratePlanet(
            ref QuantumRandom quantumRandom,
            double totalLuminositySolar,
            double orbitAu)
        {
            var snowLineAu = GetSnowLineAu(totalLuminositySolar);

            var type = GetPlanetType(
                ref quantumRandom,
                orbitAu,
                snowLineAu);

            var massEarths = GetMassEarths(
                type,
                ref quantumRandom);

            var density = GetDensity(
                type,
                ref quantumRandom);

            var massKg = massEarths * EarthMassKg;

            var radiusMeters = GetRadiusMeters(
                massKg,
                density);

            var surfaceGravity = GetSurfaceGravity(
                massKg,
                radiusMeters);

            var equilibriumTemperature = GetEquilibriumTemperatureKelvin(
                totalLuminositySolar,
                orbitAu);

            GetEnvironment(
                type,
                equilibriumTemperature,
                ref quantumRandom,
                out var atmospherePressurePascals,
                out var waterCoverage,
                out var iceCoverage,
                out var volcanicActivity,
                out var hasAtmosphere);

            var hasRings = GenerateHasRings(
                type,
                ref quantumRandom);

            var moonCount = GenerateMoonCount(
                type,
                massEarths,
                ref quantumRandom);

            var orbit = GenerateOrbit(
                orbitAu,
                ref quantumRandom);

            return new PlanetData(
                type,
                massKg,
                radiusMeters,
                density,
                surfaceGravity,
                equilibriumTemperature,
                atmospherePressurePascals,
                waterCoverage,
                iceCoverage,
                volcanicActivity,
                hasAtmosphere,
                hasRings,
                moonCount,
                orbit);
        }

        private static PlanetType GetPlanetType(
            ref QuantumRandom quantumRandom,
            double orbitAu,
            double snowLineAu)
        {
            var positionRelativeToSnowLine = orbitAu / snowLineAu;
            var roll = quantumRandom.NextDouble();

            if (positionRelativeToSnowLine < 0.65)
            {
                if (roll < 0.82)
                    return PlanetType.Terrestrial;

                if (roll < 0.94)
                    return PlanetType.Ocean;

                return PlanetType.DwarfPlanet;
            }

            if (positionRelativeToSnowLine < 1.35)
            {
                if (roll < 0.55)
                    return PlanetType.Terrestrial;

                if (roll < 0.78)
                    return PlanetType.Ocean;

                if (roll < 0.92)
                    return PlanetType.GasGiant;

                return PlanetType.DwarfPlanet;
            }

            if (positionRelativeToSnowLine < 4.0)
            {
                if (roll < 0.48)
                    return PlanetType.GasGiant;

                if (roll < 0.76)
                    return PlanetType.IceGiant;

                if (roll < 0.88)
                    return PlanetType.Terrestrial;

                return PlanetType.DwarfPlanet;
            }

            if (roll < 0.38)
                return PlanetType.IceGiant;

            if (roll < 0.60)
                return PlanetType.GasGiant;

            return PlanetType.DwarfPlanet;
        }

        private static double GetMassEarths(
            PlanetType type,
            ref QuantumRandom quantumRandom)
        {
            switch (type)
            {
                case PlanetType.Terrestrial:
                    return quantumRandom.NextDouble(0.08, 5.0);

                case PlanetType.Ocean:
                    return quantumRandom.NextDouble(0.3, 8.0);

                case PlanetType.GasGiant:
                    return quantumRandom.NextDouble(20.0, 1_000.0);

                case PlanetType.IceGiant:
                    return quantumRandom.NextDouble(6.0, 35.0);

                case PlanetType.DwarfPlanet:
                    return quantumRandom.NextDouble(0.0001, 0.20);

                default:
                    return 1.0;
            }
        }

        private static double GetDensity(
            PlanetType type,
            ref QuantumRandom quantumRandom)
        {
            switch (type)
            {
                case PlanetType.Terrestrial:
                    return quantumRandom.NextDouble(3_800.0, 7_200.0);

                case PlanetType.Ocean:
                    return quantumRandom.NextDouble(2_800.0, 5_500.0);

                case PlanetType.GasGiant:
                    return quantumRandom.NextDouble(250.0, 1_500.0);

                case PlanetType.IceGiant:
                    return quantumRandom.NextDouble(900.0, 2_100.0);

                case PlanetType.DwarfPlanet:
                    return quantumRandom.NextDouble(900.0, 3_000.0);

                default:
                    return 5_500.0;
            }
        }

        private static void GetEnvironment(
            PlanetType type,
            double equilibriumTemperatureKelvin,
            ref QuantumRandom quantumRandom,
            out double atmospherePressurePascals,
            out double waterCoverage,
            out double iceCoverage,
            out double volcanicActivity,
            out bool hasAtmosphere)
        {
            atmospherePressurePascals = 0.0;
            waterCoverage = 0.0;
            iceCoverage = 0.0;
            volcanicActivity = 0.0;
            hasAtmosphere = false;

            switch (type)
            {
                case PlanetType.Terrestrial:
                    hasAtmosphere = quantumRandom.NextDouble() < 0.68;

                    atmospherePressurePascals = hasAtmosphere
                        ? quantumRandom.NextDouble(2_000.0, 2_000_000.0)
                        : 0.0;

                    waterCoverage =
                        equilibriumTemperatureKelvin > 265.0 &&
                        equilibriumTemperatureKelvin < 370.0
                            ? quantumRandom.NextDouble(0.0, 0.85)
                            : quantumRandom.NextDouble(0.0, 0.15);

                    iceCoverage =
                        equilibriumTemperatureKelvin < 270.0
                            ? quantumRandom.NextDouble(0.15, 1.0)
                            : quantumRandom.NextDouble(0.0, 0.20);

                    volcanicActivity = quantumRandom.NextDouble(0.0, 1.0);
                    break;

                case PlanetType.Ocean:
                    hasAtmosphere = true;

                    atmospherePressurePascals = quantumRandom.NextDouble(
                        40_000.0,
                        5_000_000.0);

                    waterCoverage = quantumRandom.NextDouble(0.70, 1.0);

                    iceCoverage =
                        equilibriumTemperatureKelvin < 280.0
                            ? quantumRandom.NextDouble(0.10, 0.85)
                            : 0.0;

                    volcanicActivity = quantumRandom.NextDouble(0.0, 0.55);
                    break;

                case PlanetType.GasGiant:
                    hasAtmosphere = true;

                    atmospherePressurePascals = quantumRandom.NextDouble(
                        10_000_000.0,
                        1_000_000_000.0);
                    break;

                case PlanetType.IceGiant:
                    hasAtmosphere = true;

                    atmospherePressurePascals = quantumRandom.NextDouble(
                        2_000_000.0,
                        500_000_000.0);

                    iceCoverage = quantumRandom.NextDouble(0.10, 0.75);
                    volcanicActivity = quantumRandom.NextDouble(0.0, 0.15);
                    break;

                case PlanetType.DwarfPlanet:
                    hasAtmosphere = quantumRandom.NextDouble() < 0.12;

                    atmospherePressurePascals = hasAtmosphere
                        ? quantumRandom.NextDouble(1.0, 1_000.0)
                        : 0.0;

                    iceCoverage = quantumRandom.NextDouble(0.15, 1.0);
                    volcanicActivity = quantumRandom.NextDouble(0.0, 0.10);
                    break;
            }
        }

        private static byte GenerateMoonCount(
            PlanetType type,
            double massEarths,
            ref QuantumRandom quantumRandom)
        {
            switch (type)
            {
                case PlanetType.GasGiant:
                    return (byte)quantumRandom.NextInt(4, 50);

                case PlanetType.IceGiant:
                    return (byte)quantumRandom.NextInt(1, 25);

                case PlanetType.Ocean:
                case PlanetType.Terrestrial:
                    if (massEarths < 0.3)
                        return 0;

                    return (byte)quantumRandom.NextInt(0, 4);

                case PlanetType.DwarfPlanet:
                    return quantumRandom.NextDouble() < 0.12
                        ? (byte)1
                        : (byte)0;

                default:
                    return 0;
            }
        }

        private static bool GenerateHasRings(
            PlanetType type,
            ref QuantumRandom quantumRandom)
        {
            switch (type)
            {
                case PlanetType.GasGiant:
                    return quantumRandom.NextDouble() < 0.38;

                case PlanetType.IceGiant:
                    return quantumRandom.NextDouble() < 0.22;

                case PlanetType.Terrestrial:
                case PlanetType.Ocean:
                    return quantumRandom.NextDouble() < 0.01;

                case PlanetType.DwarfPlanet:
                    return quantumRandom.NextDouble() < 0.03;

                default:
                    return false;
            }
        }

        private static OrbitData GenerateOrbit(
            double orbitAu,
            ref QuantumRandom quantumRandom)
        {
            return new OrbitData(
                orbitAu * AstronomicalUnitMeters,
                quantumRandom.NextDouble(0.0, 0.18),
                quantumRandom.NextDouble(0.0, DegreesToRadians(7.0)),
                quantumRandom.NextDouble(0.0, Math.PI * 2.0),
                quantumRandom.NextDouble(0.0, Math.PI * 2.0),
                quantumRandom.NextDouble(0.0, Math.PI * 2.0),
                0.0);
        }

        private static double GetInnerOrbitAu(
            double totalLuminositySolar,
            FixedList512Bytes<StarData> stars)
        {
            var luminosityInnerOrbitAu = Math.Max(
                0.05,
                0.12 * Math.Sqrt(totalLuminositySolar));

            var circumbinaryInnerOrbitAu = GetCircumbinaryInnerOrbitAu(stars);

            return Math.Max(
                luminosityInnerOrbitAu,
                circumbinaryInnerOrbitAu);
        }

        private static double GetCircumbinaryInnerOrbitAu(
            FixedList512Bytes<StarData> stars)
        {
            if (stars.Length < 2)
                return 0.0;

            var firstOrbit = stars[0].BarycentricOrbit;
            var secondOrbit = stars[1].BarycentricOrbit;

            var binarySemiMajorAxisMeters =
                firstOrbit.SemiMajorAxisMeters +
                secondOrbit.SemiMajorAxisMeters;

            if (binarySemiMajorAxisMeters <= 0.0)
                return 0.0;

            var binaryEccentricity = Math.Max(
                firstOrbit.Eccentricity,
                secondOrbit.Eccentricity);

            var stableDistanceMeters =
                binarySemiMajorAxisMeters *
                (3.0 + 2.0 * binaryEccentricity);

            return stableDistanceMeters / AstronomicalUnitMeters;
        }

        private static double GetOuterOrbitAu(
            double totalLuminositySolar,
            double totalMassKg,
            double innerOrbitAu)
        {
            var massSolar = totalMassKg / SolarMassKg;

            var naturalOuterOrbitAu = Math.Max(
                8.0,
                35.0 *
                Math.Sqrt(totalLuminositySolar) *
                Math.Sqrt(massSolar));

            return Math.Max(
                naturalOuterOrbitAu,
                innerOrbitAu * 16.0);
        }

        private static double GetSnowLineAu(
            double totalLuminositySolar)
        {
            return Math.Max(
                0.35,
                2.7 * Math.Sqrt(totalLuminositySolar));
        }

        private static double GetEquilibriumTemperatureKelvin(
            double totalLuminositySolar,
            double orbitAu)
        {
            return 278.5 *
                   Math.Pow(totalLuminositySolar, 0.25) /
                   Math.Sqrt(orbitAu);
        }

        private static double GetRadiusMeters(
            double massKg,
            double densityKgPerCubicMeter)
        {
            var volume = massKg / densityKgPerCubicMeter;

            return Math.Pow(
                3.0 * volume /
                (4.0 * Math.PI),
                1.0 / 3.0);
        }

        private static double GetSurfaceGravity(
            double massKg,
            double radiusMeters)
        {
            return GravitationalConstant *
                   massKg /
                   (radiusMeters * radiusMeters);
        }

        private static double GetTotalLuminositySolar(
            FixedList512Bytes<StarData> stars)
        {
            var luminosityWatts = 0.0;

            for (var i = 0; i < stars.Length; i++)
                luminosityWatts += stars[i].LuminosityWatts;

            return luminosityWatts / SolarLuminosityWatts;
        }

        private static double GetTotalMassKg(
            FixedList512Bytes<StarData> stars)
        {
            var massKg = 0.0;

            for (var i = 0; i < stars.Length; i++)
                massKg += stars[i].MassKg;

            return massKg;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}