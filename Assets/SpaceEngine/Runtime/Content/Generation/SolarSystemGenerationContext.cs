using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Data;

namespace SpaceEngine.Runtime.Content.Generation
{
    /// <summary>
    /// Immutable input for choosing and generating one complete solar system.
    /// Generator/entity bindings are supplied only as content references; a
    /// SolarSystemGenerator owns all mutable structural decisions.
    /// </summary>
    public readonly struct SolarSystemGenerationContext
    {
        public readonly CoordinatesData Coordinates;
        public readonly ulong SystemSeed;
        public readonly IReadOnlyList<StellarObjectGeneratorBinding>
            StellarObjectGenerators;
        public readonly IReadOnlyList<PlanetGeneratorBinding> PlanetGenerators;

        private readonly ulong selectionKey;

        public SolarSystemGenerationContext(
            CoordinatesData coordinates,
            ulong systemSeed,
            IReadOnlyList<StellarObjectGeneratorBinding>
                stellarObjectGenerators,
            IReadOnlyList<PlanetGeneratorBinding> planetGenerators,
            ulong selectionKey = 0UL)
        {
            Coordinates = coordinates;
            SystemSeed = systemSeed;
            StellarObjectGenerators = stellarObjectGenerators;
            PlanetGenerators = planetGenerators;
            this.selectionKey = selectionKey;
        }

        public float GetSelectionWeight(float relativeChance)
        {
            return SelectionWeightUtility.GetDeterministicSelectionWeight(
                SystemSeed,
                selectionKey,
                relativeChance);
        }

        internal SolarSystemGenerationContext WithSelectionKey(
            ulong candidateKey)
        {
            return new SolarSystemGenerationContext(
                Coordinates,
                SystemSeed,
                StellarObjectGenerators,
                PlanetGenerators,
                candidateKey);
        }
    }
}
