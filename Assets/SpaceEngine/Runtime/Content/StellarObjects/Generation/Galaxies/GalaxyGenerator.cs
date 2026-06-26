using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies
{
    public abstract class GalaxyGenerator : ScriptableObject
    {
        public abstract float GetWeight(in GalaxyGenerationContext context);

        public abstract GalaxyData Generate(
            in GalaxyGenerationContext context,
            ref QuantumRandom random);

        public abstract GalaxySectorData GenerateSector(
            in GalaxyData galaxy,
            int3 sectorCoordinates);

        public abstract SolarSystemLocationData GenerateSolarSystemLocation(
            in GalaxyData galaxy,
            long solarSystemID);

        public abstract bool IsInside(
            in GalaxyData galaxy,
            double3 localPositionLightYears);
    }
}
