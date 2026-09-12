/// <summary>
/// The red cup tower, expressed in the backend's own schema so the offline path and the
/// online path run through identical code. Used when the server cannot be reached.
/// </summary>
public static class BuiltInAssembly
{
    public const string Id = "builtin-red-cup-tower";

    public static AssemblyDocumentDto CupTower()
    {
        return new AssemblyDocumentDto
        {
            instruction_id = Id,
            version = 1,
            name = "Red cup tower",
            description = "Six red cups in three layers. Built in, used when the server is offline.",
            objects = new[]
            {
                Cup("bottom_left", 0, "down", new[] { AssemblyLayout.TableSlot },
                    "Place a cup in bottom left with its opening facing down, resting on the table."),
                Cup("bottom_center", 0, "down", new[] { AssemblyLayout.TableSlot },
                    "Place a cup in bottom center with its opening facing down, resting on the table."),
                Cup("bottom_right", 0, "down", new[] { AssemblyLayout.TableSlot },
                    "Place a cup in bottom right with its opening facing down, resting on the table."),
                Cup("middle_left", 1, "up", new[] { "bottom_left", "bottom_center" },
                    "Place a cup in middle left with its opening facing up, bridging the two cups below."),
                Cup("middle_right", 1, "up", new[] { "bottom_center", "bottom_right" },
                    "Place a cup in middle right with its opening facing up, bridging the two cups below."),
                Cup("top_center", 2, "down", new[] { "middle_left", "middle_right" },
                    "Place the last cup on top with its opening facing down, bridging both middle cups."),
            },
            steps = new[]
            {
                Step(0, "Place three cups with openings facing down on the table.",
                    new[] { "bottom_left", "bottom_center", "bottom_right" }),
                Step(1, "Keep the bottom layer and add two cups with openings facing up, each bridging adjacent bottom cups.",
                    new[] { "bottom_left", "bottom_center", "bottom_right", "middle_left", "middle_right" }),
                Step(2, "Keep both earlier layers and add one top cup with its opening facing down, bridging both middle cups.",
                    new[] { "bottom_left", "bottom_center", "bottom_right", "middle_left", "middle_right", "top_center" }),
            },
        };
    }

    static AssemblyObjectDto Cup(string slotId, int stepIndex, string opening, string[] supports, string instructions)
    {
        return new AssemblyObjectDto
        {
            slot_id = slotId,
            object_type = "cup",
            step_index = stepIndex,
            required_opening_direction = opening,
            supporting_slot_ids = supports,
            placement_instructions = instructions,
        };
    }

    static AssemblyStepDto Step(int index, string instructions, string[] requiredSlotIds)
    {
        return new AssemblyStepDto
        {
            step_index = index,
            instructions = instructions,
            required_slot_ids = requiredSlotIds,
            target_image_path = null,
        };
    }
}
