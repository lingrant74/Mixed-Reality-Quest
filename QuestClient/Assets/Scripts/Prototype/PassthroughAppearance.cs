using UnityEngine;

/// <summary>
/// Virtual content must write alpha 1, otherwise the Quest compositor shows passthrough
/// through it and the object looks invisible.
/// </summary>
public static class PassthroughAppearance
{
    const string ShaderName = "AssemblyGuide/PassthroughUnlit";

    static Shader _shader;

    static Shader Shader
    {
        get
        {
            if (_shader == null)
                _shader = UnityEngine.Shader.Find(ShaderName);
            if (_shader == null)
                _shader = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit");
            return _shader;
        }
    }

    public static Material Create(Color color)
    {
        if (Shader == null)
            return null;

        var material = new Material(Shader);
        Apply(material, color);
        return material;
    }

    public static void ApplyTo(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        var material = renderer.material;
        if (Shader != null && material.shader != Shader)
            material.shader = Shader;
        Apply(material, color);
    }

    public static void Apply(Material material, Color color)
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
