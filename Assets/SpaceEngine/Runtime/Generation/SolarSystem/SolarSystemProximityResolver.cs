using System;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.SolarSystem
{
    /// <summary>
    /// Data-only nearest-system lookup. It uses GalaxyGenerator sector data and
    /// therefore belongs to generation, not rendering.
    /// </summary>
    public static class SolarSystemProximityResolver
    {
        public static bool TryFindNearest(
            SpaceEngineConfiguration configuration,
            in GalaxyData galaxy,
            double3 galaxyLocalPositionLightYears,
            int sectorSearchRadius,
            out SolarSystemLocationData nearestSolarSystem,
            out double distanceMeters)
        {
            var centerSector = GalaxySectorUtility.GetCoordinates(
                galaxyLocalPositionLightYears);
            var radius = Math.Max(0, sectorSearchRadius);
            var nearestDistanceSquared = double.PositiveInfinity;
            nearestSolarSystem = default;

            var generator = UniverseGeneration.ResolveGalaxyGenerator(
                configuration.GalaxyGenerators,
                galaxy.UniverseID,
                galaxy.GalaxyID,
                galaxy.UniversePositionLightYears);

            if (generator == null)
            {
                distanceMeters = 0.0;
                return false;
            }

            for (var z = centerSector.z - radius;
                 z <= centerSector.z + radius;
                 z++)
            {
                for (var y = centerSector.y - radius;
                     y <= centerSector.y + radius;
                     y++)
                {
                    for (var x = centerSector.x - radius;
                         x <= centerSector.x + radius;
                         x++)
                    {
                        var sector = generator.GenerateSector(
                            galaxy,
                            new int3(x, y, z));

                        for (var index = 0;
                             index < sector.SolarSystems.Length;
                             index++)
                        {
                            var solarSystem = sector.SolarSystems[index];
                            var relativeMeters =
                                (solarSystem
                                     .GalaxyLocalPositionLightYears -
                                 galaxyLocalPositionLightYears) *
                                SolarConstants.LIGHT_YEAR_METERS;

                            var distanceSquared = math.dot(
                                relativeMeters,
                                relativeMeters);

                            if (distanceSquared >= nearestDistanceSquared)
                                continue;

                            nearestDistanceSquared = distanceSquared;
                            nearestSolarSystem = solarSystem;
                        }
                    }
                }
            }

            if (double.IsPositiveInfinity(nearestDistanceSquared))
            {
                distanceMeters = 0.0;
                return false;
            }

            distanceMeters = Math.Sqrt(nearestDistanceSquared);
            return true;
        }
    }
}
