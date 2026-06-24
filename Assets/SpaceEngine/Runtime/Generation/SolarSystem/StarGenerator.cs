using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;

namespace SpaceEngine.Runtime.Generation.SolarSystem
{
    /// <summary>
    /// Generates deterministic physical data for stars and compact objects.
    /// </summary>
    internal static class StarGenerator
    {
        private const double SolarMassKg = 1.98847e30;
        private const double SolarRadiusMeters = 6.957e8;
        private const double SolarLuminosityWatts = 3.828e26;
        private const double SolarTemperatureKelvin = 5772.0;

        private const double SpeedOfLightMetersPerSecond = 299_792_458.0;
        private const double GravitationalConstant = 6.67430e-11;

        private const double MinimumAgeYears = 50_000_000.0;
        private const double MaximumUniverseAgeYears = 13_500_000_000.0;

        private struct StarPrototype
        {
            public StarType Type;
            public double MassSolar;
            public double RadiusSolar;
            public double TemperatureKelvin;
            public double LuminositySolar;
            public double AgeYears;
            public double Metallicity;
            public double RotationPeriodSeconds;
        }

        internal static StarData GeneratePrimary(
            ref QuantumRandom quantumRandom,
            OrbitData barycentricOrbit)
        {
            var prototype = GeneratePrimaryPrototype(ref quantumRandom);

            return CreateStar(
                prototype,
                barycentricOrbit);
        }

        internal static StarData GenerateCompanion(
            ref QuantumRandom quantumRandom,
            StarData primaryStar,
            OrbitData barycentricOrbit)
        {
            var primaryMassSolar = primaryStar.MassKg / SolarMassKg;

            var prototype = GenerateCompanionPrototype(
                ref quantumRandom,
                primaryMassSolar,
                primaryStar.AgeYears,
                primaryStar.Metallicity);

            return CreateStar(
                prototype,
                barycentricOrbit);
        }

        internal static double GenerateCompanionMassSolar(
            ref QuantumRandom quantumRandom,
            double primaryMassSolar)
        {
            var massRatio =
                0.1 +
                0.9 *
                Math.Pow(
                    quantumRandom.NextDouble(),
                    0.65);

            var companionMassSolar =
                primaryMassSolar * massRatio;

            return Math.Max(
                0.08,
                companionMassSolar);
        }

        private static StarPrototype GeneratePrimaryPrototype(
            ref QuantumRandom quantumRandom)
        {
            var type = GeneratePrimaryType(ref quantumRandom);

            switch (type)
            {
                case StarType.WhiteDwarf:
                    return CreateWhiteDwarfPrototype(
                        ref quantumRandom);

                case StarType.RedGiant:
                    return CreateRedGiantPrototype(
                        ref quantumRandom);

                case StarType.NeutronStar:
                    return CreateNeutronStarPrototype(
                        ref quantumRandom);

                case StarType.Pulsar:
                    return CreatePulsarPrototype(
                        ref quantumRandom);

                case StarType.BlackHole:
                    return CreateBlackHolePrototype(
                        ref quantumRandom);

                default:
                    return CreateMainSequencePrimaryPrototype(
                        type,
                        ref quantumRandom);
            }
        }

        private static StarPrototype GenerateCompanionPrototype(
            ref QuantumRandom quantumRandom,
            double primaryMassSolar,
            double systemAgeYears,
            double metallicity)
        {
            var massSolar = GenerateCompanionMassSolar(
                ref quantumRandom,
                primaryMassSolar);

            var maximumAgeYears =
                GetMainSequenceLifetimeYears(massSolar);

            var ageYears = Math.Min(
                systemAgeYears,
                maximumAgeYears);

            return CreateMainSequencePrototype(
                GetMainSequenceType(massSolar),
                massSolar,
                ageYears,
                metallicity,
                ref quantumRandom);
        }

        private static StarType GeneratePrimaryType(
            ref QuantumRandom quantumRandom)
        {
            var roll = quantumRandom.NextDouble();

            if (roll < 0.650)
                return StarType.RedDwarf;

            if (roll < 0.830)
                return StarType.OrangeDwarf;

            if (roll < 0.930)
                return StarType.YellowDwarf;

            if (roll < 0.970)
                return StarType.WhiteDwarf;

            if (roll < 0.990)
                return StarType.RedGiant;

            if (roll < 0.996)
                return StarType.NeutronStar;

            if (roll < 0.998)
                return StarType.Pulsar;

            return StarType.BlackHole;
        }

        private static StarPrototype CreateMainSequencePrimaryPrototype(
            StarType type,
            ref QuantumRandom quantumRandom)
        {
            var massSolar = GetMassForMainSequenceType(
                type,
                ref quantumRandom);

            var maximumAgeYears =
                GetMainSequenceLifetimeYears(massSolar);

            var ageYears = quantumRandom.NextDouble(
                MinimumAgeYears,
                Math.Max(
                    MinimumAgeYears,
                    maximumAgeYears));

            var metallicity = quantumRandom.NextDouble(
                0.2,
                2.0);

            return CreateMainSequencePrototype(
                type,
                massSolar,
                ageYears,
                metallicity,
                ref quantumRandom);
        }

