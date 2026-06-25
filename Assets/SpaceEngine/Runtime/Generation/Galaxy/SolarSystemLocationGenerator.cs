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

        /// <summary>
        /// Resolves a SolarSystemID only when it was actually emitted by a
        /// galaxy sector. Use this for spawn points, user input and save-file
        /// validation. Normal streaming may continue to call Generate after it
        /// has obtained IDs from a map, scanner or sector generator.
        /// </summary>
        public static bool TryGenerateExisting(
            in GalaxyData galaxy,
            ulong solarSystemID,
            out SolarSystemLocationData location)
        {
            SolarSystemIDUtility.DecodeSolarSystemID(
                solarSystemID,
                out var galaxySectorCoordinates,
                out var localSolarSystemIndex);

            var candidate = GalaxySectorGenerator.GenerateCandidate(
                galaxy,
                galaxySectorCoordinates,
                localSolarSystemIndex);

            location = candidate.Location;
            return candidate.IsPresent;
        }
    }
}
