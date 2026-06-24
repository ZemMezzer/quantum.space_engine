using SpaceEngine.Runtime.Data.Galaxy;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy.Jobs
{
    /// <summary>
    /// Generates many galaxy streaming sectors in parallel.
    /// </summary>
    public struct GalaxySectorBatchGenerationJob : IJobParallelFor
    {
        public GalaxyData Galaxy;

        [ReadOnly]
        public NativeArray<int3> SectorCoordinates;

        [WriteOnly]
        public NativeArray<GalaxySectorData> Results;

        public void Execute(int index)
        {
            Results[index] = GalaxySectorGenerator.Generate(
                Galaxy,
                SectorCoordinates[index]);
        }
    }
}
