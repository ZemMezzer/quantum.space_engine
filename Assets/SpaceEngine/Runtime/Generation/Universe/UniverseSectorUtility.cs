using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Universe
{
    /// <summary>Coordinate helpers for fixed universe map sectors.</summary>
    public static class UniverseSectorUtility
    {
        public static int3 GetCoordinates(double3 universePositionLightYears)
        {
            return new int3(
                (int)math.floor(universePositionLightYears.x /
                    UniverseGeneration.SectorSizeLightYears),
                (int)math.floor(universePositionLightYears.y /
                    UniverseGeneration.SectorSizeLightYears),
                (int)math.floor(universePositionLightYears.z /
                    UniverseGeneration.SectorSizeLightYears));
        }

        public static double3 GetOriginLightYears(int3 coordinates)
        {
            return (double3)coordinates *
                   UniverseGeneration.SectorSizeLightYears;
        }
    }
}
