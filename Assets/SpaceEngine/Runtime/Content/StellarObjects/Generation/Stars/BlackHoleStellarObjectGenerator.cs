using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    [CreateAssetMenu(
        fileName = "Black Hole Generator",
        menuName = "Space Engine/Stellar Objects/Stars/Generators/Black Hole")]
    public sealed class BlackHoleStellarObjectGenerator : StellarObjectGenerator
    {
        private const float RELATIVE_WEIGHT = 0.002f;

        public override float GetWeight(
            in StellarObjectGenerationContext context)
        {
            // The coordinate convention is all this generator needs. Core code
            // never carries a separate "galactic core" flag.
            if (context.Coordinates.SolarSystemID == 0L)
                return context.ObjectIndex == 0 ? 1.0f : 0.0f;

            return context.GetSelectionWeight(RELATIVE_WEIGHT);
        }

        public override StellarObjectData Generate(
            in StellarObjectGenerationContext context,
            ref QuantumRandom random)
        {
            var isGalaxyCentre = context.Coordinates.SolarSystemID == 0L;
            var massSolar = isGalaxyCentre
                ? random.NextDouble(1_000_000.0, 12_000_000.0)
                : random.NextDouble(3.0, 30.0);
            var massKg = massSolar * SolarConstants.SOLAR_MASS_KG;
            var ageYears = random.NextDouble(
                10_000_000.0,
                StellarObjectGenerationUtility.MaximumUniverseAgeYears);
            var metallicity = random.NextDouble(0.01, 1.5);
            var hasAccretionDisk = isGalaxyCentre || HasAccretionDisk(
                massSolar,
                ageYears,
                metallicity);

            return StellarObjectGenerationUtility.CreateBlackHole(
                massKg,
                StellarObjectGenerationUtility.GetSchwarzschildRadiusMeters(massKg),
                random.NextDouble(0.001, 10.0),
                ageYears,
                metallicity,
                hasAccretionDisk,
                context.Orbit);
        }

        private static bool HasAccretionDisk(
            double massSolar,
            double ageYears,
            double metallicity)
        {
            unchecked
            {
                var hash = (ulong)BitConverter.DoubleToInt64Bits(massSolar);
                hash ^= (ulong)BitConverter.DoubleToInt64Bits(ageYears) *
                        0x9E3779B97F4A7C15UL;
                hash ^= (ulong)BitConverter.DoubleToInt64Bits(metallicity) *
                        0xD1B54A32D192ED03UL;
                hash ^= hash >> 30;
                hash *= 0xBF58476D1CE4E5B9UL;
                hash ^= hash >> 27;
                hash *= 0x94D049BB133111EBUL;
                hash ^= hash >> 31;
                return (hash >> 40) / (double)(1UL << 24) < 0.58;
            }
        }
    }
}
