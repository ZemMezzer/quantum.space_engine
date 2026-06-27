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
        protected override GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy)
        {
            return new GalaxyVisualProfile(
                0x44574152465F4741UL,
                6.0f,
                new Color(0.64f, 0.84f, 1.00f, 1.0f),
                new Color(0.30f, 0.52f, 0.94f, 1.0f),
                new Color(0.32f, 0.86f, 0.80f, 1.0f),
                new Color(0.20f, 0.40f, 0.72f, 1.0f),
                new Color(0.06f, 0.12f, 0.28f, 1.0f),
                new Color(0.72f, 0.86f, 1.00f, 1.0f),
                0.72f,
                0.98f,
                1.06f,
                0.86f,
                0.94f,
                1.18f,
                false,
                false,
                true,
                false,
                true,
                0.04f,
                0.15f,
                0.15f,
                0.12f,
                0.10f);
        }

        protected override double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return SampleDwarf(galaxy, ref random);
        }
    }
}
