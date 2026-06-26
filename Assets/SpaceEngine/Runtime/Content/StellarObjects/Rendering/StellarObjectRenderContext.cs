using SpaceEngine.Runtime.Data.SolarSystem;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Rendering
{
    /// <summary>
    /// One-time scene data supplied to a renderer when it creates a visual.
    /// It contains no object-creation helper: the renderer owns every
    /// GameObject, mesh, material and child hierarchy it needs.
    /// </summary>
    public readonly struct StellarObjectRenderContext
    {
        public readonly Transform Parent;
        public readonly Camera Camera;
        public readonly int Layer;
        public readonly double MetersPerUnityUnit;
        public readonly StellarObjectData Data;
        public readonly int ObjectIndex;

        public StellarObjectRenderContext(
            Transform parent,
            Camera camera,
            int layer,
            double metersPerUnityUnit,
            StellarObjectData data,
            int objectIndex)
        {
            Parent = parent;
            Camera = camera;
            Layer = layer;
            MetersPerUnityUnit = metersPerUnityUnit;
            Data = data;
            ObjectIndex = objectIndex;
        }
    }
}
