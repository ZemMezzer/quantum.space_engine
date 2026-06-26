using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Generation.Universe
{
    /// <summary>
    /// Fixed universe-map generation. It determines where galaxy locations
    /// exist, chooses a GalaxyGeneratorBinding by weight and delegates all
    /// galaxy-specific generation to that binding's generator. Core never
    /// interprets the paired StellarEntity.
    /// </summary>
    public static class UniverseGeneration
    {
        public const double SectorSizeLightYears = 10_000_000.0;

        public static UniverseSectorData GenerateSector(
            IReadOnlyList<GalaxyGeneratorBinding> galaxyGenerators,
            long universeID,
            int3 sectorCoordinates)
        {
            if (galaxyGenerators == null)
                throw new ArgumentNullException(nameof(galaxyGenerators));

            var seed = GalaxyIDUtility.GetUniverseSectorSeed(
                universeID,
                sectorCoordinates);
            var locations = new FixedList4096Bytes<GalaxyLocationData>();
            var count = GetGalaxyCount(seed);

            for (var index = 0; index < count; index++)
            {
                var galaxyID = GalaxyIDUtility.CreateGalaxyID(
                    sectorCoordinates,
                    (ushort)index);
                var position = GenerateGalaxyUniversePosition(
                    universeID,
                    sectorCoordinates,
                    (ushort)index);
                var galaxy = GenerateGalaxy(
                    galaxyGenerators,
                    universeID,
                    galaxyID,
                    position);

                locations.Add(new GalaxyLocationData(
                    galaxyID,
                    position,
                    galaxy.RadiusLightYears));
            }

            return new UniverseSectorData(
                seed,
                sectorCoordinates,
                locations);
        }

        public static GalaxyData GenerateGalaxy(
            IReadOnlyList<GalaxyGeneratorBinding> galaxyGenerators,
            long universeID,
            long galaxyID,
            double3 universePositionLightYears)
        {
            var binding = ResolveGalaxyBinding(
                galaxyGenerators,
                universeID,
                galaxyID,
                universePositionLightYears,
                out var selectedContext);
            if (binding?.Generator == null || binding.Entity == null)
            {
                throw new InvalidOperationException(
                    "No configured GalaxyGeneratorBinding returned a positive " +
                    "GetWeight for this galaxy location.");
            }

            var random = new QuantumRandom(selectedContext.Seed);
            var data = binding.Generator.Generate(selectedContext, ref random);
            return data?.WithEntity(binding.Entity);
        }

        public static GalaxyGeneratorBinding ResolveGalaxyBinding(
            IReadOnlyList<GalaxyGeneratorBinding> galaxyGenerators,
            long universeID,
            long galaxyID,
            double3 universePositionLightYears)
        {
            return ResolveGalaxyBinding(
                galaxyGenerators,
                universeID,
                galaxyID,
                universePositionLightYears,
                out _);
        }

        /// <summary>
        /// Compatibility convenience for code that needs to ask the winning
        /// content generator for sector data. Entity selection remains in the
        /// same binding and is attached by GenerateGalaxy.
        /// </summary>
        public static GalaxyGenerator
            ResolveGalaxyGenerator(
                IReadOnlyList<GalaxyGeneratorBinding> galaxyGenerators,
                long universeID,
                long galaxyID,
                double3 universePositionLightYears)
        {
            return ResolveGalaxyBinding(
                galaxyGenerators,
                universeID,
                galaxyID,
                universePositionLightYears)?.Generator;
        }

        public static double3 GenerateGalaxyUniversePosition(
            long universeID,
            int3 sectorCoordinates,
            ushort localGalaxyIndex)
        {
            var sectorSeed = GalaxyIDUtility.GetUniverseSectorSeed(
                universeID,
                sectorCoordinates);
            var slotSeed = GalaxyIDUtility.Combine(
                sectorSeed,
                localGalaxyIndex);
            var random = new QuantumRandom(slotSeed);
            var origin = UniverseSectorUtility.GetOriginLightYears(
                sectorCoordinates);

            return origin + new double3(
                random.NextDouble(0.0, SectorSizeLightYears),
                random.NextDouble(0.0, SectorSizeLightYears),
                random.NextDouble(0.0, SectorSizeLightYears));
        }

        private static GalaxyGeneratorBinding ResolveGalaxyBinding(
            IReadOnlyList<GalaxyGeneratorBinding> galaxyGenerators,
            long universeID,
            long galaxyID,
            double3 universePositionLightYears,
            out GalaxyGenerationContext selectedContext)
        {
            var seed = GalaxyIDUtility.GetGalaxySeed(universeID, galaxyID);
            var context = new GalaxyGenerationContext(
                universeID,
                galaxyID,
                seed,
                universePositionLightYears);
            selectedContext = context;

            if (galaxyGenerators == null)
                return null;

            GalaxyGeneratorBinding selected = null;
            var greatestWeight = 0.0f;

            for (var index = 0; index < galaxyGenerators.Count; index++)
            {
                var candidate = galaxyGenerators[index];
                if (candidate?.Generator == null || candidate.Entity == null)
                    continue;

                var candidateContext = context.WithSelectionKey(
                    (ulong)index + 1UL);
                var weight = Mathf.Clamp01(
                    candidate.Generator.GetWeight(candidateContext));
                if (weight <= greatestWeight)
                    continue;

                greatestWeight = weight;
                selected = candidate;
                selectedContext = candidateContext;
            }

            return greatestWeight > 0.0f ? selected : null;
        }

        private static int GetGalaxyCount(ulong sectorSeed)
        {
            var random = new QuantumRandom(GalaxyIDUtility.Combine(
                sectorSeed,
                0x47414C4158595F43UL));
            var densityRoll = random.NextDouble();

            if (densityRoll < 0.42)
                return 0;
            if (densityRoll < 0.76)
                return random.NextInt(1, 4);
            if (densityRoll < 0.94)
                return random.NextInt(4, 11);

            return random.NextInt(11, 25);
        }
    }
}
