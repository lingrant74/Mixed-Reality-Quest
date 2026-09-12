using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A miniature of an assembly, for the selection menu. The backend holds no thumbnails, so
/// rather than show nothing we build the real thing in miniature from the same layout the
/// full-size hologram uses. Shown at a three-quarter angle so it reads as a solid object.
/// </summary>
public static class AssemblyPreview
{
    static readonly Quaternion ViewAngle = Quaternion.Euler(-18f, 28f, 0f);

    /// <summary>
    /// Builds the miniature under <paramref name="parent"/>, scaled to fit
    /// <paramref name="box"/>. Returns null if the document yields no parts.
    /// </summary>
    public static GameObject Create(
        AssemblyDocumentDto document,
        Transform parent,
        Vector3 localPosition,
        Vector2 box,
        float partHeight,
        float partDiameter,
        Func<string, string, Mesh> meshFor)
    {
        var poses = AssemblyLayout.Compute(document, partHeight, partDiameter);
        if (poses.Count == 0)
            return null;

        var root = new GameObject("Preview");
        root.transform.SetParent(parent, false);

        // The parts go under a pivot so the miniature can be centred and scaled as one unit,
        // independently of the tilt applied to the root.
        var pivot = new GameObject("Pivot");
        pivot.transform.SetParent(root.transform, false);

        var combined = new Bounds();
        var first = true;

        foreach (var pose in poses)
        {
            var mesh = meshFor(pose.ObjectType, pose.Instructions);
            if (mesh == null)
                continue;

            var part = new GameObject(pose.SlotId);
            part.transform.SetParent(pivot.transform, false);
            part.transform.localPosition = pose.LocalPosition;
            part.transform.localRotation = pose.LocalRotation;

            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = HologramMaterials.Preview;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var bounds = AssemblyLayout.TransformBounds(
                mesh.bounds, Matrix4x4.TRS(pose.LocalPosition, pose.LocalRotation, Vector3.one));

            if (first)
            {
                combined = bounds;
                first = false;
            }
            else
            {
                combined.Encapsulate(bounds);
            }
        }

        if (first)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        // Fit the tilted silhouette, not the upright one, or a tall assembly's corners spill
        // out of the row once the view angle is applied.
        var size = AssemblyLayout.TransformBounds(combined, Matrix4x4.Rotate(ViewAngle)).size;
        var scale = Mathf.Min(
            box.x / Mathf.Max(size.x, 0.0001f),
            box.y / Mathf.Max(size.y, 0.0001f));

        pivot.transform.localScale = Vector3.one * scale;
        pivot.transform.localPosition = -combined.center * scale;

        // Sit the miniature's back face on the requested plane so it stands proud of the menu
        // row rather than sinking through it. Panels face -Z, so forward is negative.
        var depth = size.z * scale;
        root.transform.localPosition = localPosition + new Vector3(0f, 0f, -depth * 0.5f);
        root.transform.localRotation = ViewAngle;
        return root;
    }
}
