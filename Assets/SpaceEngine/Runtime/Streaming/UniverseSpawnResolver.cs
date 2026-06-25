using System;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Finds valid galaxy IDs issued by UniverseSectorGenerator. It exists for
    /// demo/bootstrap code only; gameplay should normally receive IDs from
    /// maps, scanners, portals or saved coordinates.
    /// </summary>
    public static class UniverseSpawnResolver
    {
        public static bool TryResolveExisting(
            ulong universeID,
            ulong galaxyID,
            out GalaxyLocationData location)
        {
            GalaxyIDUtility.DecodeGalaxyID(
                galaxyID,
                out var sectorCoordinates,
                out _);

            var sector = UniverseSectorGenerator.Generate(
                universeID,
                sectorCoordinates);

            for (var i = 0; i < sector.Galaxies.Length; i++)
            {
                if (sector.Galaxies[i].GalaxyID != galaxyID)
                    continue;

                location = sector.Galaxies[i];
                return true;
            }

            location = default;
            return false;
        }

        /// <summary>
        /// Searches from the universe origin outwards and returns the closest
        /// actually generated galaxy. This avoids treating 0 as a valid opaque
        /// GalaxyID, because zero decodes to the minimum representable sector.
        /// </summary>
        public static bool TryFindNearestGeneratedGalaxy(
            ulong universeID,
            int maximumSectorRadius,
            out GalaxyLocationData location)
        {
            maximumSectorRadius = Math.Max(0, maximumSectorRadius);

            var found = false;
            var nearestDistanceSquared = double.PositiveInfinity;
            location = default;

            for (var radius = 0; radius <= maximumSectorRadius; radius++)
            {
                for (var z = -radius; z <= radius; z++)
                {
                    for (var y = -radius; y <= radius; y++)
                    {
                        for (var x = -radius; x <= radius; x++)
                        {
                            if (math.max(math.abs(x), math.max(math.abs(y), math.abs(z))) != radius)
                                continue;

                            var sectorCoordinates = new int3(x, y, z);
                            var sector = UniverseSectorGenerator.Generate(
                                universeID,
                                sectorCoordinates);

                            for (var index = 0;
                                 index < sector.Galaxies.Length;
                                 index++)
                            {
                                var candidate = sector.Galaxies[index];
                                var distanceSquared = math.dot(
                                    candidate.UniversePositionLightYears,
                                    candidate.UniversePositionLightYears);

                                if (distanceSquared >= nearestDistanceSquared)
                                    continue;

                                nearestDistanceSquared = distanceSquared;
                                location = candidate;
                                found = true;
                            }
                        }
                    }
                }

                if (found)
                    return true;
            }

            return false;
        }
    }
}
