using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Works out where each part sits from the instruction document alone.
/// <para>
/// The backend stores no transforms; part geometry lives inside a GLB addressed by node index.
/// But <c>supporting_slot_ids</c> describes the assembly topology, and that is enough: a part
/// rests one layer above its highest support and is centred over the supports it bridges.
/// Parts resting on the reserved <c>table</c> support form the ground layer and spread out
/// sideways. For the six-cup pyramid this reproduces the hand-authored tower exactly, and for
/// a straight column it produces a straight column.
/// </para>
/// </summary>
public static class AssemblyLayout
{
    public const string TableSlot = "table";

    public struct PartPose
    {
        public string SlotId;
        public string ObjectType;
        public string Instructions;
        public int StepIndex;
        public int Layer;
        public bool OpeningDown;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
    }

    public static List<PartPose> Compute(AssemblyDocumentDto document, float partHeight, float partDiameter)
    {
        var result = new List<PartPose>();
        if (document?.objects == null)
            return result;

        // Supports always reference strictly earlier steps, so a single pass in step order
        // resolves every dependency before it is needed.
        var ordered = new List<AssemblyObjectDto>();
        foreach (var candidate in document.objects)
        {
            if (candidate != null && !string.IsNullOrEmpty(candidate.slot_id))
                ordered.Add(candidate);
        }

        ordered.Sort((a, b) => a.step_index.CompareTo(b.step_index));

        var groundSlots = new List<string>();
        foreach (var obj in ordered)
        {
            if (IsGround(obj))
                groundSlots.Add(obj.slot_id);
        }

        var layerBySlot = new Dictionary<string, int>();
        var xBySlot = new Dictionary<string, float>();

        foreach (var obj in ordered)
        {
            int layer;
            float x;

            if (IsGround(obj))
            {
                layer = 0;
                var index = groundSlots.IndexOf(obj.slot_id);
                x = (index - (groundSlots.Count - 1) * 0.5f) * partDiameter;
            }
            else
            {
                layer = 0;
                var sum = 0f;
                var count = 0;

                foreach (var support in obj.supporting_slot_ids)
                {
                    if (string.IsNullOrEmpty(support) || support == TableSlot)
                        continue;

                    if (layerBySlot.TryGetValue(support, out var supportLayer))
                        layer = Mathf.Max(layer, supportLayer + 1);
                    if (xBySlot.TryGetValue(support, out var supportX))
                    {
                        sum += supportX;
                        count++;
                    }
                }

                x = count > 0 ? sum / count : 0f;
            }

            layerBySlot[obj.slot_id] = layer;
            xBySlot[obj.slot_id] = x;

            // An inverted part is modelled base-at-origin, so flipping it puts its base a full
            // part-height above the layer floor.
            var openingDown = obj.required_opening_direction == "down";
            var y = layer * partHeight + (openingDown ? partHeight : 0f);

            result.Add(new PartPose
            {
                SlotId = obj.slot_id,
                ObjectType = obj.object_type,
                Instructions = obj.placement_instructions,
                StepIndex = obj.step_index,
                Layer = layer,
                OpeningDown = openingDown,
                LocalPosition = new Vector3(x, y, 0f),
                LocalRotation = openingDown ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity,
            });
        }

        return result;
    }

    /// <summary>
    /// Bounds carried through a transform, measured from all eight corners so the result is
    /// correct for any rotation.
    /// </summary>
    public static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
        var extents = bounds.extents;

        for (var corner = 0; corner < 8; corner++)
        {
            var offset = new Vector3(
                (corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y,
                (corner & 4) == 0 ? -extents.z : extents.z);
            result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + offset));
        }

        return result;
    }

    static bool IsGround(AssemblyObjectDto obj)
    {
        if (obj.supporting_slot_ids == null || obj.supporting_slot_ids.Length == 0)
            return true;

        foreach (var support in obj.supporting_slot_ids)
        {
            if (!string.IsNullOrEmpty(support) && support != TableSlot)
                return false;
        }

        return true;
    }
}
