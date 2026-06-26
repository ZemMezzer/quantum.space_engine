using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Planets
{
    [CreateAssetMenu(
        fileName = "Gas Giant Renderer",
        menuName = "Space Engine/Stellar Objects/Planets/Rendering/Gas Giant")]
    public sealed class GasGiantStellarObjectRenderer
        : PlanetStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColor();
        }

        protected override Color DefaultColor =>
            new Color(0.85f, 0.56f, 0.25f, 1.0f);
    }
}
