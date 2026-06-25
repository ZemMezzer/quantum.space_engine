using SpaceEngine.Runtime.Data.Galaxy;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Universe.Jobs
{
    /// <summary>
    /// Generates many universe streaming sectors in parallel.
    /// </summary>
    public struct UniverseSectorBatchGenerationJob : IJobParallelFor
    {
        public long UniverseID;

        [ReadOnly]
        public NativeArray<int3> SectorCoordinates;

        [WriteOnly]
        public NativeArray<UniverseSectorData> Results;

        public void Execute(int index)
        {
            Results[index] = UniverseSectorGenerator.Generate(
                UniverseID,
                SectorCoordinates[index]);
        }
    }
}
