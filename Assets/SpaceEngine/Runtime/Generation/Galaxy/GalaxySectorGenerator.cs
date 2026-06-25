using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Generates the visible, physically present stellar systems for one
    /// internal galaxy streaming sector.
    /// </summary>
    public static class GalaxySectorGenerator
    {
        public const double SECTOR_SIZE_LIGHT_YEARS = 10.0;

        private const int CandidateCount =
            GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS;

        public static GalaxySectorData Generate(
            in GalaxyData galaxy,
            int3 galaxySectorCoordinates)
        {
            var seed = SolarSystemIDUtility.GetGalaxySectorSeed(
                galaxy.Seed,
                galaxySectorCoordinates);

            var solarSystems =
                new FixedList4096Bytes<SolarSystemLocationData>();

            for (var candidateIndex = 0;
                 candidateIndex < CandidateCount;
                 candidateIndex++)
            {
                var candidate = GenerateCandidate(
                    galaxy,
                    galaxySectorCoordinates,
                    (byte)candidateIndex);

                if (!candidate.IsPresent)
                    continue;

                solarSystems.Add(candidate.Location);
            }

            return new GalaxySectorData(
                seed,
                galaxy.GalaxyID,
                galaxySectorCoordinates,
                solarSystems);
        }

        /// <summary>
        /// Reconstructs one internal streaming slot deterministically.
        /// Gameplay logical coordinates are resolved separately and never
        /// need to be issued by this density-driven sector catalogue.
        /// </summary>
        public static GalaxySectorCandidateData GenerateCandidate(
            in GalaxyData galaxy,
            int3 galaxySectorCoordinates,
            byte localSolarSystemIndex)
        {
            var sectorSeed = SolarSystemIDUtility.GetGalaxySectorSeed(
                galaxy.Seed,
                galaxySectorCoordinates);

            var candidateSeed = GalaxyIDUtility.Combine(
                sectorSeed,
                localSolarSystemIndex);

            var random = new QuantumRandom(candidateSeed);
            var sectorOrigin = GalaxySectorUtility.GetOriginLightYears(
                galaxySectorCoordinates);

            var localOffset = new double3(
                random.NextDouble(0.0, SECTOR_SIZE_LIGHT_YEARS),
                random.NextDouble(0.0, SECTOR_SIZE_LIGHT_YEARS),
                random.NextDouble(0.0, SECTOR_SIZE_LIGHT_YEARS));

            var galaxyLocalPosition = sectorOrigin + localOffset;
            var normalizedDensity = GalaxyDensityUtility.GetDensity(
                galaxy,
                galaxyLocalPosition);

            var sectorVolume = SECTOR_SIZE_LIGHT_YEARS *
                               SECTOR_SIZE_LIGHT_YEARS *
                               SECTOR_SIZE_LIGHT_YEARS;

            var expectedSystems = normalizedDensity *
                                  galaxy.BaseSystemDensityPerCubicLightYear *
                                  sectorVolume;

            var probability = math.clamp(
                expectedSystems / CandidateCount,
                0.0,
                1.0);

            var isPresent = random.NextDouble() <= probability;

            var solarSystemID = SolarSystemIDUtility.CreateSolarSystemID(
                galaxySectorCoordinates,
                localSolarSystemIndex);

            var estimatedMass = random.NextDouble(0.08, 4.0);

            return new GalaxySectorCandidateData(
                isPresent,
                new SolarSystemLocationData(
                    solarSystemID,
                    galaxyLocalPosition,
                    estimatedMass));
        }
    }
}
