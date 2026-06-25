using System;

namespace SpaceEngine.Runtime.Data
{
    /// <summary>
    /// Logical address of a solar system.
    ///
    /// These are gameplay data, not packed streaming-sector identifiers.
    /// Every value in the signed long range is valid. The engine resolves the
    /// hidden spatial placement internally when an anchor moves here.
    /// </summary>
    public readonly struct CoordinatesData : IEquatable<CoordinatesData>
    {
        public readonly long UniverseID;
        public readonly long GalaxyID;
        public readonly long SolarSystemID;

        public CoordinatesData(
            long universeID,
            long galaxyID,
            long solarSystemID)
        {
            UniverseID = universeID;
            GalaxyID = galaxyID;
            SolarSystemID = solarSystemID;
        }

        public ulong GetUniverseSeed()
        {
            return Mix(ToUnsigned(UniverseID));
        }

        public ulong GetGalaxySeed()
        {
            return Combine(
                GetUniverseSeed(),
                ToUnsigned(GalaxyID));
        }

        public ulong GetSolarSystemSeed()
        {
            return Combine(
                GetGalaxySeed(),
                ToUnsigned(SolarSystemID));
        }

        public bool Equals(CoordinatesData other)
        {
            return UniverseID == other.UniverseID &&
                   GalaxyID == other.GalaxyID &&
                   SolarSystemID == other.SolarSystemID;
        }

        public override bool Equals(object obj)
        {
            return obj is CoordinatesData other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = GetSolarSystemSeed();
            return unchecked((int)(hash ^ (hash >> 32)));
        }

        public static bool operator ==(
            CoordinatesData left,
            CoordinatesData right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CoordinatesData left,
            CoordinatesData right)
        {
            return !left.Equals(right);
        }

        internal static ulong Combine(ulong seed, ulong value)
        {
            unchecked
            {
                return Mix(seed ^ (value + 0x9E3779B97F4A7C15UL));
            }
        }

        internal static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        internal static ulong ToUnsigned(long value)
        {
            return unchecked((ulong)value);
        }
    }
}
