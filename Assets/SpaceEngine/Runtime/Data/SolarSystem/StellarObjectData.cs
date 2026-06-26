using SpaceEngine.Runtime.Content.Entities;

namespace SpaceEngine.Runtime.Data.SolarSystem
{
    /// <summary>
    /// Simulation data shared by every object in a solar system.
    ///
    /// The only authored metadata carried by the generic contract is Entity.
    /// It is assigned exactly once by the generator/entity binding selected by
    /// the fixed pipeline. Core physics interprets none of its metadata.
    /// </summary>
    public abstract class StellarObjectData
    {
        public StellarEntity Entity { get; private set; }
        public double MassKg { get; }
        public double RadiusMeters { get; }
        public double LuminosityWatts { get; }
        public OrbitData Orbit { get; }

        protected StellarObjectData(
            double massKg,
            double radiusMeters,
            double luminosityWatts,
            OrbitData orbit)
        {
            MassKg = massKg;
            RadiusMeters = radiusMeters;
            LuminosityWatts = luminosityWatts;
            Orbit = orbit;
        }

        /// <summary>
        /// Binds the authored entity selected next to the generator in
        /// SpaceEngineConfiguration. It is intentionally the only mutable
        /// part of generated data and may be assigned only once.
        /// </summary>
        public bool IsEntity(StellarEntity entity)
        {
            return Entity == entity;
        }

        public StellarObjectData WithEntity(StellarEntity entity)
        {
            if (entity == null)
                return this;

            if (Entity != null && Entity != entity)
            {
                throw new System.InvalidOperationException(
                    "A generated StellarObjectData instance cannot be rebound " +
                    "to a different StellarEntity.");
            }

            Entity = entity;
            return this;
        }

        /// <summary>
        /// Returns the same concrete generated object with a system-generator-
        /// authored orbit. Custom implementations must preserve Entity; use
        /// PreserveEntity on the newly-created copy.
        /// </summary>
        public abstract StellarObjectData WithOrbit(in OrbitData orbit);

        protected T PreserveEntity<T>(T copy)
            where T : StellarObjectData
        {
            if (Entity != null)
                copy.WithEntity(Entity);

            return copy;
        }
    }
}
