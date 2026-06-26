using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Content.StellarObjects.Generation;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Generation.SolarSystem
{
    /// <summary>
    /// Fixed engine-side selection pipeline for complete solar-system
    /// generators. It knows no system types, object types, binary branch or
    /// planet policy. All content variation is returned by the winning
    /// SolarSystemGenerator and its generator/entity bindings.
    /// </summary>
    public static class SolarSystemGeneration
    {
        private const ulong SystemGeneratorSeedSalt =
            0x534F4C41525F5359UL;

        public static bool TryGenerate(
            in CoordinatesData coordinates,
            IReadOnlyList<SolarSystemGenerator> solarSystemGenerators,
            IReadOnlyList<StellarObjectGeneratorBinding>
                stellarObjectGenerators,
            IReadOnlyList<PlanetGeneratorBinding> planetGenerators,
            out SolarSystemData solarSystem)
        {
            var systemSeed = coordinates.GetSolarSystemSeed();
            var context = new SolarSystemGenerationContext(
                coordinates,
                systemSeed,
                stellarObjectGenerators,
                planetGenerators);

            var generator = SelectGenerator(
                solarSystemGenerators,
                context,
                out var selectedContext);

            if (generator == null)
            {
                solarSystem = null;
                return false;
            }

            var random = new QuantumRandom(
                StableIdentifierUtility.Mix(
                    systemSeed ^ SystemGeneratorSeedSalt));

            solarSystem = generator.Generate(selectedContext, ref random);

            return solarSystem != null &&
                   solarSystem.StellarObjects != null &&
                   solarSystem.StellarObjects.Length > 0;
        }

        public static SolarSystemData Generate(
            in CoordinatesData coordinates,
            IReadOnlyList<SolarSystemGenerator> solarSystemGenerators,
            IReadOnlyList<StellarObjectGeneratorBinding>
                stellarObjectGenerators,
            IReadOnlyList<PlanetGeneratorBinding> planetGenerators)
        {
            if (TryGenerate(
                    coordinates,
                    solarSystemGenerators,
                    stellarObjectGenerators,
                    planetGenerators,
                    out var solarSystem))
            {
                return solarSystem;
            }

            throw new InvalidOperationException(
                "No configured SolarSystemGenerator returned a positive " +
                "GetWeight and generated data for this solar-system address.");
        }

        public static double GetTotalSystemMassKg(
            SolarSystemData solarSystem)
        {
            if (solarSystem?.StellarObjects == null)
                return 0.0;

            var totalMassKg = 0.0;
            for (var index = 0; index < solarSystem.StellarObjects.Length;
                 index++)
            {
                var body = solarSystem.StellarObjects[index];
                if (body != null && body.MassKg > 0.0)
                    totalMassKg += body.MassKg;
            }

            return totalMassKg;
        }

        private static SolarSystemGenerator SelectGenerator(
            IReadOnlyList<SolarSystemGenerator> generators,
            in SolarSystemGenerationContext context,
            out SolarSystemGenerationContext selectedContext)
        {
            selectedContext = context;
            if (generators == null)
                return null;

            SolarSystemGenerator selected = null;
            var greatestWeight = 0.0f;

            for (var index = 0; index < generators.Count; index++)
            {
                var candidate = generators[index];
                if (candidate == null)
                    continue;

                var candidateContext = context.WithSelectionKey(
                    (ulong)index + 1UL);
                var weight = Mathf.Clamp01(
                    candidate.GetWeight(candidateContext));

                if (weight <= greatestWeight)
                    continue;

                greatestWeight = weight;
                selected = candidate;
                selectedContext = candidateContext;
            }

            return greatestWeight > 0.0f ? selected : null;
        }
    }
}
