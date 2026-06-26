using SpaceEngine.Runtime.Data.SolarSystem;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Rendering
{
    /// <summary>
    /// Base renderer contract retained in Runtime so content packages can
    /// declare renderer assets without making SpaceEngine depend on a concrete
    /// graphics module. The renderer is selected by StellarEntity binding, not
    /// by concrete data-class checks.
    /// </summary>
    public abstract class StellarObjectRenderer : ScriptableObject
    {
        public abstract IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context);

        public virtual bool TryGetDistantPointStyle(
            StellarObjectData data,
            out Color color,
            out float intensity)
        {
            color = Color.white;
            intensity = 1.5f;
            return false;
        }
    }
}
