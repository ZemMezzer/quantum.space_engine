using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    public abstract class PlanetGenerator : ScriptableObject
    {
        public abstract float GetWeight(in PlanetGenerationContext context);

        public abstract StellarObjectData Generate(
            in PlanetGenerationContext context,
            ref QuantumRandom random);
    }
}
