namespace SpaceEngine.Runtime.Data.SolarSystem
{
        /// <summary>
        /// Physical and orbital data of one star in a stellar system.
        /// </summary>
        public struct StarData
        {
            /// <summary>
            /// Classification of this star or compact object.
            /// </summary>
            public readonly StarType Type;

            /// <summary>
            /// Mass in kilograms.
            /// </summary>
            public readonly double MassKg;

            /// <summary>
            /// Radius in meters.
            /// </summary>
            public readonly double RadiusMeters;

            /// <summary>
            /// Effective surface temperature in Kelvin.
            /// </summary>
            public readonly double TemperatureKelvin;

            /// <summary>
            /// Luminosity in watts.
            /// </summary>
            public readonly double LuminosityWatts;

            /// <summary>
            /// Whether this black hole has a visible accretion disk. It stays
            /// false for ordinary stars and inactive black holes.
            /// </summary>
            public readonly bool HasAccretionDisk;

            /// <summary>
            /// Age in years.
            /// </summary>
            public readonly double AgeYears;

            /// <summary>
            /// Metallicity relative to the system baseline.
            /// </summary>
            public readonly double Metallicity;

            /// <summary>
            /// Rotation period in seconds.
            /// </summary>
            public readonly double RotationPeriodSeconds;

            /// <summary>
            /// Position/orbit around the system barycenter.
            /// For a single star this remains zero/default.
            /// </summary>
            public readonly OrbitData BarycentricOrbit;

            internal StarData(
                StarType type, 
                double massKg, 
                double radiusMeters, 
                double temperatureKelvin,
                double luminosityWatts,
                bool hasAccretionDisk,
                double ageYears,
                double metallicity,
                double rotationPeriodSeconds, 
                OrbitData barycentricOrbit)
            {
                Type = type;
                MassKg = massKg;
                RadiusMeters = radiusMeters;
                TemperatureKelvin = temperatureKelvin;
                LuminosityWatts = luminosityWatts;
                HasAccretionDisk = hasAccretionDisk;
                AgeYears = ageYears;
                Metallicity = metallicity;
                RotationPeriodSeconds = rotationPeriodSeconds;
                BarycentricOrbit = barycentricOrbit;
            }
        }
}
