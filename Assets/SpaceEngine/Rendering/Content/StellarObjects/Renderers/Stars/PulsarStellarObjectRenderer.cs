using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    [CreateAssetMenu(
        fileName = "Pulsar Renderer",
        menuName = "Space Engine/Stellar Objects/Stars/Rendering/Pulsar")]
    public sealed class PulsarStellarObjectRenderer
        : StarStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColors();
        }

        protected override Color DefaultSurfaceColor =>
            new Color(0.40f, 0.62f, 1.00f, 1.0f);

        protected override Color DefaultCoronaColor =>
            new Color(0.72f, 0.22f, 1.00f, 1.0f);
    }
}
