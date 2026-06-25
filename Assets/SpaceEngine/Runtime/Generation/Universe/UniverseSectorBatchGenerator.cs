using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Universe.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Universe
{
    /// <summary>
    /// Schedules parallel generation for universe sectors needed by the
    /// universe-scale streamer.
    /// </summary>
    public static class UniverseSectorBatchGenerator
    {
        public static JobHandle Schedule(
            long universeID,
            NativeArray<int3> sectorCoordinates,
            NativeArray<UniverseSectorData> results,
            int batchSize,
            JobHandle dependency = default)
        {
            var job = new UniverseSectorBatchGenerationJob
            {
                UniverseID = universeID,
                SectorCoordinates = sectorCoordinates,
                Results = results
            };

            return job.Schedule(
                sectorCoordinates.Length,
                batchSize,
                dependency);
        }
    }
}
