namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>Physical data specific to a luminous star.</summary>
    public sealed class StarData : StellarObjectData
    {
        public double DensityKgPerCubicMeter { get; }
        public double SurfaceGravityMetersPerSecondSquared { get; }
        public double SurfaceTemperatureKelvin { get; }
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
            : base(massKg, radiusMeters, luminosityWatts, orbit)
        {
            DensityKgPerCubicMeter = densityKgPerCubicMeter;
            SurfaceGravityMetersPerSecondSquared =
                surfaceGravityMetersPerSecondSquared;
            SurfaceTemperatureKelvin = surfaceTemperatureKelvin;
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
                SurfaceTemperatureKelvin,
                LuminosityWatts,
                RotationPeriodSeconds,
                AgeYears,
                Metallicity,
                orbit));
        }
    }
}
