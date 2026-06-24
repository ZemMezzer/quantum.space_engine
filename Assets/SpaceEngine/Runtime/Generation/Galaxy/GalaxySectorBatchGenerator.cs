using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Schedules parallel generation for the galaxy sectors currently needed
    /// by the runtime streamer.
    /// </summary>
    public static class GalaxySectorBatchGenerator
    {
        public static JobHandle Schedule(
            in GalaxyData galaxy,
            NativeArray<int3> sectorCoordinates,
            NativeArray<GalaxySectorData> results,
            int batchSize,
            JobHandle dependency = default)
        {
            var job = new GalaxySectorBatchGenerationJob
            {
                Galaxy = galaxy,
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
