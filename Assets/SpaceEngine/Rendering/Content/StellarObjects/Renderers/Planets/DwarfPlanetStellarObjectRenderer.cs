using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Planets
{
    [CreateAssetMenu(
        fileName = "Dwarf Planet Renderer",
        menuName = "Space Engine/Stellar Objects/Planets/Rendering/Dwarf Planet")]
    public sealed class DwarfPlanetStellarObjectRenderer
        : PlanetStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColor();
        }

        protected override Color DefaultColor =>
            new Color(0.58f, 0.50f, 0.44f, 1.0f);
    }
}
