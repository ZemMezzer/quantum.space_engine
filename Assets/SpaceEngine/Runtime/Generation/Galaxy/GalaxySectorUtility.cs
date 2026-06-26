using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>Coordinate helpers for the fixed 10-light-year sector grid.</summary>
    public static class GalaxySectorUtility
    {
        public const double SectorSizeLightYears = 10.0;

        public static int3 GetCoordinates(double3 localPositionLightYears)
        {
            return new int3(
                (int)math.floor(localPositionLightYears.x / SectorSizeLightYears),
                (int)math.floor(localPositionLightYears.y / SectorSizeLightYears),
                (int)math.floor(localPositionLightYears.z / SectorSizeLightYears));
        }

        public static double3 GetOriginLightYears(int3 coordinates)
        {
            return (double3)coordinates * SectorSizeLightYears;
        }
    }
}
