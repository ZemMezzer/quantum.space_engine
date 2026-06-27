using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Irregular Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Irregular")]
    public sealed class IrregularGalaxyRenderer : ProceduralGalaxyRenderer
    {
        protected override GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy)
        {
            return new GalaxyVisualProfile(
                0x4952524547554C56UL,
                4.0f,
                new Color(0.88f, 0.70f, 0.34f, 1.0f),
                new Color(0.20f, 0.76f, 0.72f, 1.0f),
                new Color(0.80f, 0.25f, 0.66f, 1.0f),
                new Color(0.30f, 0.55f, 0.54f, 1.0f),
                new Color(0.08f, 0.26f, 0.26f, 1.0f),
                new Color(0.70f, 1.00f, 0.92f, 1.0f),
                0.76f,
                1.05f,
                1.16f,
                1.20f,
                1.00f,
                1.22f,
                false,
                false,
                false,
                false,
                true,
                0.05f,
                0.18f,
                0.18f,
                0.16f,
                0.12f);
        }

        protected override double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleIrregular(galaxy, ref random);
        }
    }
}
