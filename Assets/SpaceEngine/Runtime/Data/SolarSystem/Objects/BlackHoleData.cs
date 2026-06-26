namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>Physical data specific to a black hole.</summary>
    public sealed class BlackHoleData : StellarObjectData
    {
        public double RotationPeriodSeconds { get; }
        public double AgeYears { get; }
        public double Metallicity { get; }
        public bool HasAccretionDisk { get; }

        public BlackHoleData(
            double massKg,
            double radiusMeters,
            double rotationPeriodSeconds,
            double ageYears,
            double metallicity,
            bool hasAccretionDisk,
            OrbitData orbit)
            : base(massKg, radiusMeters, 0.0, orbit)
        {
            RotationPeriodSeconds = rotationPeriodSeconds;
            AgeYears = ageYears;
            Metallicity = metallicity;
            HasAccretionDisk = hasAccretionDisk;
        }

        public override StellarObjectData WithOrbit(in OrbitData orbit)
        {
            return PreserveEntity(new BlackHoleData(
                MassKg,
                RadiusMeters,
                RotationPeriodSeconds,
                AgeYears,
                Metallicity,
                HasAccretionDisk,
                orbit));
        }
    }
}
