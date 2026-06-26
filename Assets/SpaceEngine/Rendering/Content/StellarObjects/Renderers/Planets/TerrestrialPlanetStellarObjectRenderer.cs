using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Planets
{
    [CreateAssetMenu(
        fileName = "Terrestrial Planet Renderer",
        menuName = "Space Engine/Stellar Objects/Planets/Rendering/Terrestrial Planet")]
    public sealed class TerrestrialPlanetStellarObjectRenderer
        : PlanetStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColor();
        }

        protected override Color DefaultColor =>
            new Color(0.55f, 0.74f, 0.34f, 1.0f);
    }
}
