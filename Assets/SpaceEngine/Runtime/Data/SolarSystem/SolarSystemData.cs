using SpaceEngine.Runtime.Data.Planet;
using Unity.Collections;

namespace SpaceEngine.Runtime.Data.SolarSystem
{
    /// <summary>
    /// Generated runtime data for one stellar system.
    /// Supports single and binary-star systems.
    /// </summary>
    public struct SolarSystemData
    {
        public const byte MAX_STARS = 3;
        public const byte MAX_PLANETS = 16;

        public readonly ulong Seed;
        public readonly byte StarCount;
        public readonly byte PlanetCount;

        public readonly FixedList512Bytes<StarData> Stars;
        public readonly FixedList4096Bytes<PlanetData> Planets;

        internal SolarSystemData(
            ulong seed,
            FixedList512Bytes<StarData> stars,
            FixedList4096Bytes<PlanetData> planets)
        {
            Seed = seed;
            Stars = stars;
            Planets = planets;

            StarCount = (byte)stars.Length;
            PlanetCount = (byte)planets.Length;
        }
    }
}