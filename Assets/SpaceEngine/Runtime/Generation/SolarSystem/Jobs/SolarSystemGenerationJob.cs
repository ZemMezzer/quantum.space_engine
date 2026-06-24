using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SpaceEngine.Runtime.Generation.SolarSystem.Jobs
{
    /// <summary>
    /// Generates a batch of stellar systems in parallel.
    /// Each output system is fully determined by its input coordinates.
    /// </summary>
    [BurstCompile]
    public struct SolarSystemGenerationJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<CoordinatesData> Coordinates;

        [WriteOnly]
        public NativeArray<SolarSystemData> Results;

        public void Execute(int index)
        {
            Results[index] = SolarSystemGenerator.Generate(Coordinates[index]);
        }
    }
}