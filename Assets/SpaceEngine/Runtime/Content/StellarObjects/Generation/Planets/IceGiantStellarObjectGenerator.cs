using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    [CreateAssetMenu(
        fileName = "Ice Giant Generator",
        menuName = "Space Engine/Stellar Objects/Planets/Generators/Ice Giant")]
    public sealed class IceGiantStellarObjectGenerator : PlanetaryStellarObjectGeneratorBase
    {
        protected override float GetRelativeWeight(double d)
        {
            return d < 1.35 ? 0.0f : d < 4.0 ? 0.28f : 0.38f;
        }

        public override StellarObjectData Generate(
            in PlanetGenerationContext context,
            ref QuantumRandom random)
        {
            const bool hasAtmosphere = true;
            var hasRings = random.NextDouble() < 0.22;

            return CreatePlanet(
                random.NextDouble(6.0, 35.0),
                random.NextDouble(900.0, 2_100.0),
                GetEquilibriumTemperature(context),
                random.NextDouble(2_000_000.0, 500_000_000.0),
                0.0,
                random.NextDouble(0.10, 0.75),
                random.NextDouble(0.0, 0.15),
                (byte)random.NextInt(1, 25),
                hasAtmosphere,
                hasRings,
                context);
        }
    }
}
