using SpaceEngine.Runtime.Data.SolarSystem;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Rendering
{
    /// <summary>
    /// Per-frame physical placement supplied by the generic rendering runtime.
    /// Concrete visuals decide how to present it; this contract contains no
    /// star, planet, black-hole or renderer-specific field.
    /// </summary>
    public readonly struct StellarObjectVisualUpdateContext
    {
        public readonly StellarObjectData Data;
        public readonly int ObjectIndex;
        public readonly Camera Camera;
        public readonly double3 BarycentricPositionMeters;
        public readonly double3 RelativePositionMeters;
        public readonly double DistanceToCameraMeters;
        public readonly double SimulationTimeSeconds;
        public readonly double MetersPerUnityUnit;
        public readonly bool Immediate;

        public StellarObjectVisualUpdateContext(
            StellarObjectData data,
            int objectIndex,
            Camera camera,
            double3 barycentricPositionMeters,
            double3 relativePositionMeters,
            double distanceToCameraMeters,
            double simulationTimeSeconds,
            double metersPerUnityUnit,
            bool immediate)
        {
            Data = data;
            ObjectIndex = objectIndex;
            Camera = camera;
            BarycentricPositionMeters = barycentricPositionMeters;
            RelativePositionMeters = relativePositionMeters;
            DistanceToCameraMeters = distanceToCameraMeters;
            SimulationTimeSeconds = simulationTimeSeconds;
            MetersPerUnityUnit = metersPerUnityUnit;
            Immediate = immediate;
        }
    }
}
