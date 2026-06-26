namespace SpaceEngine.Runtime.Data.SolarSystem.Objects
{
    /// <summary>
    /// Minimal reusable data for a mesh-only object. Custom content can define
    /// a richer derived data type without changing any common engine contract.
    /// </summary>
    public sealed class GenericMeshObjectData : StellarObjectData
    {
        public GenericMeshObjectData(
            double massKg,
            double radiusMeters,
            double luminosityWatts,
            OrbitData orbit)
            : base(massKg, radiusMeters, luminosityWatts, orbit)
        {
        }

        public override StellarObjectData WithOrbit(in OrbitData orbit)
        {
            return PreserveEntity(new GenericMeshObjectData(
                MassKg,
                RadiusMeters,
                LuminosityWatts,
                orbit));
        }
    }
}
