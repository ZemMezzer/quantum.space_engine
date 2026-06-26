using System;

namespace SpaceEngine.Runtime.Content.StellarObjects.Rendering
{
    /// <summary>
    /// Runtime visual created and fully owned by one StellarObjectRenderer.
    /// The shared rendering runtime only forwards generic placement and
    /// lifecycle events; it never branches on celestial-object type.
    /// </summary>
    public interface IStellarObjectVisual : IDisposable
    {
        void SetVisible(bool isVisible);

        void Update(in StellarObjectVisualUpdateContext context);

        /// <summary>
        /// Whether this visual can replace the far system point at the requested
        /// apparent diameter. Objects that do not own that handoff return false.
        /// </summary>
        bool IsDistantPointReplacementReady(
            float requiredAngularDiameterDegrees);
    }
}
