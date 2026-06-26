using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Dwarf Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Dwarf")]
    public sealed class DwarfGalaxyRenderer : ProceduralGalaxyRenderer
    {
        private void Reset()
        {
            ApplyDefaultPaletteForNewAsset();
        }

        protected override float ShaderMorphologyValue => 6.0f;

        protected override GalaxyRendererDefaults Defaults =>
            new GalaxyRendererDefaults(
                new Color(0.64f, 0.84f, 1.00f, 1.0f),
                new Color(0.30f, 0.52f, 0.94f, 1.0f),
                new Color(0.32f, 0.86f, 0.80f, 1.0f),
                new Color(0.20f, 0.40f, 0.72f, 1.0f),
                new Color(0.06f, 0.12f, 0.28f, 1.0f),
                new Color(0.72f, 0.86f, 1.00f, 1.0f));

        protected override double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleIrregular(galaxy, ref random);
        }
    }
}
