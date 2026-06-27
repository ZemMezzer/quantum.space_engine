using System;
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
        /// <summary>
        /// Baseline temperature assigned when an older custom data type does
        /// not yet provide its own value. It represents deep-space background
        /// radiation rather than a surface temperature.
        /// </summary>
        public const double CosmicBackgroundTemperatureKelvin = 2.725;

        public StellarEntity Entity { get; private set; }
        public double MassKg { get; }
        public double RadiusMeters { get; }
        public double LuminosityWatts { get; }

        /// <summary>
        /// Characteristic generated temperature in kelvin. For stars and
        /// planets this is the surface temperature. A black hole with an
        /// accretion disk exposes the disk's effective colour temperature;
        /// without a disk it exposes its Hawking temperature.
        /// </summary>
        public double TemperatureKelvin { get; }

        /// <summary>
        /// Characteristic radius of the emitting region in metres. Physics
        /// uses it to keep radiation finite at the source and to report the
        /// object's own generated temperature while the query point is inside
        /// that region. Ordinary bodies emit from their physical radius;
        /// extended emitters, such as an accretion disk, override it.
        /// </summary>
        public virtual double RadiatingRadiusMeters => RadiusMeters;

        public OrbitData Orbit { get; }

        protected StellarObjectData(
            double massKg,
            double radiusMeters,
            double luminosityWatts,
            double temperatureKelvin,
            OrbitData orbit)
        {
            MassKg = massKg;
            RadiusMeters = radiusMeters;
            LuminosityWatts = luminosityWatts;
            TemperatureKelvin = Math.Max(0.0, temperatureKelvin);
            Orbit = orbit;
        }

        /// <summary>
        /// Compatibility constructor for external data types compiled against
        /// the older common contract. New data types should pass an explicit
        /// temperature through the overload above.
        /// </summary>
        protected StellarObjectData(
            double massKg,
            double radiusMeters,
            double luminosityWatts,
            OrbitData orbit)
            : this(
                massKg,
                radiusMeters,
                luminosityWatts,
                CosmicBackgroundTemperatureKelvin,
                orbit)
        {
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
                throw new InvalidOperationException(
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
