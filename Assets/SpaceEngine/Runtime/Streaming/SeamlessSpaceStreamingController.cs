using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Activates the scaled solar-system representation around a real nearby
    /// stellar-system point. The point and the solar LOD share the same
    /// SolarSystemLocationData, so the visual handoff does not change place.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SeamlessSpaceAnchor))]
    [RequireComponent(typeof(StellarFieldRenderer))]
    [RequireComponent(typeof(SolarSystemScaledSpaceRenderer))]
    public sealed class SeamlessSpaceStreamingController : MonoBehaviour
    {
        [SerializeField, HideInInspector] private SeamlessSpaceAnchor spaceAnchor;
        [SerializeField, HideInInspector] private StellarFieldRenderer stellarFieldRenderer;
        [SerializeField, HideInInspector]
        private SolarSystemScaledSpaceRenderer solarSystemRenderer;

        [SerializeField, HideInInspector, Min(0.001f)]
        private float proximityCheckIntervalSeconds = 0.25f;
        [SerializeField, HideInInspector, Min(0)]
        private int nearestSystemSectorSearchRadius = 1;
        [SerializeField, HideInInspector, Min(0.000001f)]
        private double solarSystemActivationDistanceLightYears = 0.02;
        [SerializeField, HideInInspector, Min(0.000001f)]
        private double solarSystemDeactivationDistanceLightYears = 0.03;
        [SerializeField, HideInInspector, Min(0.001f)]
        private float stellarPointHideAfterLod1AngularDiameterDegrees =
            0.35f;

        private bool _solarSystemLodActive;
        private float _nextProximityCheckTime;

        public event Action<CoordinatesData> SolarSystemLodEntered;
        public event Action<CoordinatesData> SolarSystemLodExited;

        public bool IsSolarSystemLodActive => _solarSystemLodActive;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            StellarFieldRenderer renderer,
            SolarSystemScaledSpaceRenderer solarRenderer,
            double activationDistanceLightYears,
            double deactivationDistanceLightYears,
            float stellarPointHideAngularDiameterDegrees)
        {
            spaceAnchor = anchor;
            stellarFieldRenderer = renderer;
            solarSystemRenderer = solarRenderer;
            solarSystemActivationDistanceLightYears = Math.Max(
                0.000001,
                activationDistanceLightYears);
            solarSystemDeactivationDistanceLightYears = Math.Max(
                solarSystemActivationDistanceLightYears,
                deactivationDistanceLightYears);
            stellarPointHideAfterLod1AngularDiameterDegrees = Mathf.Max(
                0.001f,
                stellarPointHideAngularDiameterDegrees);
        }

        private void Awake()
        {
            spaceAnchor ??= GetComponent<SeamlessSpaceAnchor>();
            stellarFieldRenderer ??= GetComponent<StellarFieldRenderer>();
            solarSystemRenderer ??= GetComponent<SolarSystemScaledSpaceRenderer>();
        }

        private void OnEnable()
        {
            if (solarSystemRenderer != null)
                solarSystemRenderer.SetScaledSpaceVisible(false);
        }

        private void OnDisable()
        {
            if (solarSystemRenderer != null)
                solarSystemRenderer.SetScaledSpaceVisible(false);

            SetStellarPointSuppression(false);
            _solarSystemLodActive = false;
        }

        private void OnValidate()
        {
            proximityCheckIntervalSeconds = Mathf.Max(
                0.001f,
                proximityCheckIntervalSeconds);

            nearestSystemSectorSearchRadius = Mathf.Max(
                0,
                nearestSystemSectorSearchRadius);

            solarSystemActivationDistanceLightYears = Math.Max(
                0.000001,
                solarSystemActivationDistanceLightYears);

            solarSystemDeactivationDistanceLightYears = Math.Max(
                solarSystemActivationDistanceLightYears,
                solarSystemDeactivationDistanceLightYears);

            stellarPointHideAfterLod1AngularDiameterDegrees = Mathf.Max(
                0.001f,
                stellarPointHideAfterLod1AngularDiameterDegrees);
        }

        private void Update()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            if (Time.unscaledTime < _nextProximityCheckTime)
                return;

            _nextProximityCheckTime = Time.unscaledTime +
                                      proximityCheckIntervalSeconds;
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            var hasNearest = SolarSystemProximityResolver.TryFindNearest(
                spaceAnchor.Galaxy,
                spaceAnchor.GalaxyLocalPositionLightYears,
                nearestSystemSectorSearchRadius,
                out var nearestSolarSystem,
                out var nearestDistanceMeters);

            var activationDistanceMeters =
                solarSystemActivationDistanceLightYears *
                SeamlessSpaceAnchor.MetersPerLightYear;

            var deactivationDistanceMeters =
                solarSystemDeactivationDistanceLightYears *
                SeamlessSpaceAnchor.MetersPerLightYear;

            if (!_solarSystemLodActive)
            {
                if (hasNearest &&
                    nearestDistanceMeters <= activationDistanceMeters)
                {
                    ActivateSolarSystemLod(nearestSolarSystem);
                }

                return;
            }

            if (hasNearest &&
                nearestSolarSystem.SolarSystemID !=
                spaceAnchor.Coordinates.SolarSystemID &&
                nearestDistanceMeters <= activationDistanceMeters)
            {
                ActivateSolarSystemLod(nearestSolarSystem);
                return;
            }

            if (spaceAnchor.GetDistanceToActiveSolarSystemMeters() >
                deactivationDistanceMeters)
            {
                DeactivateSolarSystemLod();
                return;
            }

            UpdateStellarPointSuppression();
        }

        private void ActivateSolarSystemLod(
            in SolarSystemLocationData solarSystem)
        {
            if (spaceAnchor.Coordinates.SolarSystemID !=
                solarSystem.SolarSystemID)
            {
                spaceAnchor.RebaseToSolarSystem(solarSystem.SolarSystemID);
            }

            if (solarSystemRenderer != null)
            {
                solarSystemRenderer.SetScaledSpaceVisible(true);
                solarSystemRenderer.RefreshNow();
            }

            // Keep LOD 0 visible until the scaled star sphere has reached
            // its configured apparent size. This provides an overlap instead
            // of the former one-frame / tiny-star gap.
            SetStellarPointSuppression(false);

            if (_solarSystemLodActive)
            {
                UpdateStellarPointSuppression();
                return;
            }

            _solarSystemLodActive = true;
            UpdateStellarPointSuppression();
            SolarSystemLodEntered?.Invoke(spaceAnchor.Coordinates);
        }

        private void DeactivateSolarSystemLod()
        {
            if (solarSystemRenderer != null)
                solarSystemRenderer.SetScaledSpaceVisible(false);

            SetStellarPointSuppression(false);

            if (!_solarSystemLodActive)
                return;

            _solarSystemLodActive = false;
            SolarSystemLodExited?.Invoke(spaceAnchor.Coordinates);
        }

        private void UpdateStellarPointSuppression()
        {
            var canHideStellarPoint =
                solarSystemRenderer != null &&
                solarSystemRenderer.IsNearestStarLod1VisibleAt(
                    stellarPointHideAfterLod1AngularDiameterDegrees);

            SetStellarPointSuppression(canHideStellarPoint);
        }

        private void SetStellarPointSuppression(bool suppress)
        {
            if (stellarFieldRenderer != null)
            {
                stellarFieldRenderer.SetAnchorSolarSystemPointSuppressed(
                    suppress);
            }
        }
    }
}
