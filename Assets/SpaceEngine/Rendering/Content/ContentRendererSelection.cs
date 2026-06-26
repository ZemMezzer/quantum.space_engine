using System.Collections.Generic;
using SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;

namespace SpaceEngine.Rendering.Content
{
    internal static class ContentRendererSelection
    {
        public static StellarObjectRenderer SelectStellarObjectRendererOrNull(
            IReadOnlyList<StellarObjectRendererBinding> bindings,
            StellarEntity entity)
        {
            if (bindings == null || entity == null)
                return null;

            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding?.Entity == entity)
                    return binding.Renderer;
            }

            return null;
        }

        public static GalaxyRenderer SelectGalaxyRendererOrNull(
            IReadOnlyList<GalaxyRendererBinding> bindings,
            StellarEntity entity)
        {
            if (bindings == null || entity == null)
                return null;

            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding?.Entity == entity)
                    return binding.Renderer;
            }

            return null;
        }
    }
}
