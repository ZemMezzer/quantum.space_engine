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
        private void Reset()
        {
            ApplyDefaultPaletteForNewAsset();
        }

        protected override float ShaderMorphologyValue => 5.0f;

        protected override GalaxyRendererDefaults Defaults =>
            new GalaxyRendererDefaults(
                new Color(1.00f, 0.76f, 0.36f, 1.0f),
                new Color(0.38f, 0.74f, 1.00f, 1.0f),
                new Color(0.86f, 0.30f, 0.92f, 1.0f),
                new Color(0.38f, 0.46f, 0.94f, 1.0f),
                new Color(0.12f, 0.12f, 0.36f, 1.0f),
                new Color(0.78f, 0.88f, 1.00f, 1.0f));

        protected override double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleRing(galaxy, ref random);
        }
    }
}
