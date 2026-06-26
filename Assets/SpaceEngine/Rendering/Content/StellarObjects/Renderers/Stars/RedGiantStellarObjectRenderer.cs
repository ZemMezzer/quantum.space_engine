using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    [CreateAssetMenu(
        fileName = "RedGiant Renderer",
        menuName = "Space Engine/Stellar Objects/Stars/Rendering/Red Giant")]
    public sealed class RedGiantStellarObjectRenderer
        : StarStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColors();
        }

        protected override Color DefaultSurfaceColor =>
            new Color(1.00f, 0.32f, 0.16f, 1.0f);

        protected override Color DefaultCoronaColor =>
            new Color(1.00f, 0.18f, 0.07f, 1.0f);
    }
}
