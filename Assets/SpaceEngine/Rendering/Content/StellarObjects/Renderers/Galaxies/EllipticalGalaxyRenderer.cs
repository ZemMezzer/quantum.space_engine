using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Elliptical Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Elliptical")]
    public sealed class EllipticalGalaxyRenderer : ProceduralGalaxyRenderer
    {
        protected override GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy)
        {
            return new GalaxyVisualProfile(
                0x454C4C4950534556UL,
                2.0f,
                new Color(1.00f, 0.85f, 0.58f, 1.0f),
                new Color(0.94f, 0.74f, 0.45f, 1.0f),
                new Color(0.80f, 0.64f, 0.42f, 1.0f),
                new Color(0.74f, 0.57f, 0.35f, 1.0f),
                new Color(0.28f, 0.20f, 0.12f, 1.0f),
                new Color(1.00f, 0.90f, 0.72f, 1.0f),
                0.58f,
                0.86f,
                0.92f,
                0.48f,
                1.00f,
                1.00f,
                false,
                false,
                true,
                false,
                false,
                0.014f,
                0.05f,
                0.08f,
                0.05f,
                0.02f);
        }

        protected override double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleEllipsoid(galaxy, ref random);
        }
    }
}
