namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>Physical data specific to a built-in planet.</summary>
    public sealed class PlanetData : StellarObjectData
    {
        public double DensityKgPerCubicMeter { get; }
        public double SurfaceGravityMetersPerSecondSquared { get; }

        /// <summary>
        /// Backwards-compatible name for the common characteristic
        /// temperature. Planet temperatures are surface temperatures.
        /// </summary>
        public double SurfaceTemperatureKelvin => TemperatureKelvin;

        public double AtmospherePressurePascals { get; }
        public double WaterCoverage { get; }
        public double IceCoverage { get; }
        public double VolcanicActivity { get; }
        public byte SatelliteCount { get; }
        public bool HasAtmosphere { get; }
        public bool HasRings { get; }

        public PlanetData(
            double massKg,
            double radiusMeters,
            double densityKgPerCubicMeter,
            double surfaceGravityMetersPerSecondSquared,
            double surfaceTemperatureKelvin,
            double atmospherePressurePascals,
            double waterCoverage,
            double iceCoverage,
            double volcanicActivity,
            byte satelliteCount,
            bool hasAtmosphere,
            bool hasRings,
            OrbitData orbit)
            : base(
                massKg,
                radiusMeters,
                0.0,
                surfaceTemperatureKelvin,
                orbit)
        {
            DensityKgPerCubicMeter = densityKgPerCubicMeter;
            SurfaceGravityMetersPerSecondSquared =
                surfaceGravityMetersPerSecondSquared;
            AtmospherePressurePascals = atmospherePressurePascals;
            WaterCoverage = waterCoverage;
            IceCoverage = iceCoverage;
            VolcanicActivity = volcanicActivity;
            SatelliteCount = satelliteCount;
            HasAtmosphere = hasAtmosphere;
            HasRings = hasRings;
        }

        public override StellarObjectData WithOrbit(in OrbitData orbit)
        {
            return PreserveEntity(new PlanetData(
                MassKg,
                RadiusMeters,
                DensityKgPerCubicMeter,
                SurfaceGravityMetersPerSecondSquared,
                TemperatureKelvin,
                AtmospherePressurePascals,
                WaterCoverage,
                IceCoverage,
                VolcanicActivity,
                SatelliteCount,
                HasAtmosphere,
                HasRings,
                orbit));
        }
    }
}
