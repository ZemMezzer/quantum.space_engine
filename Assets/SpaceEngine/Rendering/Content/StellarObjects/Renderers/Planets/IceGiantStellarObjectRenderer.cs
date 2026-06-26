using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Planets
{
    [CreateAssetMenu(
        fileName = "Ice Giant Renderer",
        menuName = "Space Engine/Stellar Objects/Planets/Rendering/Ice Giant")]
    public sealed class IceGiantStellarObjectRenderer
        : PlanetStellarObjectRenderer
    {
        private void Reset()
        {
            ApplyDefaultColor();
        }

        protected override Color DefaultColor =>
            new Color(0.30f, 0.80f, 1.00f, 1.0f);
    }
}
