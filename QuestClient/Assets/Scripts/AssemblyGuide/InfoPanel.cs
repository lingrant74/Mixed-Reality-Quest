using TMPro;
using UnityEngine;

/// <summary>
/// A world-space text panel that faces the user. Used for the current instruction text, and
/// intended to also carry backend guidance once /assist is wired up.
/// </summary>
public class InfoPanel : MonoBehaviour
{
    TextMeshPro _body;
    TextMeshPro _heading;
    Material _material;
    Vector2 _size;

    public static InfoPanel Create(string name, Transform parent, Vector2 size, Color background)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.transform.SetParent(parent, false);

        var panel = root.AddComponent<InfoPanel>();
        panel._size = size;
        panel.Build(size, background);

        root.SetActive(true);
        return panel;
    }

    void Build(Vector2 size, Color background)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Background";
        quad.transform.SetParent(transform, false);
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);

        var collider = quad.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        _material = HologramMaterials.CreatePanel(background);
        var renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        _heading = MakeText("Heading", new Vector3(0f, size.y * 0.5f - 0.016f, -0.002f),
            new Vector2(size.x * 0.88f, 0.02f), 0.105f, new Color(0.55f, 0.85f, 1f));
        _heading.alignment = TextAlignmentOptions.Top;

        _body = MakeText("Body", new Vector3(0f, -0.014f, -0.002f),
            new Vector2(size.x * 0.88f, size.y - 0.045f), 0.098f, Color.white);
        _body.alignment = TextAlignmentOptions.TopLeft;
    }

    TextMeshPro MakeText(string name, Vector3 localPosition, Vector2 rect, float fontSize, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;

        var text = go.AddComponent<TextMeshPro>();
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.fontSize = fontSize;
        text.color = color;
        text.rectTransform.sizeDelta = rect;
        return text;
    }

    public void SetText(string heading, string body)
    {
        if (_heading != null)
            _heading.text = heading ?? string.Empty;
        if (_body != null)
            _body.text = body ?? string.Empty;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        var forward = transform.position - camera.transform.position;
        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }
}
