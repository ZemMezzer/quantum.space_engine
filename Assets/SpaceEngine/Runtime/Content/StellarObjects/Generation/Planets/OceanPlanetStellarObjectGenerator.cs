using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    [CreateAssetMenu(
        fileName = "Ocean Planet Generator",
        menuName = "Space Engine/Stellar Objects/Planets/Generators/Ocean")]
    public sealed class OceanPlanetStellarObjectGenerator : PlanetaryStellarObjectGeneratorBase
    {
        protected override float GetRelativeWeight(double d)
        {
            return d < 0.65 ? 0.12f : d < 1.35 ? 0.23f : 0.0f;
        }

        public override StellarObjectData Generate(
            in PlanetGenerationContext context,
            ref QuantumRandom random)
        {
            var temperature = GetEquilibriumTemperature(context);
            const bool hasAtmosphere = true;
            var hasRings = random.NextDouble() < 0.01;

            return CreatePlanet(
                random.NextDouble(0.3, 8.0),
                random.NextDouble(2_800.0, 5_500.0),
                temperature,
                random.NextDouble(40_000.0, 5_000_000.0),
                random.NextDouble(0.70, 1.0),
                temperature < 280.0 ? random.NextDouble(0.10, 0.85) : 0.0,
                random.NextDouble(0.0, 0.55),
                (byte)random.NextInt(0, 4),
                hasAtmosphere,
                hasRings,
                context);
        }
    }
}
