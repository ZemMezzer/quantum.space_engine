using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Universe
{
    /// <summary>
    /// Generates one internal universe streaming sector.
    /// Galaxy IDs emitted by this generator are valid galaxy addresses.
    /// </summary>
    public static class UniverseSectorGenerator
    {
        public const double SECTOR_SIZE_LIGHT_YEARS = 10_000_000.0;

        public static UniverseSectorData Generate(
            ulong universeID,
            int3 universeSectorCoordinates)
        {
            var seed = GalaxyIDUtility.GetUniverseSectorSeed(
                universeID,
                universeSectorCoordinates);

            var galaxies =
                new FixedList4096Bytes<GalaxyLocationData>();

            var galaxyCount = GetGalaxyCount(seed);

            for (var i = 0; i < galaxyCount; i++)
            {
                var galaxyID = GalaxyIDUtility.CreateGalaxyID(
                    universeSectorCoordinates,
                    (ushort)i);

                galaxies.Add(GalaxyGenerator.GenerateLocation(
                    universeID,
                    galaxyID));
            }

            return new UniverseSectorData(
                seed,
                universeSectorCoordinates,
                galaxies);
        }

        /// <summary>
        /// Reconstructs the position assigned to a galaxy slot without
        /// generating the full sector list.
        /// </summary>
        public static double3 GenerateGalaxyUniversePosition(
            ulong universeID,
            int3 universeSectorCoordinates,
            ushort localGalaxyIndex)
        {
            var sectorSeed = GalaxyIDUtility.GetUniverseSectorSeed(
                universeID,
                universeSectorCoordinates);

            var slotSeed = GalaxyIDUtility.Combine(
                sectorSeed,
                localGalaxyIndex);

            var random = new QuantumRandom(slotSeed);

            var origin = UniverseSectorUtility.GetOriginLightYears(
                universeSectorCoordinates);

            return origin + new double3(
                random.NextDouble(0.0, SECTOR_SIZE_LIGHT_YEARS),
                random.NextDouble(0.0, SECTOR_SIZE_LIGHT_YEARS),
                random.NextDouble(0.0, SECTOR_SIZE_LIGHT_YEARS));
        }

        private static int GetGalaxyCount(ulong sectorSeed)
        {
            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    sectorSeed,
                    0x47414C4158595F43UL));

            var densityRoll = random.NextDouble();

            if (densityRoll < 0.42)
                return 0;

            if (densityRoll < 0.76)
                return random.NextInt(1, 4);

            if (densityRoll < 0.94)
                return random.NextInt(4, 11);

            return random.NextInt(11, 25);
        }
    }
}
