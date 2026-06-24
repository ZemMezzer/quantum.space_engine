using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.SolarSystem.Jobs;
using Unity.Collections;
using Unity.Jobs;

namespace SpaceEngine.Runtime.Generation.SolarSystem
{
    /// <summary>
    /// Schedules generation of multiple stellar systems.
    /// </summary>
    public static class SolarSystemBatchGenerator
    {
        public static JobHandle Schedule(
            NativeArray<CoordinatesData> coordinates,
            NativeArray<SolarSystemData> results,
            int batchSize,
            JobHandle dependency = default)
        {
            var job = new SolarSystemGenerationJob
            {
                Coordinates = coordinates,
                Results = results
            };

            return job.Schedule(coordinates.Length, batchSize, dependency);
        }
    }
}