using SpaceEngine.Runtime.Data.Galaxy;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Resolves a known existing SolarSystemID to its galaxy-local position.
    /// The caller supplies IDs obtained from GalaxySectorGenerator, map data,
    /// scanners, portals or saved coordinates.
    /// </summary>
    public static class SolarSystemLocationGenerator
    {
        public static SolarSystemLocationData Generate(
            in GalaxyData galaxy,
            ulong solarSystemID)
        {
            SolarSystemIDUtility.DecodeSolarSystemID(
                solarSystemID,
                out var galaxySectorCoordinates,
                out var localSolarSystemIndex);

            var candidate = GalaxySectorGenerator.GenerateCandidate(
                galaxy,
                galaxySectorCoordinates,
                localSolarSystemIndex);

            return candidate.Location;
        }
    }
}
