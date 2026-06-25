using System;

namespace SpaceEngine.Runtime.Data
{
    /// <summary>
    /// Request for a celestial body inside an addressable solar system.
    /// Unlike a system, a particular body identifier can be absent.
    /// </summary>
    public readonly struct CelestialBodyCoordinatesData :
        IEquatable<CelestialBodyCoordinatesData>
    {
        public readonly CoordinatesData SolarSystemCoordinates;
        public readonly long CelestialBodyID;

        public CelestialBodyCoordinatesData(
            CoordinatesData solarSystemCoordinates,
            long celestialBodyID)
        {
            SolarSystemCoordinates = solarSystemCoordinates;
            CelestialBodyID = celestialBodyID;
        }

        public ulong GetCelestialBodySeed()
        {
            return CoordinatesData.Combine(
                SolarSystemCoordinates.GetSolarSystemSeed(),
                CoordinatesData.ToUnsigned(CelestialBodyID));
        }

        public bool Equals(CelestialBodyCoordinatesData other)
        {
            return SolarSystemCoordinates == other.SolarSystemCoordinates &&
                   CelestialBodyID == other.CelestialBodyID;
        }

        public override bool Equals(object obj)
        {
            return obj is CelestialBodyCoordinatesData other &&
                   Equals(other);
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
