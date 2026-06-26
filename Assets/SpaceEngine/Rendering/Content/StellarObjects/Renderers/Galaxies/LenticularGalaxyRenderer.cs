using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    [CreateAssetMenu(
        fileName = "Lenticular Galaxy Renderer",
        menuName = "Space Engine/Stellar Objects/Galaxies/Rendering/Lenticular")]
    public sealed class LenticularGalaxyRenderer : ProceduralGalaxyRenderer
    {
        private void Reset()
        {
            ApplyDefaultPaletteForNewAsset();
        }

        protected override float ShaderMorphologyValue => 3.0f;

        protected override GalaxyRendererDefaults Defaults =>
            new GalaxyRendererDefaults(
                new Color(1.00f, 0.78f, 0.50f, 1.0f),
                new Color(0.62f, 0.72f, 0.88f, 1.0f),
                new Color(0.52f, 0.48f, 0.78f, 1.0f),
                new Color(0.46f, 0.54f, 0.70f, 1.0f),
                new Color(0.16f, 0.20f, 0.34f, 1.0f),
                new Color(0.82f, 0.90f, 1.00f, 1.0f));

        protected override double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleDisk(galaxy, ref random);
        }
    }
}
