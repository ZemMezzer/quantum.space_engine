using SpaceEngine.Runtime.Data;

namespace SpaceEngine.Runtime.Content.Generation
{
    /// <summary>
    /// Immutable request for one system object. The selected solar-system
    /// generator owns the object's slot and initial orbit; the object generator
    /// only decides whether it fits and returns its own data.
    /// </summary>
    public readonly struct StellarObjectGenerationContext
    {
        public readonly CoordinatesData Coordinates;
        public readonly ulong SystemSeed;
        public readonly int ObjectIndex;
        public readonly OrbitData Orbit;

        private readonly ulong selectionKey;

        public StellarObjectGenerationContext(
            CoordinatesData coordinates,
            ulong systemSeed,
            int objectIndex,
            OrbitData orbit,
            ulong selectionKey = 0UL)
        {
            Coordinates = coordinates;
            SystemSeed = systemSeed;
            ObjectIndex = objectIndex;
            Orbit = orbit;
            this.selectionKey = selectionKey;
        }

        public float GetSelectionWeight(float relativeChance)
        {
            return SelectionWeightUtility.GetDeterministicSelectionWeight(
                SystemSeed ^ ((ulong)(uint)ObjectIndex *
                              0x9E3779B97F4A7C15UL),
                selectionKey,
                relativeChance);
        }

        internal StellarObjectGenerationContext WithSelectionKey(
            ulong candidateKey)
        {
            return new StellarObjectGenerationContext(
                Coordinates,
                SystemSeed,
                ObjectIndex,
                Orbit,
                candidateKey);
        }
    }
}
