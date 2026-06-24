namespace SpaceEngine.Runtime.Data
{
    /// <summary>
    /// Keplerian orbit parameters around a parent body.
    /// </summary>
    public struct OrbitData
    {
        /// <summary>
        /// Average orbit radius in meters.
        /// </summary>
        public readonly double SemiMajorAxisMeters;

        /// <summary>
        /// Orbit stretch:
        /// 0 = circle,
        /// closer to 1 = more elongated ellipse.
        /// </summary>
        public readonly double Eccentricity;

        /// <summary>
        /// Orbit tilt relative to the parent body's reference plane, in radians.
        /// </summary>
        public readonly double InclinationRadians;

        /// <summary>
        /// Direction of the orbit ellipse in its plane, in radians.
        /// </summary>
        public readonly double ArgumentOfPeriapsisRadians;

        /// <summary>
        /// Rotation of the orbit plane around the parent body, in radians.
        /// </summary>
        public readonly double LongitudeOfAscendingNodeRadians;

        /// <summary>
        /// Position along the orbit at EpochSeconds, in radians.
        /// </summary>
        public readonly double MeanAnomalyAtEpochRadians;

        /// <summary>
        /// Universe time at which MeanAnomalyAtEpochRadians is valid.
        /// </summary>
        public readonly double EpochSeconds;

        internal OrbitData(
            double semiMajorAxisMeters, 
            double eccentricity, 
            double inclinationRadians, 
            double argumentOfPeriapsisRadians, 
            double longitudeOfAscendingNodeRadians, 
            double meanAnomalyAtEpochRadians, 
            double epochSeconds)
        {
            SemiMajorAxisMeters = semiMajorAxisMeters;
            Eccentricity = eccentricity;
            InclinationRadians = inclinationRadians;
            ArgumentOfPeriapsisRadians = argumentOfPeriapsisRadians;
            LongitudeOfAscendingNodeRadians = longitudeOfAscendingNodeRadians;
            MeanAnomalyAtEpochRadians = meanAnomalyAtEpochRadians;
            EpochSeconds = epochSeconds;
        }
    }
}