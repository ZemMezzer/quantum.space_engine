using Unity.Collections;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Internal streaming cell inside one galaxy.
    /// </summary>
    public struct GalaxySectorData
    {
        public const byte MAXIMUM_SOLAR_SYSTEMS = 64;

        public readonly ulong Seed;
        public readonly long GalaxyID;
        public readonly int3 Coordinates;
        public readonly FixedList4096Bytes<SolarSystemLocationData> SolarSystems;

        internal GalaxySectorData(
            ulong seed,
            long galaxyID,
            int3 coordinates,
            FixedList4096Bytes<SolarSystemLocationData> solarSystems)
        {
            Seed = seed;
            GalaxyID = galaxyID;
            Coordinates = coordinates;
            SolarSystems = solarSystems;
        }
    }
}
