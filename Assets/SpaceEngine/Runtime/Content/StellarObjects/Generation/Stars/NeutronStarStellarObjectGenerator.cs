using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    [CreateAssetMenu(
        fileName = "Neutron Star Generator",
        menuName = "Space Engine/Stellar Objects/Stars/Generators/Neutron Star")]
    public sealed class NeutronStarStellarObjectGenerator : CompactStellarObjectGeneratorBase
    {
        protected override float PrimaryWeight => 0.003f;
        protected override double MinimumRotationSeconds => 0.05;
        protected override double MaximumRotationSeconds => 30.0;
        protected override double MinimumTemperatureKelvin => 100_000.0;
        protected override double MaximumTemperatureKelvin => 2_000_000.0;
    }
}
