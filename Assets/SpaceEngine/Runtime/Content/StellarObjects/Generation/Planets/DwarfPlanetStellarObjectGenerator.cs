using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets
{
    [CreateAssetMenu(
        fileName = "Dwarf Planet Generator",
        menuName = "Space Engine/Stellar Objects/Planets/Generators/Dwarf Planet")]
    public sealed class DwarfPlanetStellarObjectGenerator : PlanetaryStellarObjectGeneratorBase
    {
        protected override float GetRelativeWeight(double d)
        {
            return d < 0.65 ? 0.06f : d < 1.35 ? 0.08f : d < 4.0 ? 0.12f : 0.40f;
        }

        public override StellarObjectData Generate(
            in PlanetGenerationContext context,
            ref QuantumRandom random)
        {
            var hasAtmosphere = random.NextDouble() < 0.12;
            var hasRings = random.NextDouble() < 0.03;

            return CreatePlanet(
                random.NextDouble(0.0001, 0.20),
                random.NextDouble(900.0, 3_000.0),
                GetEquilibriumTemperature(context),
                hasAtmosphere ? random.NextDouble(1.0, 1_000.0) : 0.0,
                0.0,
                random.NextDouble(0.15, 1.0),
                random.NextDouble(0.0, 0.10),
                random.NextDouble() < 0.12 ? (byte)1 : (byte)0,
                hasAtmosphere,
                hasRings,
                context);
        }
    }
}
