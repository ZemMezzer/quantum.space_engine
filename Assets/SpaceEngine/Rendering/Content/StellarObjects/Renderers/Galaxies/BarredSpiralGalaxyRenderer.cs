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
        private void Reset()
        {
            ApplyDefaultPaletteForNewAsset();
        }

        protected override float ShaderMorphologyValue => 1.0f;

        protected override GalaxyRendererDefaults Defaults =>
            new GalaxyRendererDefaults(
                new Color(1.00f, 0.66f, 0.32f, 1.0f),
                new Color(0.30f, 0.68f, 0.92f, 1.0f),
                new Color(0.80f, 0.30f, 0.72f, 1.0f),
                new Color(0.30f, 0.50f, 0.78f, 1.0f),
                new Color(0.12f, 0.22f, 0.36f, 1.0f),
                new Color(0.82f, 0.92f, 1.00f, 1.0f));

        protected override double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleDisk(galaxy, ref random);
        }
    }
}
