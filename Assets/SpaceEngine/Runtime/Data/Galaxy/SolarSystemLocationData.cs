using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Lightweight record for one existing stellar system in a galaxy-sector.
    /// Position is relative to the galaxy centre, in light-years.
    /// </summary>
    public readonly struct SolarSystemLocationData
    {
        public readonly ulong SolarSystemID;
        public readonly double3 GalaxyLocalPositionLightYears;
        public readonly double EstimatedSystemMassSolarMasses;

        internal SolarSystemLocationData(
            ulong solarSystemID,
            double3 galaxyLocalPositionLightYears,
            double estimatedSystemMassSolarMasses)
        {
            SolarSystemID = solarSystemID;
            GalaxyLocalPositionLightYears = galaxyLocalPositionLightYears;
            EstimatedSystemMassSolarMasses = estimatedSystemMassSolarMasses;
        }
    }
}
