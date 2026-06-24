using System;

namespace SpaceEngine.Runtime.Data
{
    /// <summary>
    /// Request for a celestial body inside an existing solar system.
    /// The requested body may resolve to a planet, moon, asteroid,
    /// station, or may not exist.
    /// </summary>
    public readonly struct CelestialBodyCoordinatesData :
        IEquatable<CelestialBodyCoordinatesData>
    {
        public readonly CoordinatesData SolarSystemCoordinates;
        public readonly ulong CelestialBodyID;

        public CelestialBodyCoordinatesData(
            CoordinatesData solarSystemCoordinates,
            ulong celestialBodyID)
        {
            SolarSystemCoordinates = solarSystemCoordinates;
            CelestialBodyID = celestialBodyID;
        }

        public ulong GetCelestialBodySeed()
        {
            return CoordinatesData.Combine(
                SolarSystemCoordinates.GetSolarSystemSeed(),
                CelestialBodyID);
        }

        public bool Equals(CelestialBodyCoordinatesData other)
        {
            return SolarSystemCoordinates == other.SolarSystemCoordinates
                   && CelestialBodyID == other.CelestialBodyID;
        }

        public override bool Equals(object obj)
        {
            return obj is CelestialBodyCoordinatesData other
                   && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = GetCelestialBodySeed();

            return unchecked((int)(hash ^ (hash >> 32)));
        }

        public static bool operator ==(
            CelestialBodyCoordinatesData left,
            CelestialBodyCoordinatesData right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CelestialBodyCoordinatesData left,
            CelestialBodyCoordinatesData right)
        {
            return !left.Equals(right);
        }
    }
}