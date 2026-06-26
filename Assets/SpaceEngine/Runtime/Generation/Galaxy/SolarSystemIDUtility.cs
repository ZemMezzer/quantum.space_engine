using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Internal packing utility for stellar-field streaming slots.
    ///
    /// Gameplay CoordinatesData does not use this encoding. Logical solar
    /// system IDs are resolved by the selected GalaxyGenerator.
    /// </summary>
    public static class SolarSystemIDUtility
    {
        public const int COORDINATE_BITS = 19;
        public const int LOCAL_INDEX_BITS = 7;

        public const int MAXIMUM_LOCAL_SOLAR_SYSTEM_INDEX =
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

        public static long CreateSolarSystemID(
            int3 galaxySectorCoordinates,
            byte localSolarSystemIndex)
        {
            var x = (ulong)(
                ClampSectorCoordinate(galaxySectorCoordinates.x) +
                CoordinateOffset);

            var y = (ulong)(
                ClampSectorCoordinate(galaxySectorCoordinates.y) +
                CoordinateOffset);

            var z = (ulong)(
                ClampSectorCoordinate(galaxySectorCoordinates.z) +
                CoordinateOffset);

            var packed = ((z & CoordinateMask) <<
                          (COORDINATE_BITS * 2 + LOCAL_INDEX_BITS)) |
                         ((y & CoordinateMask) <<
                          (COORDINATE_BITS + LOCAL_INDEX_BITS)) |
                         ((x & CoordinateMask) << LOCAL_INDEX_BITS) |
                         ((ulong)localSolarSystemIndex & LocalIndexMask);

            return unchecked((long)packed);
        }

        public static void DecodeSolarSystemID(
            long solarSystemID,
            out int3 galaxySectorCoordinates,
            out byte localSolarSystemIndex)
        {
            var packed = unchecked((ulong)solarSystemID);

            localSolarSystemIndex = (byte)(packed & LocalIndexMask);

            var x = (int)(
                (packed >> LOCAL_INDEX_BITS) & CoordinateMask) -
                CoordinateOffset;

            var y = (int)(
                (packed >>
                 (COORDINATE_BITS + LOCAL_INDEX_BITS)) &
                CoordinateMask) - CoordinateOffset;

            var z = (int)(
                (packed >>
                 (COORDINATE_BITS * 2 + LOCAL_INDEX_BITS)) &
                CoordinateMask) - CoordinateOffset;

            galaxySectorCoordinates = new int3(x, y, z);
        }

        public static bool IsSectorCoordinateInRange(int3 coordinates)
        {
            return IsCoordinateInRange(coordinates.x) &&
                   IsCoordinateInRange(coordinates.y) &&
                   IsCoordinateInRange(coordinates.z);
        }

        public static ulong GetGalaxySectorSeed(
            ulong galaxySeed,
            int3 galaxySectorCoordinates)
        {
            var seed = GalaxyIDUtility.Combine(
                galaxySeed,
                0x534543544F525F47UL);

            seed = GalaxyIDUtility.Combine(seed, ToUnsigned(
                galaxySectorCoordinates.x));
            seed = GalaxyIDUtility.Combine(seed, ToUnsigned(
                galaxySectorCoordinates.y));
            seed = GalaxyIDUtility.Combine(seed, ToUnsigned(
                galaxySectorCoordinates.z));

            return seed;
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
