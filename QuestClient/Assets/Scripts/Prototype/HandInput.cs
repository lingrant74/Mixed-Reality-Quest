using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

/// <summary>
/// Per-hand pinch state in world space. Prefers XR Hands joint data and falls back to the
/// controller pose with trigger/grip so the prototype still works when the runtime reports
/// hands as controllers (which is what happens over Meta Horizon Link).
/// </summary>
public class HandInput : MonoBehaviour
{
    public sealed class HandState
    {
        /// <summary>True when a usable pose was produced this frame.</summary>
        public bool isTracked;

        /// <summary>True while the pinch (or trigger) is closed.</summary>
        public bool isPinching;

        /// <summary>True on the frame the pinch closes.</summary>
        public bool pinchStarted;

        /// <summary>World-space pinch point: between thumb and index tip, or the controller.</summary>
        public Vector3 position;

        /// <summary>World-space orientation of the hand or controller.</summary>
        public Quaternion rotation = Quaternion.identity;

        /// <summary>True when the pose came from XR Hands rather than a controller.</summary>
        public bool isHandTracked;
    }

    [Header("Pinch thresholds (metres between thumb and index tip)")]
    [SerializeField] float pinchCloseDistance = 0.025f;
    [SerializeField] float pinchOpenDistance = 0.045f;
    [Header("Controller fallback")]
    [SerializeField] float triggerThreshold = 0.55f;

    public HandState Left { get; } = new();
    public HandState Right { get; } = new();

    static HandInput _instance;
    public static HandInput Instance => _instance;

    readonly List<XRHandSubsystem> _subsystems = new();
    XRHandSubsystem _hands;
    bool _leftWasPinching;
    bool _rightWasPinching;
    int _sampledFrame = -1;

    void Awake()
    {
        _instance = this;
    }

    void Update()
    {
        EnsureUpdated();
    }

    /// <summary>
    /// Samples both hands at most once per frame. Consumers call this before reading state so
    /// the result does not depend on script execution order.
    /// </summary>
    public void EnsureUpdated()
    {
        if (_sampledFrame == Time.frameCount)
            return;
        _sampledFrame = Time.frameCount;

        RefreshSubsystem();
        UpdateHand(Left, Handedness.Left, XRNode.LeftHand, ref _leftWasPinching);
        UpdateHand(Right, Handedness.Right, XRNode.RightHand, ref _rightWasPinching);
    }

    void UpdateHand(HandState state, Handedness handedness, XRNode node, ref bool wasPinching)
    {
        state.isTracked = false;
        state.isHandTracked = false;
        var pinching = false;

        if (_hands != null)
        {
            var hand = handedness == Handedness.Left ? _hands.leftHand : _hands.rightHand;
            if (hand.isTracked &&
                hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index) &&
                hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb))
            {
                var midpoint = Vector3.Lerp(index.position, thumb.position, 0.5f);
                state.position = XrSpace.ToWorld(midpoint);
                state.rotation = XrSpace.ToWorld(index.rotation);
                state.isTracked = true;
                state.isHandTracked = true;

                // Hysteresis so a pinch held near the threshold does not chatter.
                var span = Vector3.Distance(index.position, thumb.position);
                pinching = wasPinching ? span <= pinchOpenDistance : span <= pinchCloseDistance;
            }
        }

        if (!state.isTracked)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.devicePosition, out var devicePosition))
            {
                state.position = XrSpace.ToWorld(devicePosition);
                if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out var deviceRotation))
                    state.rotation = XrSpace.ToWorld(deviceRotation);
                state.isTracked = true;

                if (device.TryGetFeatureValue(CommonUsages.trigger, out var trigger))
                    pinching = trigger >= triggerThreshold;
                if (!pinching && device.TryGetFeatureValue(CommonUsages.grip, out var grip))
                    pinching = grip >= triggerThreshold;
            }
        }

        state.isPinching = state.isTracked && pinching;
        state.pinchStarted = state.isPinching && !wasPinching;
        wasPinching = state.isPinching;
    }

    void RefreshSubsystem()
    {
        if (_hands != null && _hands.running)
            return;

        _subsystems.Clear();
        SubsystemManager.GetSubsystems(_subsystems);
        _hands = null;
        for (var i = 0; i < _subsystems.Count; i++)
        {
            if (_subsystems[i] != null && _subsystems[i].running)
            {
                _hands = _subsystems[i];
                break;
            }
        }
    }
}
