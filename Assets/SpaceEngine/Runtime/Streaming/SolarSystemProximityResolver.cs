using System;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Resolves the closest real stellar system near a galaxy-space position.
    /// It only scans a small number of internal streaming sectors and is meant
    /// to be called periodically by the seamless LOD controller, not per
    /// frame.
    /// </summary>
    public static class SolarSystemProximityResolver
    {
        public static bool TryFindNearest(
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
                        var sectorCoordinates = new int3(x, y, z);

                        if (!SolarSystemIDUtility.IsSectorCoordinateInRange(
                                sectorCoordinates))
                        {
                            continue;
                        }

                        var sector = GalaxySectorGenerator.Generate(
                            galaxy,
                            sectorCoordinates);

                        for (var i = 0;
                             i < sector.SolarSystems.Length;
                             i++)
                        {
                            var solarSystem = sector.SolarSystems[i];
                            var relativeMeters =
                                (solarSystem
                                     .GalaxyLocalPositionLightYears -
                                 galaxyLocalPositionLightYears) *
                                SeamlessSpaceAnchor.MetersPerLightYear;

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
