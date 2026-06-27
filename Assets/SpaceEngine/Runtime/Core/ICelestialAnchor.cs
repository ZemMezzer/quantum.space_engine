using System;
using SpaceEngine.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Core
{
    /// <summary>
    /// Small interface for systems that only need to inspect or move a
    /// traveller. The caller owns the concrete CelestialAnchor instance,
    /// which is abstract and IDisposable.
    /// </summary>
    public interface ICelestialAnchor : IDisposable
    {
        bool IsConfigured { get; }
        bool IsDisposed { get; }
        CoordinatesData Coordinates { get; }
        CelestialPositionData Position { get; }
        double3 SolarSystemLocalPositionMeters { get; }
        double3 GalaxyLocalPositionLightYears { get; }

        CelestialPositionData GetMoveData(CoordinatesData coordinates);

        bool TryGetMoveData(
            CelestialBodyCoordinatesData bodyCoordinates,
            out CelestialPositionData positionData);

        double GetTemperatureLocal();
        double3 GetGravitationVector();
        double GetGravitationForce();

        void Activate();
        void Refresh();
        void Move(CelestialPositionData positionData);
        void Move(CoordinatesData coordinates);
        void Move(
            CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters);
        void Move(Vector3 direction, double distanceMeters);
        void MoveByMeters(double3 deltaMeters);
        void SetSolarSystemLocalPositionMeters(double3 positionMeters);
    }
}
