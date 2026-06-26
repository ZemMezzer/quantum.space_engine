using System;
using SpaceEngine.Runtime.Content.Entities;

namespace SpaceEngine.Runtime.Data.SolarSystem
{
    /// <summary>
    /// Generated system result. Objects may use arbitrary derived data classes;
    /// entity-based queries remain independent of those classes.
    /// </summary>
    public sealed class SolarSystemData
    {
        public ulong Seed { get; }
        public StellarObjectData[] StellarObjects { get; }

        public SolarSystemData(ulong seed, StellarObjectData[] stellarObjects)
        {
            Seed = seed;
            StellarObjects = stellarObjects ??
                             Array.Empty<StellarObjectData>();
        }

        /// <summary>
        /// Finds the first generated object paired to an authored entity.
        /// Gameplay can use this for a known type without checking concrete
        /// StarData, PlanetData or other implementation classes.
        /// </summary>
        public bool TryFindFirst(
            StellarEntity entity,
            out int objectIndex,
            out StellarObjectData data)
        {
            if (entity != null)
            {
                for (var index = 0; index < StellarObjects.Length; index++)
                {
                    var candidate = StellarObjects[index];
                    if (candidate?.Entity != entity)
                        continue;

                    objectIndex = index;
                    data = candidate;
                    return true;
                }
            }

            objectIndex = -1;
            data = null;
            return false;
        }
    }
}
