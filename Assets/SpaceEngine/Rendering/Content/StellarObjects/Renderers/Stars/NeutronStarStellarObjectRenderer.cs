using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    [CreateAssetMenu(
        fileName = "NeutronStar Renderer",
        menuName = "Space Engine/Stellar Objects/Stars/Rendering/Neutron Star")]
    public sealed class NeutronStarStellarObjectRenderer
        : StarStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColors();
        }

        protected override Color DefaultSurfaceColor =>
            new Color(0.48f, 0.72f, 1.00f, 1.0f);

        protected override Color DefaultCoronaColor =>
            new Color(0.28f, 0.52f, 1.00f, 1.0f);
    }
}
