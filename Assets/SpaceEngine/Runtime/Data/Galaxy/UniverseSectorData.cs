using Unity.Collections;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Internal streaming cell at universe scale. It is not part of player-facing coordinates.
    /// </summary>
    public struct UniverseSectorData
    {
        public const byte MAXIMUM_GALAXIES = 64;

        public readonly ulong Seed;
        public readonly int3 Coordinates;
        public readonly FixedList4096Bytes<GalaxyLocationData> Galaxies;

        internal UniverseSectorData(
            ulong seed,
            int3 coordinates,
            FixedList4096Bytes<GalaxyLocationData> galaxies)
        {
            Seed = seed;
            Coordinates = coordinates;
            Galaxies = galaxies;
        }
    }
}
