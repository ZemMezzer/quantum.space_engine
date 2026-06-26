using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Planets
{
    [CreateAssetMenu(
        fileName = "Ocean Planet Renderer",
        menuName = "Space Engine/Stellar Objects/Planets/Rendering/Ocean Planet")]
    public sealed class OceanPlanetStellarObjectRenderer
        : PlanetStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColor();
        }

        protected override Color DefaultColor =>
            new Color(0.08f, 0.42f, 0.95f, 1.0f);
    }
}
