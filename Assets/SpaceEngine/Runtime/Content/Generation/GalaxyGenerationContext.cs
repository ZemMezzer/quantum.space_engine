using Unity.Mathematics;

namespace SpaceEngine.Runtime.Content.Generation
{
    /// <summary>
    /// Pure deterministic request for one galaxy location. The temporary
    /// selection key exists only while GetWeight is evaluated; no generated
    /// data stores generator ids or preset ids.
    /// </summary>
    public readonly struct GalaxyGenerationContext
    {
        public readonly long UniverseID;
        public readonly long GalaxyID;
        public readonly ulong Seed;
        public readonly double3 UniversePositionLightYears;
        private readonly ulong selectionKey;

        public GalaxyGenerationContext(
            long universeID,
            long galaxyID,
            ulong seed,
            double3 universePositionLightYears,
            ulong selectionKey = 0UL)
        {
            UniverseID = universeID;
            GalaxyID = galaxyID;
            Seed = seed;
            UniversePositionLightYears = universePositionLightYears;
            this.selectionKey = selectionKey;
        }

        public float GetSelectionWeight(float relativeChance)
        {
            return SelectionWeightUtility.GetDeterministicSelectionWeight(
                Seed,
                selectionKey,
                relativeChance);
        }

        internal GalaxyGenerationContext WithSelectionKey(ulong candidateKey)
        {
            return new GalaxyGenerationContext(
                UniverseID,
                GalaxyID,
                Seed,
                UniversePositionLightYears,
                candidateKey);
        }
    }
}
