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
        private void Reset()
        {
            ApplyDefaultPaletteForNewAsset();
        }

        protected override float ShaderMorphologyValue => 0.0f;

        protected override GalaxyRendererDefaults Defaults =>
            new GalaxyRendererDefaults(
                new Color(1.00f, 0.72f, 0.40f, 1.0f),
                new Color(0.28f, 0.53f, 0.95f, 1.0f),
                new Color(0.62f, 0.28f, 0.94f, 1.0f),
                new Color(0.20f, 0.38f, 0.75f, 1.0f),
                new Color(0.10f, 0.16f, 0.38f, 1.0f),
                new Color(0.72f, 0.84f, 1.00f, 1.0f));

        protected override double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleDisk(galaxy, ref random);
        }
    }
}
