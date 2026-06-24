namespace SpaceEngine.Runtime.Data.Planet
{
    /// <summary>
    /// Physical, orbital and environmental data for one generated planet.
    /// </summary>
    public struct PlanetData
    {
        public readonly PlanetType Type;

        public readonly double MassKg;
        public readonly double RadiusMeters;
        public readonly double DensityKgPerCubicMeter;

        public readonly double SurfaceGravityMetersPerSecondSquared;
        public readonly double SurfaceTemperatureKelvin;

        public readonly double AtmospherePressurePascals;
        public readonly double WaterCoverage;
        public readonly double IceCoverage;
        public readonly double VolcanicActivity;

        public readonly bool HasAtmosphere;
        public readonly bool HasRings;

        public readonly byte MoonCount;

        /// <summary>
        /// Orbit around the parent star or the system barycenter.
        /// </summary>
        public readonly OrbitData Orbit;

        internal PlanetData(
            PlanetType type,
            double massKg,
            double radiusMeters,
            double densityKgPerCubicMeter,
            double surfaceGravityMetersPerSecondSquared,
            double surfaceTemperatureKelvin,
            double atmospherePressurePascals,
            double waterCoverage,
            double iceCoverage,
            double volcanicActivity,
            bool hasAtmosphere,
            bool hasRings,
            byte moonCount,
            OrbitData orbit)
        {
            Type = type;

            MassKg = massKg;
            RadiusMeters = radiusMeters;
            DensityKgPerCubicMeter = densityKgPerCubicMeter;

            SurfaceGravityMetersPerSecondSquared =
                surfaceGravityMetersPerSecondSquared;

            SurfaceTemperatureKelvin = surfaceTemperatureKelvin;

            AtmospherePressurePascals = atmospherePressurePascals;

            WaterCoverage = waterCoverage;
            IceCoverage = iceCoverage;
            VolcanicActivity = volcanicActivity;

            HasAtmosphere = hasAtmosphere;
            HasRings = hasRings;

            MoonCount = moonCount;

            Orbit = orbit;
        }
    }
}