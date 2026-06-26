using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    [CreateAssetMenu(
        fileName = "Gas Giant Generator",
        menuName = "Space Engine/Stellar Objects/Planets/Generators/Gas Giant")]
    public sealed class GasGiantStellarObjectGenerator : PlanetaryStellarObjectGeneratorBase
    {
        protected override float GetRelativeWeight(double d)
        {
            return d < 0.65 ? 0.0f : d < 1.35 ? 0.14f : d < 4.0 ? 0.48f : 0.22f;
        }

        public override StellarObjectData Generate(
            in PlanetGenerationContext context,
            ref QuantumRandom random)
        {
            const bool hasAtmosphere = true;
            var hasRings = random.NextDouble() < 0.38;

            return CreatePlanet(
                random.NextDouble(20.0, 1_000.0),
                random.NextDouble(250.0, 1_500.0),
                GetEquilibriumTemperature(context),
                random.NextDouble(10_000_000.0, 1_000_000_000.0),
                0.0,
                0.0,
                0.0,
                (byte)random.NextInt(4, 50),
                hasAtmosphere,
                hasRings,
                context);
        }
    }
}
