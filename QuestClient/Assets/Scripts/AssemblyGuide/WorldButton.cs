using System;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;

/// <summary>
/// A world-space button you select by pointing and pinching. Built entirely at runtime out
/// of Meta Interaction SDK parts: a collider surface feeding a RayInteractable, which the
/// hand and controller ray interactors in the rig already know how to drive.
/// </summary>
public class WorldButton : MonoBehaviour
{
    public event Action Clicked;

    Renderer _background;
    Material _material;
    TextMeshPro _label;
    Color _idleColor;
    Color _hoverColor;
    bool _faceCamera;
    bool _clickPending;

    /// <summary>
    /// Creates a button. The root is built inactive so the Interaction SDK components can be
    /// injected with their dependencies before their Awake runs.
    /// </summary>
    public static WorldButton Create(
        string name,
        string label,
        Vector2 size,
        Transform parent,
        Color idleColor,
        Color hoverColor,
        bool faceCamera = true)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.transform.SetParent(parent, false);

        var button = root.AddComponent<WorldButton>();
        button._idleColor = idleColor;
        button._hoverColor = hoverColor;
        button._faceCamera = faceCamera;
        button.Build(label, size);

        root.SetActive(true);
        return button;
    }

    void Build(string label, Vector2 size)
    {
        var collider = gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(size.x, size.y, 0.02f);
        // ColliderSurface raycasts the collider directly, so keeping it a trigger keeps these
        // buttons out of the hologram rigidbody's compound collider.
        collider.isTrigger = true;

        var surface = gameObject.AddComponent<ColliderSurface>();
        surface.InjectAllColliderSurface(collider);

        var interactable = gameObject.AddComponent<RayInteractable>();
        interactable.InjectAllRayInteractable(surface);
        interactable.WhenStateChanged += HandleStateChanged;

        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Panel";
        panel.transform.SetParent(transform, false);
        panel.transform.localScale = new Vector3(size.x, size.y, 1f);
        var panelCollider = panel.GetComponent<Collider>();
        if (panelCollider != null)
            Destroy(panelCollider);

        _background = panel.GetComponent<Renderer>();
        _material = HologramMaterials.CreatePanel(_idleColor);
        if (_material != null)
            _background.sharedMaterial = _material;
        _background.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _background.receiveShadows = false;

        var text = new GameObject("Label");
        text.transform.SetParent(transform, false);
        text.transform.localPosition = new Vector3(0f, 0f, -0.003f);
        _label = text.AddComponent<TextMeshPro>();
        _label.text = label;
        _label.alignment = TextAlignmentOptions.Center;
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 0.05f;
        _label.fontSizeMax = 1.2f;
        _label.color = Color.white;
        _label.rectTransform.sizeDelta = new Vector2(size.x * 0.92f, size.y * 0.8f);
    }

    void Update()
    {
        if (!_clickPending)
            return;

        _clickPending = false;
        Clicked?.Invoke();
    }

    void LateUpdate()
    {
        if (!_faceCamera)
            return;

        var camera = Camera.main;
        if (camera == null)
            return;

        // Quads face -Z, so look away from the camera to present the front to the user.
        var forward = transform.position - camera.transform.position;
        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    void HandleStateChanged(InteractableStateChangeArgs args)
    {
        switch (args.NewState)
        {
            case InteractableState.Select:
                HologramMaterials.SetPanelColor(_material, _hoverColor);
                // Handlers commonly hide this button, which is unsafe to do while the
                // interactable is still dispatching, so raise the event next frame.
                _clickPending = true;
                break;
            case InteractableState.Hover:
                HologramMaterials.SetPanelColor(_material, _hoverColor);
                break;
            default:
                HologramMaterials.SetPanelColor(_material, _idleColor);
                break;
        }
    }

    public void SetLabel(string label)
    {
        if (_label != null)
            _label.text = label;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }
}
