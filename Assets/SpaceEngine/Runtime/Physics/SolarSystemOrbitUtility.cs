using System;
using SpaceEngine.Runtime.Data;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Physics
{
    /// <summary>
    /// Data-only Kepler evaluation shared by physics and rendering. It depends
    /// only on OrbitData and is deliberately outside the rendering module.
    /// </summary>
    public static class SolarSystemOrbitUtility
    {
        public const double GravitationalConstant = 6.67430e-11;

        public static double3 GetPositionMeters(
            in OrbitData orbit,
            double gravitationalParameter,
            double simulationTimeSeconds)
        {
            if (orbit.SemiMajorAxisMeters <= 0.0 ||
                gravitationalParameter <= 0.0)
            {
                return double3.zero;
            }

            var semiMajorAxis = orbit.SemiMajorAxisMeters;
            var eccentricity = math.clamp(
                orbit.Eccentricity,
                0.0,
                0.999999);

            var meanMotion = Math.Sqrt(
                gravitationalParameter /
                (semiMajorAxis * semiMajorAxis * semiMajorAxis));

            var meanAnomaly = NormalizeRadians(
                orbit.MeanAnomalyAtEpochRadians +
                meanMotion *
                (simulationTimeSeconds - orbit.EpochSeconds));

            var eccentricAnomaly = SolveEccentricAnomaly(
                meanAnomaly,
                eccentricity);
            var cosine = Math.Cos(eccentricAnomaly);
            var sine = Math.Sin(eccentricAnomaly);
            var ellipseYScale = Math.Sqrt(
                1.0 - eccentricity * eccentricity);

            var orbitalX = semiMajorAxis * (cosine - eccentricity);
            var orbitalY = semiMajorAxis * ellipseYScale * sine;

            return RotateOrbitalPlane(
                orbitalX,
                orbitalY,
                orbit.ArgumentOfPeriapsisRadians,
                orbit.InclinationRadians,
                orbit.LongitudeOfAscendingNodeRadians);
        }

        private static double SolveEccentricAnomaly(
            double meanAnomaly,
            double eccentricity)
        {
            var anomaly = eccentricity < 0.8
                ? meanAnomaly
                : Math.PI;

            for (var iteration = 0; iteration < 10; iteration++)
            {
                var sine = Math.Sin(anomaly);
                var cosine = Math.Cos(anomaly);
                var delta = (anomaly - eccentricity * sine -
                             meanAnomaly) /
                            (1.0 - eccentricity * cosine);
                anomaly -= delta;

                if (Math.Abs(delta) < 0.0000000001)
                    break;
            }

            return anomaly;
        }

        private static double3 RotateOrbitalPlane(
            double orbitalX,
            double orbitalY,
            double argumentOfPeriapsis,
            double inclination,
            double longitudeOfAscendingNode)
        {
            var cosArgument = Math.Cos(argumentOfPeriapsis);
            var sinArgument = Math.Sin(argumentOfPeriapsis);
            var argumentX = cosArgument * orbitalX -
                            sinArgument * orbitalY;
            var argumentY = sinArgument * orbitalX +
                            cosArgument * orbitalY;

            var cosInclination = Math.Cos(inclination);
            var sinInclination = Math.Sin(inclination);
            var inclinedX = argumentX;
            var inclinedY = cosInclination * argumentY;
            var inclinedZ = sinInclination * argumentY;

            var cosNode = Math.Cos(longitudeOfAscendingNode);
            var sinNode = Math.Sin(longitudeOfAscendingNode);

            return new double3(
                cosNode * inclinedX - sinNode * inclinedY,
                sinNode * inclinedX + cosNode * inclinedY,
                inclinedZ);
        }

        private static double NormalizeRadians(double radians)
        {
            var fullTurn = Math.PI * 2.0;
            radians %= fullTurn;
            return radians < 0.0 ? radians + fullTurn : radians;
        }
    }
}
