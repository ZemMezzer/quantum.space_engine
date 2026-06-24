using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Lightweight universe-map record for one existing galaxy.
    /// </summary>
    public readonly struct GalaxyLocationData
    {
        public readonly ulong GalaxyID;
        public readonly GalaxyType Type;
        public readonly double3 UniversePositionLightYears;
        public readonly double RadiusLightYears;

        internal GalaxyLocationData(
            ulong galaxyID,
            GalaxyType type,
            double3 universePositionLightYears,
            double radiusLightYears)
        {
            GalaxyID = galaxyID;
            Type = type;
            UniversePositionLightYears = universePositionLightYears;
            RadiusLightYears = radiusLightYears;
        }
    }
}
