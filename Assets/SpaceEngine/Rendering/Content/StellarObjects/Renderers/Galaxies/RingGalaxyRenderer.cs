using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Ring Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Ring")]
    public sealed class RingGalaxyRenderer : ProceduralGalaxyRenderer
    {
        protected override GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy)
        {
            return new GalaxyVisualProfile(
                0x52494E475F47414CUL,
                5.0f,
                new Color(1.00f, 0.76f, 0.36f, 1.0f),
                new Color(0.38f, 0.74f, 1.00f, 1.0f),
                new Color(0.86f, 0.30f, 0.92f, 1.0f),
                new Color(0.38f, 0.46f, 0.94f, 1.0f),
                new Color(0.12f, 0.12f, 0.36f, 1.0f),
                new Color(0.78f, 0.88f, 1.00f, 1.0f),
                0.78f,
                1.14f,
                1.22f,
                0.96f,
                1.00f,
                1.00f,
                false,
                false,
                false,
                true,
                false,
                0.032f,
                0.12f,
                0.14f,
                0.10f,
                0.08f);
        }

        protected override double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleRing(galaxy, ref random);
        }
    }
}
