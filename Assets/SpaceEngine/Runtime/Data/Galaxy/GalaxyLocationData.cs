using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Lightweight map record for one galaxy. The generator is resolved again
    /// from the deterministic coordinate request and its GetWeight values; no
    /// preset or definition id is persisted here.
    /// </summary>
    public readonly struct GalaxyLocationData
    {
        public readonly long GalaxyID;
        public readonly double3 UniversePositionLightYears;
        public readonly double RadiusLightYears;

        public GalaxyLocationData(
            long galaxyID,
            double3 universePositionLightYears,
            double radiusLightYears)
        {
            GalaxyID = galaxyID;
            UniversePositionLightYears = universePositionLightYears;
            RadiusLightYears = radiusLightYears;
        }
    }
}
