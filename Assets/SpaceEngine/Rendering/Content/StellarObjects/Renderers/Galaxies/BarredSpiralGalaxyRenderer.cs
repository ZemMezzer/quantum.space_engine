using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Barred Spiral Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Barred Spiral")]
    public sealed class BarredSpiralGalaxyRenderer : ProceduralGalaxyRenderer
    {
        protected override GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy)
        {
            return new GalaxyVisualProfile(
                0x4241525245445F56UL,
                1.0f,
                new Color(1.00f, 0.66f, 0.32f, 1.0f),
                new Color(0.30f, 0.68f, 0.92f, 1.0f),
                new Color(0.80f, 0.30f, 0.72f, 1.0f),
                new Color(0.30f, 0.50f, 0.78f, 1.0f),
                new Color(0.12f, 0.22f, 0.36f, 1.0f),
                new Color(0.82f, 0.92f, 1.00f, 1.0f),
                0.86f,
                1.18f,
                1.22f,
                0.96f,
                1.00f,
                1.00f,
                true,
                true,
                false,
                false,
                false);
        }

        protected override double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleBarredSpiral(galaxy, ref random);
        }
    }
}
