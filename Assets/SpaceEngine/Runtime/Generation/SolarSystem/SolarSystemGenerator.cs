using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.Planet;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;

namespace SpaceEngine.Runtime.Generation.SolarSystem
{
    /// <summary>
    /// Generates deterministic single and binary stellar systems with planets.
    /// </summary>
    public static class SolarSystemGenerator
    {
        private const ulong StarSeedSalt = 0x535441525F534545UL;
        private const ulong CompanionSeedSalt = 0x434F4D50414E494FUL;
        private const ulong OrbitSeedSalt = 0x4F524249545F5345UL;
        private const ulong PlanetSeedSalt = 0x504C414E45545F53UL;
        private const ulong SystemLayoutSeedSalt = 0x4C41594F55545F53UL;

        private const double AstronomicalUnitMeters = 149_597_870_700.0;
        private const double SolarMassKg = 1.98847e30;

        public static SolarSystemData Generate(in CoordinatesData coordinates)
        {
            var systemSeed = coordinates.GetSolarSystemSeed();

            var layoutRandom = new QuantumRandom(
                CombineSeed(systemSeed, SystemLayoutSeedSalt));

            var primaryRandom = new QuantumRandom(
                CombineSeed(systemSeed, StarSeedSalt));

            var stars = new FixedList512Bytes<StarData>();

            var primaryStar = StarGenerator.GeneratePrimary(
                ref primaryRandom,
                default);

            stars.Add(primaryStar);

            if (ShouldGenerateCompanion(ref layoutRandom, primaryStar))
            {
                var companionRandom = new QuantumRandom(
                    CombineSeed(systemSeed, CompanionSeedSalt));

                var orbitRandom = new QuantumRandom(
                    CombineSeed(systemSeed, OrbitSeedSalt));

                var companionMassSolar =
                    StarGenerator.GenerateCompanionMassSolar(
                        ref companionRandom,
                        primaryStar.MassKg / SolarMassKg);

                CreateBinaryBarycentricOrbits(
                    ref orbitRandom,
                    primaryStar.MassKg,
                    companionMassSolar * SolarMassKg,
                    out var primaryOrbit,
                    out var companionOrbit);

                primaryRandom = new QuantumRandom(
                    CombineSeed(systemSeed, StarSeedSalt));

                primaryStar = StarGenerator.GeneratePrimary(
                    ref primaryRandom,
                    primaryOrbit);

                companionRandom = new QuantumRandom(
                    CombineSeed(systemSeed, CompanionSeedSalt));

                var companionStar = StarGenerator.GenerateCompanion(
                    ref companionRandom,
                    primaryStar,
                    companionOrbit);

                stars = default;
                stars.Add(primaryStar);
                stars.Add(companionStar);
            }

            var planetRandom = new QuantumRandom(
                CombineSeed(systemSeed, PlanetSeedSalt));

            var planets = PlanetGenerator.Generate(
                ref planetRandom,
                stars);

            return new SolarSystemData(
                systemSeed,
                stars,
                planets);
        }

        private static bool ShouldGenerateCompanion(
            ref QuantumRandom quantumRandom,
            StarData primaryStar)
        {
            var primaryMassSolar =
                primaryStar.MassKg / SolarMassKg;

            var companionChance = Clamp(
                0.35 + primaryMassSolar * 0.20,
                0.35,
                0.75);

            return quantumRandom.NextDouble() < companionChance;
        }

        private static void CreateBinaryBarycentricOrbits(
            ref QuantumRandom quantumRandom,
            double primaryMassKg,
            double companionMassKg,
            out OrbitData primaryOrbit,
            out OrbitData companionOrbit)
        {
            var totalMassKg =
                primaryMassKg + companionMassKg;

            var separationMeters =
                quantumRandom.NextDouble(
                    0.05 * AstronomicalUnitMeters,
                    30.0 * AstronomicalUnitMeters);

            var eccentricity =
                quantumRandom.NextDouble(0.0, 0.45);

            var inclinationRadians =
                quantumRandom.NextDouble(
                    0.0,
                    Math.PI * 0.25);

            var argumentOfPeriapsisRadians =
                quantumRandom.NextDouble(
                    0.0,
                    Math.PI * 2.0);

            var ascendingNodeRadians =
                quantumRandom.NextDouble(
                    0.0,
                    Math.PI * 2.0);

            var primarySemiMajorAxis =
                separationMeters *
                companionMassKg /
                totalMassKg;

            var companionSemiMajorAxis =
                separationMeters *
                primaryMassKg /
                totalMassKg;

            primaryOrbit = new OrbitData(
                primarySemiMajorAxis,
                eccentricity,
                inclinationRadians,
                argumentOfPeriapsisRadians,
                ascendingNodeRadians,
                0.0,
                0.0);

            companionOrbit = new OrbitData(
                companionSemiMajorAxis,
                eccentricity,
                inclinationRadians,
                argumentOfPeriapsisRadians,
                ascendingNodeRadians,
                Math.PI,
                0.0);
        }

        private static ulong CombineSeed(
            ulong seed,
            ulong salt)
        {
            unchecked
            {
                var value =
                    seed ^
                    (salt + 0x9E3779B97F4A7C15UL);

                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;

                return value;
            }
        }

        private static double Clamp(
            double value,
            double min,
            double max)
        {
            if (value < min)
                return min;

            return value > max
                ? max
                : value;
        }
    }
}