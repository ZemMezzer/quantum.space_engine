using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Internal packing utility for universe-map streaming sectors.
    ///
    /// Gameplay CoordinatesData never uses this format. A logical GalaxyID is
    /// resolved by GalaxyGenerator without exposing or decoding these bits.
    /// </summary>
    public static class GalaxyIDUtility
    {
        public const int COORDINATE_BITS = 18;
        public const int LOCAL_INDEX_BITS = 10;

        public const int MAXIMUM_LOCAL_GALAXY_INDEX =
            (1 << LOCAL_INDEX_BITS) - 1;

        public const int MINIMUM_SECTOR_COORDINATE =
            -(1 << (COORDINATE_BITS - 1));

        public const int MAXIMUM_SECTOR_COORDINATE =
            (1 << (COORDINATE_BITS - 1)) - 1;

        private const ulong CoordinateMask =
            (1UL << COORDINATE_BITS) - 1UL;

        private const ulong LocalIndexMask =
            (1UL << LOCAL_INDEX_BITS) - 1UL;

        private const int CoordinateOffset =
            1 << (COORDINATE_BITS - 1);

        /// <summary>
        /// Creates an internal map-sector identifier. It is only used by the
        /// universe streamer/editor; it is not required by CoordinatesData.
        /// </summary>
        public static long CreateGalaxyID(
            int3 universeSectorCoordinates,
            ushort localGalaxyIndex)
        {
            var x = (ulong)(
                ClampSectorCoordinate(universeSectorCoordinates.x) +
                CoordinateOffset);

            var y = (ulong)(
                ClampSectorCoordinate(universeSectorCoordinates.y) +
                CoordinateOffset);

            var z = (ulong)(
                ClampSectorCoordinate(universeSectorCoordinates.z) +
                CoordinateOffset);

            var packed = ((z & CoordinateMask) <<
                          (COORDINATE_BITS * 2 + LOCAL_INDEX_BITS)) |
                         ((y & CoordinateMask) <<
                          (COORDINATE_BITS + LOCAL_INDEX_BITS)) |
                         ((x & CoordinateMask) << LOCAL_INDEX_BITS) |
                         ((ulong)localGalaxyIndex & LocalIndexMask);

            return unchecked((long)packed);
        }

        public static void DecodeGalaxyID(
            long galaxyID,
            out int3 universeSectorCoordinates,
            out ushort localGalaxyIndex)
        {
            var packed = unchecked((ulong)galaxyID);

            localGalaxyIndex = (ushort)(packed & LocalIndexMask);

            var x = (int)(
                (packed >> LOCAL_INDEX_BITS) & CoordinateMask) -
                CoordinateOffset;

            var y = (int)(
                (packed >> (COORDINATE_BITS + LOCAL_INDEX_BITS)) &
                CoordinateMask) - CoordinateOffset;

            var z = (int)(
                (packed >>
                 (COORDINATE_BITS * 2 + LOCAL_INDEX_BITS)) &
                CoordinateMask) - CoordinateOffset;

            universeSectorCoordinates = new int3(x, y, z);
        }

        public static bool IsSectorCoordinateInRange(int3 coordinates)
        {
            return IsCoordinateInRange(coordinates.x) &&
                   IsCoordinateInRange(coordinates.y) &&
                   IsCoordinateInRange(coordinates.z);
        }

        public static ulong GetGalaxySeed(
            long universeID,
            long galaxyID)
        {
            var universeSeed = Mix(
                unchecked((ulong)universeID) ^
                0xC77A9E4D1B0F5A93UL);

            return Combine(universeSeed, unchecked((ulong)galaxyID));
        }

        public static ulong GetUniverseSectorSeed(
            long universeID,
            int3 universeSectorCoordinates)
        {
            var seed = Mix(
                unchecked((ulong)universeID) ^
                0xC77A9E4D1B0F5A93UL);

            seed = Combine(seed, ToUnsigned(
                universeSectorCoordinates.x));
            seed = Combine(seed, ToUnsigned(
                universeSectorCoordinates.y));
            seed = Combine(seed, ToUnsigned(
                universeSectorCoordinates.z));

            return seed;
        }

        public static ulong Combine(ulong seed, ulong value)
        {
            return Mix(seed ^ (value + 0x9E3779B97F4A7C15UL));
        }

        public static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private static bool IsCoordinateInRange(int value)
        {
            return value >= MINIMUM_SECTOR_COORDINATE &&
                   value <= MAXIMUM_SECTOR_COORDINATE;
        }

        private static int ClampSectorCoordinate(int value)
        {
            if (value < MINIMUM_SECTOR_COORDINATE)
                return MINIMUM_SECTOR_COORDINATE;

            if (value > MAXIMUM_SECTOR_COORDINATE)
                return MAXIMUM_SECTOR_COORDINATE;

            return value;
        }

        private static ulong ToUnsigned(int value)
        {
            return unchecked((ulong)(long)value);
        }
    }
}
