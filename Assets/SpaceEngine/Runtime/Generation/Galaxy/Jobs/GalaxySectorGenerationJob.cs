using SpaceEngine.Runtime.Data.Galaxy;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy.Jobs
{
    /// <summary>
    /// Generates one galaxy streaming sector in a Burst-compatible job.
    /// Results must contain at least one element.
    /// </summary>
    public struct GalaxySectorGenerationJob : IJob
    {
        public GalaxyData Galaxy;
        public int3 SectorCoordinates;
        public NativeArray<GalaxySectorData> Results;

        public void Execute()
        {
            Results[0] = GalaxySectorGenerator.Generate(
                Galaxy,
                SectorCoordinates);
        }
    }
}