        private static StarPrototype CreateMainSequencePrototype(
            StarType type,
            double massSolar,
            double ageYears,
            double metallicity,
            ref QuantumRandom quantumRandom)
        {
            var radiusSolar = GetRadiusSolar(massSolar);
            var luminositySolar = GetLuminositySolar(massSolar);

            var temperatureKelvin =
                SolarTemperatureKelvin *
                Math.Pow(
                    luminositySolar /
                    (radiusSolar * radiusSolar),
                    0.25);

            var rotationDays = quantumRandom.NextDouble(
                5.0,
                45.0);

            return new StarPrototype
            {
                Type = type,
                MassSolar = massSolar,
                RadiusSolar = radiusSolar,
                TemperatureKelvin = temperatureKelvin,
                LuminositySolar = luminositySolar,
                AgeYears = ageYears,
                Metallicity = metallicity,
                RotationPeriodSeconds = rotationDays * 86_400.0
            };
        }

        private static StarPrototype CreateWhiteDwarfPrototype(
            ref QuantumRandom quantumRandom)
        {
            var massSolar = quantumRandom.NextDouble(
                0.50,
                1.30);

            var radiusSolar = quantumRandom.NextDouble(
                0.008,
                0.020);

            var temperatureKelvin = quantumRandom.NextDouble(
                4_000.0,
                100_000.0);

            var luminositySolar =
                radiusSolar *
                radiusSolar *
                Math.Pow(
                    temperatureKelvin /
                    SolarTemperatureKelvin,
                    4.0);

            return new StarPrototype
            {
                Type = StarType.WhiteDwarf,
                MassSolar = massSolar,
                RadiusSolar = radiusSolar,
                TemperatureKelvin = temperatureKelvin,
                LuminositySolar = luminositySolar,
                AgeYears = quantumRandom.NextDouble(
                    1_000_000_000.0,
                    MaximumUniverseAgeYears),
                Metallicity = quantumRandom.NextDouble(
                    0.1,
                    2.0),
                RotationPeriodSeconds = quantumRandom.NextDouble(
                    3_600.0,
                    7.0 * 86_400.0)
            };
        }

        private static StarPrototype CreateRedGiantPrototype(
            ref QuantumRandom quantumRandom)
        {
            var massSolar = quantumRandom.NextDouble(
                0.8,
                8.0);

            var radiusSolar = quantumRandom.NextDouble(
                10.0,
                120.0);

            var temperatureKelvin = quantumRandom.NextDouble(
                3_000.0,
                5_500.0);

            var luminositySolar =
                radiusSolar *
                radiusSolar *
                Math.Pow(
                    temperatureKelvin /
                    SolarTemperatureKelvin,
                    4.0);

            var maximumLifetimeYears =
                GetMainSequenceLifetimeYears(massSolar);

            return new StarPrototype
            {
                Type = StarType.RedGiant,
                MassSolar = massSolar,
                RadiusSolar = radiusSolar,
                TemperatureKelvin = temperatureKelvin,
                LuminositySolar = luminositySolar,
                AgeYears = quantumRandom.NextDouble(
                    Math.Max(
                        500_000_000.0,
                        maximumLifetimeYears * 0.75),
                    Math.Min(
                        MaximumUniverseAgeYears,
                        maximumLifetimeYears * 1.10)),
                Metallicity = quantumRandom.NextDouble(
                    0.1,
                    2.0),
                RotationPeriodSeconds = quantumRandom.NextDouble(
                    50.0 * 86_400.0,
                    600.0 * 86_400.0)
            };
        }

        private static StarPrototype CreateNeutronStarPrototype(
            ref QuantumRandom quantumRandom)
        {
            return CreateCompactStarPrototype(
                StarType.NeutronStar,
                ref quantumRandom,
                minimumRotationSeconds: 0.05,
                maximumRotationSeconds: 30.0,
                minimumTemperatureKelvin: 100_000.0,
                maximumTemperatureKelvin: 2_000_000.0);
        }

        private static StarPrototype CreatePulsarPrototype(
            ref QuantumRandom quantumRandom)
        {
            return CreateCompactStarPrototype(
                StarType.Pulsar,
                ref quantumRandom,
                minimumRotationSeconds: 0.001,
                maximumRotationSeconds: 2.0,
                minimumTemperatureKelvin: 500_000.0,
                maximumTemperatureKelvin: 5_000_000.0);
        }

