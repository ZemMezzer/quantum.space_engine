using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    public abstract class StellarObjectGenerator : ScriptableObject
    {
        public abstract float GetWeight(
            in StellarObjectGenerationContext context);

        public abstract StellarObjectData Generate(
            in StellarObjectGenerationContext context,
            ref QuantumRandom random);
    }
}
