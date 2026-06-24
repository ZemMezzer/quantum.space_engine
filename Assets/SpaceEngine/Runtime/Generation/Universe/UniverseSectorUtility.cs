using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Universe
{
    /// <summary>
    /// Converts exact universe-space coordinates to internal streaming sectors.
    /// </summary>
    public static class UniverseSectorUtility
    {
        public static int3 GetCoordinates(
            double3 universePositionLightYears)
        {
            return (int3)math.floor(
                universePositionLightYears /
                UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS);
        }

        public static double3 GetOriginLightYears(
            int3 universeSectorCoordinates)
        {
            return (double3)universeSectorCoordinates *
                   UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS;
        }
    }
}
