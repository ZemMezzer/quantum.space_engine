using System;
using SpaceEngine.Runtime.Core;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Examples
{
    /// <summary>
    /// Minimal 3D flight sample.
    /// It owns one CelestialAnchor, while SpaceEngine owns only the shared
    /// physics and renderer implementations.
    /// </summary>
    [RequireComponent(typeof(Runtime.Core.SpaceEngine))]
    public sealed class SeamlessSpaceFlightBootstrap : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float mouseSensitivity = 2.0f;
        [SerializeField] private bool invertMouseY;

        [Header("Flight")]
        [SerializeField] private double speedMetersPerSecond = 1.0e16;
        [SerializeField] private double minimumSpeedMetersPerSecond = 1.0;
        [SerializeField] private double maximumSpeedMetersPerSecond = 1.0e18;
        [SerializeField] private double mouseWheelSpeedMultiplier = 10.0;

        [SerializeField] private long universeID = 0;
        [SerializeField] private long galaxyID = 0;
        [SerializeField] private long systemID = 0;

        private CelestialAnchor _anchor;
        private float _yaw;
        private float _pitch;
        private bool _isRotatingCamera;

        private void Start()
        {
            cameraTransform ??= Camera.main?.transform;

            if (cameraTransform == null)
            {
                Debug.LogError(
                    "SeamlessSpaceFlightBootstrap requires a camera Transform.",
                    this);

                enabled = false;
                return;
            }

            speedMetersPerSecond = ClampSpeed(speedMetersPerSecond);

            _anchor = GetComponent<Runtime.Core.SpaceEngine>().CreateAnchor();
            _anchor.Move(
                new CoordinatesData(universeID, galaxyID, systemID),
                new double3(0.0, 0.0, 0.0));
            _anchor.Activate();
            _anchor.Refresh();

            var initialEuler = cameraTransform.rotation.eulerAngles;
            _yaw = initialEuler.y;
            _pitch = NormalizeAngle(initialEuler.x);
        }

        [ContextMenu("Move")]
        private void MoveTo()
        {
            _anchor.Move(
                new CoordinatesData(universeID, galaxyID, systemID),
                new double3(0.0, 0.0, 0.0));
        }

        private void OnValidate()
        {
            minimumSpeedMetersPerSecond = Math.Max(
                0.000001,
                minimumSpeedMetersPerSecond);

            maximumSpeedMetersPerSecond = Math.Max(
                minimumSpeedMetersPerSecond,
                maximumSpeedMetersPerSecond);

            mouseWheelSpeedMultiplier = Math.Max(
                1.01,
                mouseWheelSpeedMultiplier);

            speedMetersPerSecond = ClampSpeed(speedMetersPerSecond);
        }

        private void Update()
        {
            if (_anchor == null || _anchor.IsDisposed ||
                cameraTransform == null)
            {
                return;
            }

            UpdateSpeedFromMouseWheel();
            UpdateCameraRotation();
            UpdateMovement();
        }

        private void OnDestroy()
        {
            if (_isRotatingCamera)
                UnlockCursor();

            _anchor?.Dispose();
        }

        private void UpdateSpeedFromMouseWheel()
        {
            var scroll = Input.mouseScrollDelta.y;

            if (Mathf.Approximately(scroll, 0.0f))
                return;

            speedMetersPerSecond = ClampSpeed(
                speedMetersPerSecond * Math.Pow(
                    mouseWheelSpeedMultiplier,
                    scroll));
        }

        private void UpdateCameraRotation()
        {
            if (Input.GetMouseButtonDown(1))
            {
                _isRotatingCamera = true;
                LockCursor();
            }

            if (Input.GetMouseButtonUp(1))
            {
                _isRotatingCamera = false;
                UnlockCursor();
            }

            if (!_isRotatingCamera)
                return;

            _yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;

            var verticalDirection = invertMouseY ? 1.0f : -1.0f;
            _pitch += Input.GetAxisRaw("Mouse Y") * mouseSensitivity *
                      verticalDirection;

            _pitch = Mathf.Clamp(_pitch, -89.0f, 89.0f);
            cameraTransform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
        }

        private void UpdateMovement()
        {
            var verticalInput =
                (Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f) -
                (Input.GetKey(KeyCode.LeftControl) ? 1.0f : 0.0f);

            var direction =
                cameraTransform.forward * Input.GetAxisRaw("Vertical") +
                cameraTransform.right * Input.GetAxisRaw("Horizontal") +
                cameraTransform.up * verticalInput;

            if (direction.sqrMagnitude <= 0.000001f)
                return;

            _anchor.Move(
                direction.normalized,
                speedMetersPerSecond * Time.deltaTime);
        }

        private double ClampSpeed(double speed)
        {
            return Math.Max(
                minimumSpeedMetersPerSecond,
                Math.Min(maximumSpeedMetersPerSecond, speed));
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180.0f ? angle - 360.0f : angle;
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
