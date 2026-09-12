using TMPro;
using UnityEngine;

/// <summary>
/// Head-locked progress readout pinned to the top-right of the view. Built from 3D quads and
/// 3D TextMeshPro rather than a Canvas, so it needs no EventSystem and composites cleanly
/// over passthrough.
/// </summary>
public class ProgressHud : MonoBehaviour
{
    const float BarWidth = 0.24f;
    const float BarHeight = 0.016f;
    const float CaptionWidth = 0.26f;

    // Placed about 22 degrees right and 13 degrees up from centre at 0.9 m: far enough to
    // read as a corner HUD, close enough to stay inside the comfortable field of view.
    static readonly Vector3 ViewOffset = new Vector3(0.24f, 0.21f, 0.90f);

    Transform _fill;
    TextMeshPro _caption;
    Material _fillMaterial;

    public static ProgressHud Create()
    {
        var root = new GameObject("Assembly Progress HUD");
        var hud = root.AddComponent<ProgressHud>();
        hud.Build();
        return hud;
    }

    void Build()
    {
        var track = Panel("Track", new Color(0.04f, 0.06f, 0.10f), BarWidth, BarHeight, 0f);
        track.localPosition = Vector3.zero;

        _fill = Panel("Fill", new Color(0.2f, 0.85f, 1f), BarWidth, BarHeight, -0.001f);
        _fillMaterial = _fill.GetComponent<Renderer>().sharedMaterial;

        var caption = new GameObject("Caption");
        caption.transform.SetParent(transform, false);
        caption.transform.localPosition = new Vector3(0f, BarHeight * 0.5f + 0.018f, -0.001f);
        _caption = caption.AddComponent<TextMeshPro>();
        _caption.alignment = TextAlignmentOptions.Center;
        _caption.enableAutoSizing = false;
        _caption.textWrappingMode = TextWrappingModes.NoWrap;
        _caption.color = Color.white;
        _caption.rectTransform.sizeDelta = new Vector2(CaptionWidth, 0.05f);

        SetProgress(0f, "Starting up");
    }

    Transform Panel(string name, Color color, float width, float height, float z)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, z);
        quad.transform.localScale = new Vector3(width, height, 1f);

        var collider = quad.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = HologramMaterials.CreatePanel(color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return quad.transform;
    }

    public void SetProgress(float normalized, string caption)
    {
        normalized = Mathf.Clamp01(normalized);

        if (_fill != null)
        {
            // Grow from the left edge instead of from the quad's centre.
            _fill.localScale = new Vector3(BarWidth * normalized, BarHeight, 1f);
            _fill.localPosition = new Vector3(-BarWidth * 0.5f + BarWidth * normalized * 0.5f, 0f, -0.001f);
        }

        if (_fillMaterial != null)
        {
            var color = normalized >= 1f
                ? new Color(0.15f, 0.95f, 0.42f)
                : new Color(0.2f, 0.85f, 1f);
            HologramMaterials.SetPanelColor(_fillMaterial, color);
        }

        if (_caption == null || _caption.text == caption)
            return;

        _caption.text = caption;
        FitCaption();
    }

    /// <summary>
    /// TMP auto-sizing does not clamp reliably at these scales, so measure at a known font
    /// size and scale down until the line fits the HUD width.
    /// </summary>
    void FitCaption()
    {
        _caption.fontSize = 1f;
        _caption.ForceMeshUpdate(true, true);

        var bounds = _caption.textBounds.size;
        if (bounds.x <= 0.0001f)
            return;

        _caption.fontSize = Mathf.Min(0.15f, CaptionWidth / bounds.x);
        _caption.ForceMeshUpdate(true, true);
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        var head = camera.transform;
        transform.position = head.TransformPoint(ViewOffset);
        transform.rotation = Quaternion.LookRotation(transform.position - head.position, head.up);
    }

    void OnDestroy()
    {
        if (_fillMaterial != null)
            Destroy(_fillMaterial);
    }
}
