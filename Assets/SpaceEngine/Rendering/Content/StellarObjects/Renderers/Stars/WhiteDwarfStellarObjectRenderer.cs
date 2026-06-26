using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    [CreateAssetMenu(
        fileName = "WhiteDwarf Renderer",
        menuName = "Space Engine/Stellar Objects/Stars/Rendering/White Dwarf")]
    public sealed class WhiteDwarfStellarObjectRenderer
        : StarStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColors();
        }

        protected override Color DefaultSurfaceColor =>
            new Color(0.80f, 0.90f, 1.00f, 1.0f);

        protected override Color DefaultCoronaColor =>
            new Color(0.58f, 0.74f, 1.00f, 1.0f);
    }
}
