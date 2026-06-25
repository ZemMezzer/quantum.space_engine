using System;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// One-time bootstrap resolver for a real stellar system near a galaxy's
    /// centre. All returned IDs originate from GalaxySectorGenerator.
    /// </summary>
    public static class SolarSystemSpawnResolver
    {
        public static bool TryFindNearestGeneratedSolarSystem(
            in GalaxyData galaxy,
            int horizontalSectorRadius,
            int verticalSectorRadius,
            out SolarSystemLocationData location)
        {
            horizontalSectorRadius = Math.Max(0, horizontalSectorRadius);
            verticalSectorRadius = Math.Max(0, verticalSectorRadius);

            var found = false;
            var nearestDistanceSquared = double.PositiveInfinity;
            location = default;

            var horizontalRadiusSquared =
                horizontalSectorRadius * horizontalSectorRadius;

            for (var z = -horizontalSectorRadius;
                 z <= horizontalSectorRadius;
                 z++)
            {
                for (var y = -verticalSectorRadius;
                     y <= verticalSectorRadius;
                     y++)
                {
                    for (var x = -horizontalSectorRadius;
                         x <= horizontalSectorRadius;
                         x++)
                    {
                        if (x * x + z * z > horizontalRadiusSquared)
                            continue;

                        var sectorCoordinates = new int3(x, y, z);
                        var sector = GalaxySectorGenerator.Generate(
                            galaxy,
                            sectorCoordinates);

                        for (var index = 0;
                             index < sector.SolarSystems.Length;
                             index++)
                        {
                            var candidate = sector.SolarSystems[index];
                            var distanceSquared = math.dot(
                                candidate.GalaxyLocalPositionLightYears,
                                candidate.GalaxyLocalPositionLightYears);

                            if (distanceSquared >= nearestDistanceSquared)
                                continue;

                            nearestDistanceSquared = distanceSquared;
                            location = candidate;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }
    }
}
