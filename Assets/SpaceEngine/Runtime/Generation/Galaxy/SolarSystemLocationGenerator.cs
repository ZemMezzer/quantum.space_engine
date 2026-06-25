using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Coordinates;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Resolves solar-system locations.
    ///
    /// Public logical IDs always map to a valid generated position. The
    /// packed-slot overload is internal and exists only for nearby-star
    /// streaming, which already works in physical sector space.
    /// </summary>
    public static class SolarSystemLocationGenerator
    {
        /// <summary>
        /// Resolves a gameplay-facing logical SolarSystemID. Every long value
        /// has a deterministic position inside the addressed galaxy.
        /// </summary>
        public static SolarSystemLocationData Generate(
            in GalaxyData galaxy,
            long solarSystemID)
        {
            return LogicalCoordinatesResolver.ResolveSolarSystemLocation(
                galaxy,
                solarSystemID);
        }

        /// <summary>
        /// Resolves an internal ID emitted by GalaxySectorGenerator. Do not
        /// use this for CoordinatesData supplied by gameplay code.
        /// </summary>
        internal static SolarSystemLocationData GenerateFromStreamingID(
            in GalaxyData galaxy,
            long solarSystemID)
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
