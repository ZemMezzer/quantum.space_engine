using System;

namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>
    /// Minimal reusable data for a mesh-only object. Custom content can define
    /// a richer derived data type without changing any common engine contract.
    /// </summary>
    public sealed class GenericMeshObjectData : StellarObjectData
    {
        private const double StefanBoltzmannConstant = 5.670374419e-8;

        /// <summary>
        /// Creates data with an explicit characteristic temperature.
        /// </summary>
        public GenericMeshObjectData(
            double massKg,
            double radiusMeters,
            double luminosityWatts,
            double temperatureKelvin,
            OrbitData orbit)
            : base(
                massKg,
                radiusMeters,
                luminosityWatts,
                temperatureKelvin,
                orbit)
        {
        }

        /// <summary>
        /// Compatibility overload. It derives an effective black-body
        /// temperature from radius and luminosity, or uses the cosmic
        /// background value when the object emits no light.
        /// </summary>
        public GenericMeshObjectData(
            double massKg,
            double radiusMeters,
            double luminosityWatts,
            OrbitData orbit)
            : this(
                massKg,
                radiusMeters,
                luminosityWatts,
                GetEffectiveTemperatureKelvin(radiusMeters, luminosityWatts),
                orbit)
        {
        }

        public override StellarObjectData WithOrbit(in OrbitData orbit)
        {
            return PreserveEntity(new GenericMeshObjectData(
                MassKg,
                RadiusMeters,
                LuminosityWatts,
                TemperatureKelvin,
                orbit));
        }

        private static double GetEffectiveTemperatureKelvin(
            double radiusMeters,
            double luminosityWatts)
        {
            if (radiusMeters <= 0.0 || luminosityWatts <= 0.0)
                return CosmicBackgroundTemperatureKelvin;

            var area = 4.0 * Math.PI * radiusMeters * radiusMeters;
            return Math.Pow(
                luminosityWatts /
                Math.Max(area * StefanBoltzmannConstant, double.Epsilon),
                0.25);
        }
    }
}
