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
        private void Reset()
        {
            ApplyDefaultPaletteForNewAsset();
        }

        protected override float ShaderMorphologyValue => 4.0f;

        protected override GalaxyRendererDefaults Defaults =>
            new GalaxyRendererDefaults(
                new Color(0.88f, 0.70f, 0.34f, 1.0f),
                new Color(0.20f, 0.76f, 0.72f, 1.0f),
                new Color(0.80f, 0.25f, 0.66f, 1.0f),
                new Color(0.30f, 0.55f, 0.54f, 1.0f),
                new Color(0.08f, 0.26f, 0.26f, 1.0f),
                new Color(0.70f, 1.00f, 0.92f, 1.0f));

        protected override double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleIrregular(galaxy, ref random);
        }
    }
}
