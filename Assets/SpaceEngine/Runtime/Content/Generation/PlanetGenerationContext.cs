using System.Collections.Generic;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;

namespace SpaceEngine.Runtime.Content.Generation
{
    /// <summary>
    /// Immutable request for an orbiting planetary body. The selected
    /// SolarSystemGenerator creates these requests after it has generated the
    /// system's central structure. It may describe one star, a binary
    /// barycentre, an artificial object or any other source configuration.
    /// </summary>
    public readonly struct PlanetGenerationContext
    {
        public readonly CoordinatesData Coordinates;
        public readonly ulong SystemSeed;
        public readonly IReadOnlyList<StellarObjectData> CentralObjects;
        public readonly int ObjectIndex;
        public readonly int SlotIndex;
        public readonly double CentralMassKg;
        public readonly double IlluminationLuminosityWatts;
        public readonly OrbitData Orbit;

        private readonly ulong selectionKey;

        public PlanetGenerationContext(
            CoordinatesData coordinates,
            ulong systemSeed,
            IReadOnlyList<StellarObjectData> centralObjects,
            int objectIndex,
            int slotIndex,
            double centralMassKg,
            double illuminationLuminosityWatts,
            OrbitData orbit,
            ulong selectionKey = 0UL)
        {
            Coordinates = coordinates;
            SystemSeed = systemSeed;
            CentralObjects = centralObjects;
            ObjectIndex = objectIndex;
            SlotIndex = slotIndex;
            CentralMassKg = centralMassKg;
            IlluminationLuminosityWatts = illuminationLuminosityWatts;
            Orbit = orbit;
            this.selectionKey = selectionKey;
        }

        public float GetSelectionWeight(float relativeChance)
        {
            return SelectionWeightUtility.GetDeterministicSelectionWeight(
                SystemSeed ^ ((ulong)(uint)ObjectIndex *
                              0xD1B54A32D192ED03UL),
                selectionKey,
                relativeChance);
        }

        internal PlanetGenerationContext WithSelectionKey(ulong candidateKey)
        {
            return new PlanetGenerationContext(
                Coordinates,
                SystemSeed,
                CentralObjects,
                ObjectIndex,
                SlotIndex,
                CentralMassKg,
                IlluminationLuminosityWatts,
                Orbit,
                candidateKey);
        }
    }
}
