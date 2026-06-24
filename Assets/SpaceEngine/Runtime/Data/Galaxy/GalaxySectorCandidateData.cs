namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// One deterministic candidate slot in a galaxy sector.
    /// It is internal generation data: streaming checks IsPresent before
    /// exposing the contained system location.
    /// </summary>
    public readonly struct GalaxySectorCandidateData
    {
        public readonly bool IsPresent;
        public readonly SolarSystemLocationData Location;

        internal GalaxySectorCandidateData(
            bool isPresent,
            SolarSystemLocationData location)
        {
            IsPresent = isPresent;
            Location = location;
        }
    }
}
