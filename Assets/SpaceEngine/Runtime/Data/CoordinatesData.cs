using System;

namespace SpaceEngine.Runtime.Data
{
    /// <summary>
    /// Logical coordinates of an existing solar system.
    /// Spatial sectors and physical positions are resolved internally by the engine.
    /// </summary>
    public readonly struct CoordinatesData : IEquatable<CoordinatesData>
    {
        public readonly ulong UniverseID;
        public readonly ulong GalaxyID;
        public readonly ulong SolarSystemID;

        public CoordinatesData(
            ulong universeID,
            ulong galaxyID,
            ulong solarSystemID)
        {
            UniverseID = universeID;
            GalaxyID = galaxyID;
            SolarSystemID = solarSystemID;
        }

        public ulong GetUniverseSeed()
        {
            return Mix(UniverseID);
        }

        public ulong GetGalaxySeed()
        {
            return Combine(
                GetUniverseSeed(),
                GalaxyID);
        }

        public ulong GetSolarSystemSeed()
        {
            return Combine(
                GetGalaxySeed(),
                SolarSystemID);
        }

        public bool Equals(CoordinatesData other)
        {
            return UniverseID == other.UniverseID
                   && GalaxyID == other.GalaxyID
                   && SolarSystemID == other.SolarSystemID;
        }

        public override bool Equals(object obj)
        {
            return obj is CoordinatesData other
                   && Equals(other);
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

        internal static ulong Combine(
            ulong seed,
            ulong value)
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
    }
}