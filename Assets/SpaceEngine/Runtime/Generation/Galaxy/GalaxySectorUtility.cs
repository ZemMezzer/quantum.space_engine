using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Converts galaxy-local positions to internal streaming sectors.
    /// </summary>
    public static class GalaxySectorUtility
    {
        public static int3 GetCoordinates(
            double3 galaxyLocalPositionLightYears)
        {
            return (int3)math.floor(
                galaxyLocalPositionLightYears /
                GalaxySectorGenerator.SECTOR_SIZE_LIGHT_YEARS);
        }

        public static double3 GetOriginLightYears(
            int3 galaxySectorCoordinates)
        {
            return (double3)galaxySectorCoordinates *
                   GalaxySectorGenerator.SECTOR_SIZE_LIGHT_YEARS;
        }
    }
}
