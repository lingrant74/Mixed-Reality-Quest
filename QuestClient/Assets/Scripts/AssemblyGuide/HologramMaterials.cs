using UnityEngine;

/// <summary>
/// Shared materials for the guide. Hologram parts use premultiplied-alpha transparency so
/// the room shows through them; panels use the opaque unlit shader so they stay readable
/// against passthrough.
/// </summary>
public static class HologramMaterials
{
    const string GhostShader = "AssemblyGuide/HologramTransparent";
    const string PanelShader = "AssemblyGuide/PassthroughUnlit";

    static Material _pending;
    static Material _done;
    static Material _stepComplete;

    /// <summary>Faint blue ghost: the part has not been placed yet.</summary>
    public static Material Pending
    {
        get
        {
            if (_pending == null)
                _pending = Ghost(new Color(0.30f, 0.68f, 1f, 0.10f), new Color(0.55f, 0.85f, 1f), 0.45f);
            return _pending;
        }
    }

    /// <summary>Orange: the user marked this individual part as placed.</summary>
    public static Material Done
    {
        get
        {
            if (_done == null)
                _done = Ghost(new Color(1f, 0.48f, 0.05f, 0.34f), new Color(1f, 0.75f, 0.35f), 0.8f);
            return _done;
        }
    }

    /// <summary>Green: every part in this instruction is placed.</summary>
    public static Material StepComplete
    {
        get
        {
            if (_stepComplete == null)
                _stepComplete = Ghost(new Color(0.15f, 0.95f, 0.42f, 0.34f), new Color(0.6f, 1f, 0.75f), 0.8f);
            return _stepComplete;
        }
    }

    static Material Ghost(Color baseColor, Color rimColor, float rimStrength)
    {
        var shader = Shader.Find(GhostShader);
        if (shader == null)
            return null;

        var material = new Material(shader);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_RimColor", rimColor);
        material.SetFloat("_RimPower", 2.5f);
        material.SetFloat("_RimStrength", rimStrength);
        return material;
    }

    /// <summary>A fresh opaque panel material; callers own the instance.</summary>
    public static Material CreatePanel(Color color)
    {
        var shader = Shader.Find(PanelShader);
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            return null;

        var material = new Material(shader);
        SetPanelColor(material, color);
        return material;
    }

    public static void SetPanelColor(Material material, Color color)
    {
        if (material == null)
            return;

        color.a = 1f;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }
}
