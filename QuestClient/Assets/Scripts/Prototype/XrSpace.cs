using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// XR Hands joints and XR input devices report poses in the device's tracking origin space.
/// XROrigin applies the tracking-mode height offset to its Camera Offset child (0 in Floor
/// mode, CameraYOffset in Device/Unbounded mode), so that child - not the XROrigin root - is
/// the transform that maps tracking space into Unity world space.
/// </summary>
public static class XrSpace
{
    static XROrigin _origin;

    public static XROrigin Origin
    {
        get
        {
            if (_origin == null)
                _origin = Object.FindAnyObjectByType<XROrigin>();
            return _origin;
        }
    }

    /// <summary>Transform that converts tracking-space poses into world space.</summary>
    public static Transform TrackingSpace
    {
        get
        {
            var origin = Origin;
            if (origin == null)
                return null;
            if (origin.CameraFloorOffsetObject != null)
                return origin.CameraFloorOffsetObject.transform;
            return origin.transform;
        }
    }

    public static Camera Camera
    {
        get
        {
            var origin = Origin;
            if (origin != null && origin.Camera != null)
                return origin.Camera;
            return UnityEngine.Camera.main;
        }
    }

    public static Vector3 ToWorld(Vector3 trackingPosition)
    {
        var space = TrackingSpace;
        return space != null ? space.TransformPoint(trackingPosition) : trackingPosition;
    }

    public static Quaternion ToWorld(Quaternion trackingRotation)
    {
        var space = TrackingSpace;
        return space != null ? space.rotation * trackingRotation : trackingRotation;
    }

    public static Pose ToWorld(Pose trackingPose)
    {
        return new Pose(ToWorld(trackingPose.position), ToWorld(trackingPose.rotation));
    }
}
