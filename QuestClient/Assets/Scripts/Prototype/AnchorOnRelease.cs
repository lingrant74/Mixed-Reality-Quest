using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Anchors the hologram to the room when the user lets go of it, and drops the anchor when
/// they grab it again. Meta's Grab Interaction block handles the actual moving; this only
/// commits the resulting pose to an <see cref="OVRSpatialAnchor"/>.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class AnchorOnRelease : MonoBehaviour
{
    PointableElement _pointable;
    OVRSpatialAnchor _anchor;
    int _selectCount;
    bool _locked;

    void Awake()
    {
        _pointable = GetComponent<Grabbable>();
    }

    void OnEnable()
    {
        if (_pointable != null)
            _pointable.WhenPointerEventRaised += HandlePointerEvent;
    }

    void OnDisable()
    {
        if (_pointable != null)
            _pointable.WhenPointerEventRaised -= HandlePointerEvent;
    }

    /// <summary>
    /// Commits the current pose and stops responding to further grabs, for when the user has
    /// decided where the hologram belongs.
    /// </summary>
    public void LockInPlace()
    {
        _locked = true;
        CreateAnchor();
    }

    void HandlePointerEvent(PointerEvent pointerEvent)
    {
        if (_locked)
            return;

        switch (pointerEvent.Type)
        {
            case PointerEventType.Select:
                _selectCount++;
                DropAnchor();
                break;
            case PointerEventType.Unselect:
            case PointerEventType.Cancel:
                _selectCount = Mathf.Max(0, _selectCount - 1);
                if (_selectCount == 0)
                    CreateAnchor();
                break;
        }
    }

    void CreateAnchor()
    {
        if (_anchor != null)
            return;

        // OVRSpatialAnchor creates its anchor at the GameObject's current pose on enable.
        _anchor = gameObject.AddComponent<OVRSpatialAnchor>();
    }

    void DropAnchor()
    {
        if (_anchor == null)
            return;

        Destroy(_anchor);
        _anchor = null;
    }
}
