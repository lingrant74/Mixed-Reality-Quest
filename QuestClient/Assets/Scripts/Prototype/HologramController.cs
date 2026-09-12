using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Owns the hologram end to end: places it once when tracking settles, lets the user pinch
/// and drag it, and anchors it to the room on release. This is the only script that writes
/// the hologram's transform.
/// </summary>
public class HologramController : MonoBehaviour
{
    enum State
    {
        WaitingForTracking,
        Idle,
        Grabbed,
        Anchored,
    }

    [Header("Initial placement")]
    [Tooltip("Distance in front of the user where the hologram first appears.")]
    [SerializeField] float placementDistance = 0.6f;
    [Tooltip("Height below eye level where the hologram first appears.")]
    [SerializeField] float placementDrop = 0.15f;
    [Tooltip("Frames to wait for head tracking to produce a real pose before placing.")]
    [SerializeField] int trackingSettleFrames = 30;

    [Header("Grabbing")]
    [Tooltip("How close a pinching hand must be to the hologram centre to grab it.")]
    [SerializeField] float grabRadius = 0.25f;

    [Header("Colours")]
    [SerializeField] Color idleColor = new Color(0.22f, 0.55f, 1f, 1f);
    [SerializeField] Color hoverColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField] Color grabbedColor = new Color(0.15f, 0.9f, 0.45f, 1f);
    [SerializeField] Color anchoredColor = new Color(0.6f, 0.4f, 1f, 1f);

    public static HologramController Instance { get; private set; }

    /// <summary>True once the hologram has been committed to a spot in the room.</summary>
    public bool IsAnchored => _state == State.Anchored;

    /// <summary>True when a tracked hand is close enough to interact.</summary>
    public bool IsHandNearby { get; private set; }

    State _state = State.WaitingForTracking;
    Renderer _renderer;
    Color _appliedColor;
    int _frameCount;

    HandInput.HandState _heldBy;
    Vector3 _grabOffset;
    Quaternion _grabRotation;

    ARAnchorManager _anchorManager;
    ARAnchor _anchor;
    bool _anchorRequestInFlight;
    bool _anchorUnsupportedLogged;

    void Awake()
    {
        Instance = this;
        _renderer = GetComponent<Renderer>();
    }

    void Update()
    {
        var hands = HandInput.Instance;

        if (_state == State.WaitingForTracking)
        {
            TryInitialPlacement();
            return;
        }

        if (hands == null)
            return;

        hands.EnsureUpdated();
        var left = hands.Left;
        var right = hands.Right;
        IsHandNearby = IsWithinReach(left) || IsWithinReach(right);

        if (_state == State.Grabbed)
        {
            UpdateGrab();
            return;
        }

        if (TryStartGrab(left) || TryStartGrab(right))
            return;

        ApplyColor(_state == State.Anchored
            ? anchoredColor
            : IsHandNearby ? hoverColor : idleColor);
    }

    // --- placement -------------------------------------------------------

    void TryInitialPlacement()
    {
        _frameCount++;

        var cam = XrSpace.Camera;
        if (cam == null)
            return;

        // Head tracking reports the origin until the first real pose arrives. Wait for a
        // non-zero pose, but place anyway after a grace period so a stationary headset
        // (or the editor with no device) still shows the hologram.
        var hasPose = cam.transform.position.sqrMagnitude > 0.0001f;
        if (!hasPose && _frameCount < trackingSettleFrames * 4)
            return;
        if (_frameCount < trackingSettleFrames)
            return;

        var forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        transform.position = cam.transform.position + forward * placementDistance + Vector3.down * placementDrop;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        _state = State.Idle;
        ApplyColor(idleColor);
    }

    // --- grabbing --------------------------------------------------------

    bool IsWithinReach(HandInput.HandState hand)
    {
        return hand.isTracked && Vector3.Distance(hand.position, transform.position) <= grabRadius;
    }

    bool TryStartGrab(HandInput.HandState hand)
    {
        if (!hand.pinchStarted || !IsWithinReach(hand))
            return false;

        DetachAnchor();

        _heldBy = hand;
        _grabOffset = Quaternion.Inverse(hand.rotation) * (transform.position - hand.position);
        _grabRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
        _state = State.Grabbed;
        ApplyColor(grabbedColor);
        return true;
    }

    void UpdateGrab()
    {
        if (_heldBy == null || !_heldBy.isPinching || !_heldBy.isTracked)
        {
            _heldBy = null;
            _state = State.Idle;
            CommitAnchor();
            return;
        }

        transform.position = _heldBy.position + _heldBy.rotation * _grabOffset;
        transform.rotation = _heldBy.rotation * _grabRotation;
        ApplyColor(grabbedColor);
    }

    // --- anchoring -------------------------------------------------------

    void CommitAnchor()
    {
        if (_anchorRequestInFlight)
            return;

        var manager = EnsureAnchorManager();
        if (manager == null || manager.subsystem == null)
        {
            // No anchor subsystem (common over Meta Horizon Link). The XR Origin does not
            // move during a session, so the current world pose is already stable.
            if (!_anchorUnsupportedLogged)
            {
                Debug.Log("[Hologram] Anchor subsystem unavailable; holding world pose instead.");
                _anchorUnsupportedLogged = true;
            }
            _state = State.Anchored;
            ApplyColor(anchoredColor);
            return;
        }

        CreateAnchor(manager, new Pose(transform.position, transform.rotation));
    }

    async void CreateAnchor(ARAnchorManager manager, Pose pose)
    {
        _anchorRequestInFlight = true;
        try
        {
            var result = await manager.TryAddAnchorAsync(pose);
            if (result.status.IsSuccess() && result.value != null)
            {
                _anchor = result.value;
                // Parenting to the anchor lets the runtime correct the pose as its map of
                // the room improves.
                transform.SetParent(_anchor.transform, true);
            }
            else
            {
                Debug.LogWarning($"[Hologram] Anchor creation failed ({result.status}); holding world pose.");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[Hologram] Anchor creation threw; holding world pose. {exception.Message}");
        }
        finally
        {
            _anchorRequestInFlight = false;
            if (_state != State.Grabbed)
            {
                _state = State.Anchored;
                ApplyColor(anchoredColor);
            }
        }
    }

    void DetachAnchor()
    {
        if (transform.parent != null)
            transform.SetParent(null, true);

        if (_anchor == null)
            return;

        var manager = _anchorManager;
        if (manager != null && manager.subsystem != null)
            manager.TryRemoveAnchor(_anchor);
        _anchor = null;
    }

    ARAnchorManager EnsureAnchorManager()
    {
        if (_anchorManager != null)
            return _anchorManager;

        var origin = XrSpace.Origin;
        if (origin == null)
            return null;

        // ARAnchorManager requires an XROrigin on the same GameObject.
        _anchorManager = origin.GetComponent<ARAnchorManager>();
        if (_anchorManager == null)
            _anchorManager = origin.gameObject.AddComponent<ARAnchorManager>();
        return _anchorManager;
    }

    // --- appearance ------------------------------------------------------

    void ApplyColor(Color color)
    {
        if (_renderer == null || _appliedColor == color)
            return;

        PassthroughAppearance.ApplyTo(_renderer, color);
        _appliedColor = color;
    }
}
