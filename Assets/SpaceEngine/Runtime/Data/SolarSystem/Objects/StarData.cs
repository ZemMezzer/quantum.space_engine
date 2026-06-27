namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>Physical data specific to a luminous star.</summary>
    public sealed class StarData : StellarObjectData
    {
        public double DensityKgPerCubicMeter { get; }
        public double SurfaceGravityMetersPerSecondSquared { get; }

        /// <summary>
        /// Backwards-compatible name for the common characteristic
        /// temperature. Star temperatures are surface temperatures.
        /// </summary>
        public double SurfaceTemperatureKelvin => TemperatureKelvin;

        public double RotationPeriodSeconds { get; }
        public double AgeYears { get; }
        public double Metallicity { get; }

        public StarData(
            double massKg,
            double radiusMeters,
            double densityKgPerCubicMeter,
            double surfaceGravityMetersPerSecondSquared,
            double surfaceTemperatureKelvin,
            double luminosityWatts,
            double rotationPeriodSeconds,
            double ageYears,
            double metallicity,
            OrbitData orbit)
            : base(
                massKg,
                radiusMeters,
                luminosityWatts,
                surfaceTemperatureKelvin,
                orbit)
        {
            DensityKgPerCubicMeter = densityKgPerCubicMeter;
            SurfaceGravityMetersPerSecondSquared =
                surfaceGravityMetersPerSecondSquared;
            RotationPeriodSeconds = rotationPeriodSeconds;
            AgeYears = ageYears;
            Metallicity = metallicity;
        }

        public override StellarObjectData WithOrbit(in OrbitData orbit)
        {
            return PreserveEntity(new StarData(
                MassKg,
                RadiusMeters,
                DensityKgPerCubicMeter,
                SurfaceGravityMetersPerSecondSquared,
                TemperatureKelvin,
                LuminosityWatts,
                RotationPeriodSeconds,
                AgeYears,
                Metallicity,
                orbit));
        }
    }
}
