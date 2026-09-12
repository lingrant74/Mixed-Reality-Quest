using UnityEngine;

/// <summary>
/// Small marker at each hand so the user can see what the app is tracking while placing the
/// hologram. Once the hologram is anchored the markers are no longer needed, so they hide
/// until a hand comes back within reach of it.
/// </summary>
public class HandPinchVisuals : MonoBehaviour
{
    [SerializeField] float radius = 0.012f;
    [SerializeField] Color trackedColor = new Color(0.2f, 0.95f, 1f, 1f);
    [SerializeField] Color pinchingColor = new Color(0.15f, 0.9f, 0.45f, 1f);

    Transform _left;
    Transform _right;
    Renderer _leftRenderer;
    Renderer _rightRenderer;
    Material _material;
    Material _pinchMaterial;

    void Start()
    {
        _material = PassthroughAppearance.Create(trackedColor);
        _pinchMaterial = PassthroughAppearance.Create(pinchingColor);
        _left = CreateMarker("LeftHandMarker", out _leftRenderer);
        _right = CreateMarker("RightHandMarker", out _rightRenderer);
    }

    Transform CreateMarker(string markerName, out Renderer markerRenderer)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = markerName;
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * (radius * 2f);

        var collider = marker.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        markerRenderer = marker.GetComponent<Renderer>();
        if (_material != null)
            markerRenderer.sharedMaterial = _material;

        marker.SetActive(false);
        return marker.transform;
    }

    void Update()
    {
        var hands = HandInput.Instance;
        if (hands == null)
            return;

        hands.EnsureUpdated();
        var show = ShouldShowMarkers();
        UpdateMarker(_left, _leftRenderer, hands.Left, show);
        UpdateMarker(_right, _rightRenderer, hands.Right, show);
    }

    bool ShouldShowMarkers()
    {
        var hologram = HologramController.Instance;
        if (hologram == null)
            return true;
        return !hologram.IsAnchored || hologram.IsHandNearby;
    }

    void UpdateMarker(Transform marker, Renderer markerRenderer, HandInput.HandState hand, bool show)
    {
        if (marker == null)
            return;

        var visible = show && hand.isTracked;
        if (marker.gameObject.activeSelf != visible)
            marker.gameObject.SetActive(visible);

        if (!visible)
            return;

        marker.position = hand.position;

        var wanted = hand.isPinching ? _pinchMaterial : _material;
        if (wanted != null && markerRenderer.sharedMaterial != wanted)
            markerRenderer.sharedMaterial = wanted;
    }
}
