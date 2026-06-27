using System;

namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>Physical data specific to a black hole.</summary>
    public sealed class BlackHoleData : StellarObjectData
    {
        private const double StefanBoltzmannConstant = 5.670374419e-8;

        /// <summary>
        /// The geometric range used by both the visual disk and the local
        /// environment queries. Keeping it here makes physics independent of
        /// the black-hole renderer.
        /// </summary>
        public const double AccretionDiskInnerRadiusInHorizonRadii = 1.18;
        public const double AccretionDiskOuterRadiusInHorizonRadii = 20.0;

        public double RotationPeriodSeconds { get; }
        public double AgeYears { get; }
        public double Metallicity { get; }
        public bool HasAccretionDisk { get; }

        /// <summary>
        /// Effective temperature of the visible accretion disk. It is zero
        /// when this black hole has no disk; use TemperatureKelvin for a
        /// temperature guaranteed to exist on every StellarObjectData.
        /// </summary>
        public double AccretionDiskTemperatureKelvin =>
            HasAccretionDisk ? TemperatureKelvin : 0.0;

        public double AccretionDiskInnerRadiusMeters =>
            HasAccretionDisk
                ? RadiusMeters * AccretionDiskInnerRadiusInHorizonRadii
                : 0.0;

        public double AccretionDiskOuterRadiusMeters =>
            HasAccretionDisk
                ? RadiusMeters * AccretionDiskOuterRadiusInHorizonRadii
                : 0.0;

        /// <summary>
        /// A disk is an extended thermal source, unlike the event horizon.
        /// This allows local-environment queries inside its visible radius to
        /// return the generated disk temperature instead of deep-space CMB.
        /// </summary>
        public override double RadiatingRadiusMeters =>
            HasAccretionDisk
                ? AccretionDiskOuterRadiusMeters
                : RadiusMeters;

        public BlackHoleData(
            double massKg,
            double radiusMeters,
            double rotationPeriodSeconds,
            double ageYears,
            double metallicity,
            double temperatureKelvin,
            bool hasAccretionDisk,
            OrbitData orbit)
            : base(
                massKg,
                radiusMeters,
                GetAccretionDiskLuminosityWatts(
                    radiusMeters,
                    temperatureKelvin,
                    hasAccretionDisk),
                temperatureKelvin,
                orbit)
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
                TemperatureKelvin,
                HasAccretionDisk,
                orbit));
        }

        private static double GetAccretionDiskLuminosityWatts(
            double horizonRadiusMeters,
            double temperatureKelvin,
            bool hasAccretionDisk)
        {
            if (!hasAccretionDisk ||
                horizonRadiusMeters <= 0.0 ||
                temperatureKelvin <= 0.0)
            {
                return 0.0;
            }

            var innerRadiusMeters = horizonRadiusMeters *
                                    AccretionDiskInnerRadiusInHorizonRadii;
            var outerRadiusMeters = horizonRadiusMeters *
                                    AccretionDiskOuterRadiusInHorizonRadii;
            var oneSideAreaSquareMeters = Math.PI * Math.Max(
                0.0,
                outerRadiusMeters * outerRadiusMeters -
                innerRadiusMeters * innerRadiusMeters);

            // The optically thick disk emits from its top and bottom faces.
            return 2.0 * oneSideAreaSquareMeters *
                   StefanBoltzmannConstant *
                   Math.Pow(temperatureKelvin, 4.0);
        }
    }
}
