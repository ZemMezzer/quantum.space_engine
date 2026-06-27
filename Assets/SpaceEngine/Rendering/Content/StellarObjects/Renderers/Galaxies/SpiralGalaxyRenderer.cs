using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Spiral Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Spiral")]
    public sealed class SpiralGalaxyRenderer : ProceduralGalaxyRenderer
    {
        protected override GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy)
        {
            return new GalaxyVisualProfile(
                0x53504952414C5F56UL,
                0.0f,
                new Color(1.00f, 0.72f, 0.40f, 1.0f),
                new Color(0.28f, 0.53f, 0.95f, 1.0f),
                new Color(0.62f, 0.28f, 0.94f, 1.0f),
                new Color(0.20f, 0.38f, 0.75f, 1.0f),
                new Color(0.10f, 0.16f, 0.38f, 1.0f),
                new Color(0.72f, 0.84f, 1.00f, 1.0f),
                0.82f,
                1.10f,
                1.20f,
                0.90f,
                1.00f,
                1.00f,
                true,
                false,
                false,
                false,
                false);
        }

        protected override double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleSpiral(galaxy, ref random);
        }
    }
}
