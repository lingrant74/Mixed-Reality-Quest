using TMPro;
using UnityEngine;

/// <summary>
/// Progress readout for the current assembly. Built from 3D quads and 3D TextMeshPro rather
/// than a Canvas, so it needs no EventSystem and composites cleanly over passthrough.
/// <para>
/// It is parented to the instruction panel so the two read as one block and share the panel's
/// billboard rotation, rather than drifting apart as the user moves.
/// </para>
/// </summary>
public class ProgressHud : MonoBehaviour
{
    const float BarHeight = 0.012f;

    float _barWidth;
    Transform _fill;
    TextMeshPro _caption;
    Material _fillMaterial;

    public static ProgressHud Create(Transform parent, Vector3 localPosition, float barWidth)
    {
        var root = new GameObject("Assembly Progress HUD");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        var hud = root.AddComponent<ProgressHud>();
        hud._barWidth = barWidth;
        hud.Build();
        return hud;
    }

    void Build()
    {
        var track = Panel("Track", new Color(0.04f, 0.06f, 0.10f), _barWidth, BarHeight, 0f);
        track.localPosition = Vector3.zero;

        _fill = Panel("Fill", new Color(0.2f, 0.85f, 1f), _barWidth, BarHeight, -0.001f);
        _fillMaterial = _fill.GetComponent<Renderer>().sharedMaterial;

        var caption = new GameObject("Caption");
        caption.transform.SetParent(transform, false);
        caption.transform.localPosition = new Vector3(0f, BarHeight * 0.5f + 0.016f, -0.001f);
        _caption = caption.AddComponent<TextMeshPro>();
        _caption.alignment = TextAlignmentOptions.Center;
        _caption.enableAutoSizing = false;
        _caption.textWrappingMode = TextWrappingModes.NoWrap;
        _caption.color = Color.white;
        _caption.rectTransform.sizeDelta = new Vector2(_barWidth, 0.04f);

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
            _fill.localScale = new Vector3(_barWidth * normalized, BarHeight, 1f);
            _fill.localPosition = new Vector3(-_barWidth * 0.5f + _barWidth * normalized * 0.5f, 0f, -0.001f);
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
    /// size and scale down until the line fits the bar width.
    /// </summary>
    void FitCaption()
    {
        _caption.fontSize = 1f;
        _caption.ForceMeshUpdate(true, true);

        var bounds = _caption.textBounds.size;
        if (bounds.x <= 0.0001f)
            return;

        _caption.fontSize = Mathf.Min(0.16f, _barWidth / bounds.x);
        _caption.ForceMeshUpdate(true, true);
    }

    void OnDestroy()
    {
        if (_fillMaterial != null)
            Destroy(_fillMaterial);
    }
}
