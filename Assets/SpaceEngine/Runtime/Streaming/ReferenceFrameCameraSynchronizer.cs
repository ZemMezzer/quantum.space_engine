using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Keeps one reference-frame camera at the virtual origin while copying
    /// the player camera projection and rotation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReferenceFrameCameraSynchronizer : MonoBehaviour
    {
        [SerializeField, HideInInspector] private Camera sourceCamera;
        [SerializeField, HideInInspector] private Camera referenceFrameCamera;
        [SerializeField, HideInInspector] private LayerMask frameLayer;
        [SerializeField, HideInInspector] private CameraClearFlags clearFlags =
            CameraClearFlags.Depth;
        [SerializeField, HideInInspector] private float farClipPlane = 5_000f;

        private void Awake()
        {
            referenceFrameCamera ??= GetComponent<Camera>();

            if (sourceCamera == null)
                sourceCamera = Camera.main;
        }

        private void LateUpdate()
        {
            SynchronizeNow();
        }

        internal void Configure(
            Camera source,
            Camera reference,
            LayerMask layer,
            CameraClearFlags requestedClearFlags,
            float requestedFarClipPlane)
        {
            sourceCamera = source;
            referenceFrameCamera = reference;
            frameLayer = layer;
            clearFlags = requestedClearFlags;
            farClipPlane = Mathf.Max(0.01f, requestedFarClipPlane);

            SynchronizeNow();
        }

        public void SynchronizeNow()
        {
            if (sourceCamera == null || referenceFrameCamera == null)
                return;

            var targetTransform = referenceFrameCamera.transform;
            targetTransform.SetPositionAndRotation(
                Vector3.zero,
                sourceCamera.transform.rotation);

            referenceFrameCamera.orthographic =
                sourceCamera.orthographic;

            referenceFrameCamera.fieldOfView =
                sourceCamera.fieldOfView;

            referenceFrameCamera.orthographicSize =
                sourceCamera.orthographicSize;

            referenceFrameCamera.nearClipPlane = 0.001f;
            referenceFrameCamera.farClipPlane = farClipPlane;
            referenceFrameCamera.aspect = sourceCamera.aspect;
            referenceFrameCamera.clearFlags = clearFlags;
            referenceFrameCamera.cullingMask = frameLayer.value;
        }
    }
}
