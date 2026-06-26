using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    [CreateAssetMenu(
        fileName = "Terrestrial Planet Generator",
        menuName = "Space Engine/Stellar Objects/Planets/Generators/Terrestrial")]
    public sealed class TerrestrialPlanetStellarObjectGenerator : PlanetaryStellarObjectGeneratorBase
    {
        protected override float GetRelativeWeight(double d)
        {
            return d < 0.65 ? 0.82f : d < 1.35 ? 0.55f : d < 4.0 ? 0.12f : 0.0f;
        }

        public override StellarObjectData Generate(
            in PlanetGenerationContext context,
            ref QuantumRandom random)
        {
            var temperature = GetEquilibriumTemperature(context);
            var hasAtmosphere = random.NextDouble() < 0.68;
            var hasRings = random.NextDouble() < 0.01;

            return CreatePlanet(
                random.NextDouble(0.08, 5.0),
                random.NextDouble(3_800.0, 7_200.0),
                temperature,
                hasAtmosphere ? random.NextDouble(2_000.0, 2_000_000.0) : 0.0,
                temperature > 265.0 && temperature < 370.0
                    ? random.NextDouble(0.0, 0.85)
                    : random.NextDouble(0.0, 0.15),
                temperature < 270.0
                    ? random.NextDouble(0.15, 1.0)
                    : random.NextDouble(0.0, 0.20),
                random.NextDouble(0.0, 1.0),
                (byte)random.NextInt(0, 4),
                hasAtmosphere,
                hasRings,
                context);
        }
    }
}