        private static StarPrototype CreateCompactStarPrototype(
            StarType type,
            ref QuantumRandom quantumRandom,
            double minimumRotationSeconds,
            double maximumRotationSeconds,
            double minimumTemperatureKelvin,
            double maximumTemperatureKelvin)
        {
            var massSolar = quantumRandom.NextDouble(
                1.10,
                2.20);

            var radiusMeters = quantumRandom.NextDouble(
                10_000.0,
                14_000.0);

            var radiusSolar =
                radiusMeters / SolarRadiusMeters;

            var temperatureKelvin = quantumRandom.NextDouble(
                minimumTemperatureKelvin,
                maximumTemperatureKelvin);

            var luminositySolar =
                radiusSolar *
                radiusSolar *
                Math.Pow(
                    temperatureKelvin /
                    SolarTemperatureKelvin,
                    4.0);

            return new StarPrototype
            {
                Type = type,
                MassSolar = massSolar,
                RadiusSolar = radiusSolar,
                TemperatureKelvin = temperatureKelvin,
                LuminositySolar = luminositySolar,
                AgeYears = quantumRandom.NextDouble(
                    10_000_000.0,
                    MaximumUniverseAgeYears),
                Metallicity = quantumRandom.NextDouble(
                    0.01,
                    1.5),
                RotationPeriodSeconds = quantumRandom.NextDouble(
                    minimumRotationSeconds,
                    maximumRotationSeconds)
            };
        }

        private static StarPrototype CreateBlackHolePrototype(
            ref QuantumRandom quantumRandom)
        {
            var massSolar = quantumRandom.NextDouble(
                3.0,
                30.0);

            var radiusMeters = GetSchwarzschildRadiusMeters(
                massSolar * SolarMassKg);

            return new StarPrototype
            {
                Type = StarType.BlackHole,
                MassSolar = massSolar,
                RadiusSolar = radiusMeters / SolarRadiusMeters,
                TemperatureKelvin = 0.0,
                LuminositySolar = 0.0,
                AgeYears = quantumRandom.NextDouble(
                    10_000_000.0,
                    MaximumUniverseAgeYears),
                Metallicity = quantumRandom.NextDouble(
                    0.01,
                    1.5),
                RotationPeriodSeconds = quantumRandom.NextDouble(
                    0.001,
                    10.0)
            };
        }

        private static StarData CreateStar(
            StarPrototype prototype,
            OrbitData barycentricOrbit)
        {
            return new StarData(
                prototype.Type,
                prototype.MassSolar * SolarMassKg,
                prototype.RadiusSolar * SolarRadiusMeters,
                prototype.TemperatureKelvin,
                prototype.LuminositySolar * SolarLuminosityWatts,
                prototype.AgeYears,
                prototype.Metallicity,
                prototype.RotationPeriodSeconds,
                barycentricOrbit);
        }

        private static double GetMassForMainSequenceType(
            StarType type,
            ref QuantumRandom quantumRandom)
        {
            switch (type)
            {
                case StarType.RedDwarf:
                    return Lerp(
                        0.08,
                        0.60,
                        Math.Pow(
                            quantumRandom.NextDouble(),
                            1.8));

                case StarType.OrangeDwarf:
                    return quantumRandom.NextDouble(
                        0.60,
                        0.90);

                case StarType.YellowDwarf:
                    return quantumRandom.NextDouble(
                        0.90,
                        2.50);

                default:
                    return quantumRandom.NextDouble(
                        0.08,
                        1.00);
            }
        }

        private static StarType GetMainSequenceType(
            double massSolar)
        {
            if (massSolar < 0.60)
                return StarType.RedDwarf;

            if (massSolar < 0.90)
                return StarType.OrangeDwarf;

            return StarType.YellowDwarf;
        }

        private static double GetLuminositySolar(
            double massSolar)
        {
            if (massSolar < 0.43)
                return 0.23 * Math.Pow(
                    massSolar,
                    2.3);

            if (massSolar < 2.0)
                return Math.Pow(
                    massSolar,
                    4.0);

            return 1.5 * Math.Pow(
                massSolar,
                3.5);
        }

        private static double GetRadiusSolar(
            double massSolar)
        {
            if (massSolar < 1.0)
                return Math.Pow(
                    massSolar,
                    0.8);

            return Math.Pow(
                massSolar,
                0.57);
        }

        private static double GetMainSequenceLifetimeYears(
            double massSolar)
        {
            var years =
                10_000_000_000.0 /
                Math.Pow(
                    massSolar,
                    2.5);

            return Math.Min(
                years,
                MaximumUniverseAgeYears);
        }

        private static double GetSchwarzschildRadiusMeters(
            double massKg)
        {
            return 2.0 *
                   GravitationalConstant *
                   massKg /
                   (SpeedOfLightMetersPerSecond *
                    SpeedOfLightMetersPerSecond);
        }

        private static double Lerp(
            double min,
            double max,
            double t)
        {
            return min + (max - min) * t;
        }
    }
}