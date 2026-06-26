using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Coordinates
{
    /// <summary>
    /// Resolves a stable universe-space position for a logical galaxy ID.
    /// Galaxy morphology and solar-system placement deliberately remain in
    /// their selected GalaxyGenerator assets.
    /// </summary>
    public static class LogicalCoordinatesResolver
    {
        private const int LogicalUniverseRadiusInSectors = 8_192;
        private const ulong GalaxyPositionSalt = 0x4C4F4749435F4741UL;

        public static double3 ResolveGalaxyUniversePosition(
            long universeID,
            long galaxyID)
        {
            if (galaxyID == 0)
                return double3.zero;

            var seed = GalaxyIDUtility.Combine(
                GalaxyIDUtility.GetGalaxySeed(universeID, galaxyID),
                GalaxyPositionSalt);
            var random = new QuantumRandom(seed);
            var sector = new int3(
                random.NextInt(-LogicalUniverseRadiusInSectors,
                    LogicalUniverseRadiusInSectors + 1),
                random.NextInt(-LogicalUniverseRadiusInSectors,
                    LogicalUniverseRadiusInSectors + 1),
                random.NextInt(-LogicalUniverseRadiusInSectors,
                    LogicalUniverseRadiusInSectors + 1));
            var origin = UniverseSectorUtility.GetOriginLightYears(sector);

            return origin + new double3(
                random.NextDouble(0.0, SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.SectorSizeLightYears),
                random.NextDouble(0.0, SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.SectorSizeLightYears),
                random.NextDouble(0.0, SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.SectorSizeLightYears));
        }
    }
}
