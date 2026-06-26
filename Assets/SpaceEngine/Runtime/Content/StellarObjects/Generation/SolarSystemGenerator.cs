using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation
{
    /// <summary>
    /// Stateless authored generator for one complete solar-system structure.
    ///
    /// It is the only content layer that decides whether a system exists,
    /// how many primary objects it has, their orbits and the orbiting-object
    /// requests. The fixed engine pipeline only selects it by weight and binds
    /// entities selected with its object generators.
    /// </summary>
    public abstract class SolarSystemGenerator : ScriptableObject
    {
        public abstract float GetWeight(
            in SolarSystemGenerationContext context);

        public abstract SolarSystemData Generate(
            in SolarSystemGenerationContext context,
            ref QuantumRandom random);

        /// <summary>
        /// Resolves one primary object through generator/entity bindings. Core
        /// sees only the returned StellarObjectData with its selected Entity.
        /// </summary>
        protected StellarObjectData GenerateStellarObject(
            in SolarSystemGenerationContext systemContext,
            int objectIndex,
            OrbitData orbit,
            ref QuantumRandom random)
        {
            var bindings = systemContext.StellarObjectGenerators;
            if (bindings == null)
                return null;

            var request = new StellarObjectGenerationContext(
                systemContext.Coordinates,
                systemContext.SystemSeed,
                objectIndex,
                orbit);

            StellarObjectGeneratorBinding selected = null;
            var selectedContext = request;
            var greatestWeight = 0.0f;

            for (var index = 0; index < bindings.Count; index++)
            {
                var candidate = bindings[index];
                if (candidate?.Generator == null || candidate.Entity == null)
                    continue;

                var candidateContext = request.WithSelectionKey(
                    (ulong)index + 1UL);
                var weight = Mathf.Clamp01(
                    candidate.Generator.GetWeight(candidateContext));

                if (weight <= greatestWeight)
                    continue;

                greatestWeight = weight;
                selected = candidate;
                selectedContext = candidateContext;
            }

            if (selected == null || greatestWeight <= 0.0f)
                return null;

            var data = selected.Generator.Generate(
                selectedContext,
                ref random);

            return data?.WithEntity(selected.Entity);
        }

        /// <summary>
        /// Resolves one orbiting object emitted by this system structure.
        /// A PlanetGeneratorBinding contains both the content generator and
        /// its queryable StellarEntity identity.
        /// </summary>
        protected StellarObjectData GeneratePlanet(
            in SolarSystemGenerationContext systemContext,
            IReadOnlyList<StellarObjectData> centralObjects,
            int objectIndex,
            int slotIndex,
            double centralMassKg,
            double illuminationLuminosityWatts,
            OrbitData orbit,
            ref QuantumRandom random)
        {
            var bindings = systemContext.PlanetGenerators;
            if (bindings == null)
                return null;

            var request = new PlanetGenerationContext(
                systemContext.Coordinates,
                systemContext.SystemSeed,
                centralObjects,
                objectIndex,
                slotIndex,
                centralMassKg,
                illuminationLuminosityWatts,
                orbit);

            PlanetGeneratorBinding selected = null;
            var selectedContext = request;
            var greatestWeight = 0.0f;

            for (var index = 0; index < bindings.Count; index++)
            {
                var candidate = bindings[index];
                if (candidate?.Generator == null || candidate.Entity == null)
                    continue;

                var candidateContext = request.WithSelectionKey(
                    (ulong)index + 1UL);
                var weight = Mathf.Clamp01(
                    candidate.Generator.GetWeight(candidateContext));

                if (weight <= greatestWeight)
                    continue;

                greatestWeight = weight;
                selected = candidate;
                selectedContext = candidateContext;
            }

            if (selected == null || greatestWeight <= 0.0f)
                return null;

            var data = selected.Generator.Generate(
                selectedContext,
                ref random);

            return data?.WithEntity(selected.Entity);
        }

        protected static ulong MixSeed(ulong seed, ulong salt)
        {
            return StableIdentifierUtility.Mix(
                seed ^ (salt + 0x9E3779B97F4A7C15UL));
        }

        protected static double GetTotalMassKg(
            IReadOnlyList<StellarObjectData> objects)
        {
            if (objects == null)
                return 0.0;

            var massKg = 0.0;
            for (var index = 0; index < objects.Count; index++)
            {
                var data = objects[index];
                if (data != null && data.MassKg > 0.0)
                    massKg += data.MassKg;
            }

            return massKg;
        }

        protected static double GetTotalLuminosityWatts(
            IReadOnlyList<StellarObjectData> objects)
        {
            if (objects == null)
                return 0.0;

            var luminosityWatts = 0.0;
            for (var index = 0; index < objects.Count; index++)
            {
                var data = objects[index];
                if (data != null && data.LuminosityWatts > 0.0)
                    luminosityWatts += data.LuminosityWatts;
            }

            return luminosityWatts;
        }
    }
}
