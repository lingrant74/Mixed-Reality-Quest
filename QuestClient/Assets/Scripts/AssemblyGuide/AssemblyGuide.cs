using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Drives the assembly walkthrough. The hologram starts free to move; once the user locks it
/// down it stays put and the guide reveals one instruction at a time. Every visual is derived
/// from <see cref="_phase"/>, <see cref="_stepIndex"/> and each part's done flag by
/// <see cref="Refresh"/>, so navigating backwards or skipping ahead cannot desync the display.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class AssemblyGuide : MonoBehaviour
{
    [Serializable]
    public class InstructionStep
    {
        public string title;
        public string[] partNames;

        /// <summary>Full instruction prose. Comes from the backend when available.</summary>
        [TextArea] public string detail;
    }

    /// <summary>
    /// Maps a backend <c>slot_id</c> onto the child GameObject that represents it. The backend
    /// names slots by their role in the assembly; our mesh children are named by build order.
    /// </summary>
    [Serializable]
    public class SlotBinding
    {
        public string slotId;
        public string partName;
    }

    enum Phase
    {
        Placing,
        Assembling,
        Complete
    }

    [Header("Backend")]
    [SerializeField] string serverUrl = "http://10.50.19.61:8000";
    [SerializeField] string instructionId = "assembly-1";
    [SerializeField] float instructionTimeoutSeconds = 6f;

    [Space]
    [SerializeField]
    SlotBinding[] slotBindings =
    {
        new SlotBinding { slotId = "bottom_left", partName = "Cup 01" },
        new SlotBinding { slotId = "bottom_center", partName = "Cup 02" },
        new SlotBinding { slotId = "bottom_right", partName = "Cup 03" },
        new SlotBinding { slotId = "middle_left", partName = "Cup 04" },
        new SlotBinding { slotId = "middle_right", partName = "Cup 05" },
        new SlotBinding { slotId = "top_center", partName = "Cup 06" },
    };

    /// <summary>Fallback used when the backend is unreachable; replaced by the fetched document.</summary>
    [SerializeField]
    InstructionStep[] steps =
    {
        new InstructionStep { title = "Bottom row", partNames = new[] { "Cup 01", "Cup 02", "Cup 03" },
            detail = "Place three bottom cups with openings facing down on the table." },
        new InstructionStep { title = "Middle row", partNames = new[] { "Cup 04", "Cup 05" },
            detail = "Add two cups with openings facing up, each bridging adjacent bottom cups." },
        new InstructionStep { title = "Top cup", partNames = new[] { "Cup 06" },
            detail = "Add one top cup with its opening facing down, bridging both middle cups." },
    };

    [SerializeField] Vector2 doneButtonSize = new Vector2(0.078f, 0.034f);
    [SerializeField] Vector2 wideButtonSize = new Vector2(0.21f, 0.058f);
    [SerializeField] Vector2 navButtonSize = new Vector2(0.10f, 0.046f);
    [SerializeField] float buttonForwardOffset = 0.11f;

    static readonly Color DoneIdle = new Color(0.10f, 0.34f, 0.62f);
    static readonly Color DoneHover = new Color(0.20f, 0.72f, 1f);
    static readonly Color UndoIdle = new Color(0.48f, 0.26f, 0.04f);
    static readonly Color UndoHover = new Color(1f, 0.55f, 0.12f);
    static readonly Color NavIdle = new Color(0.18f, 0.19f, 0.24f);
    static readonly Color NavHover = new Color(0.45f, 0.50f, 0.60f);

    class Part
    {
        public string Name;
        public int StepIndex;
        public Renderer Renderer;
        public Vector3 LocalCenter;
        public WorldButton Button;
        public bool IsDone;
    }

    readonly List<Part> _parts = new List<Part>();

    Phase _phase = Phase.Placing;
    int _stepIndex;

    WorldButton _lockButton;
    WorldButton _nextButton;
    WorldButton _backButton;
    WorldButton _forwardButton;
    ProgressHud _hud;
    InfoPanel _instructionPanel;
    string _sourceLabel = "local";

    Grabbable _grabbable;
    AnchorOnRelease _anchor;
    HandGrabInteractable[] _handGrabs;

    float _towerTop;

    int LastStep => steps.Length - 1;

    void Start()
    {
        _grabbable = GetComponent<Grabbable>();
        _anchor = GetComponent<AnchorOnRelease>();
        _handGrabs = GetComponentsInChildren<HandGrabInteractable>(true);

        _hud = ProgressHud.Create();
        _hud.SetProgress(0f, "Loading instructions...");

        StartCoroutine(Boot());
    }

    /// <summary>
    /// Pulls the instruction document before building anything, since the steps determine how
    /// many parts and buttons exist. Falls back to the serialized copy if the server is down.
    /// </summary>
    IEnumerator Boot()
    {
        yield return AssemblyBackend.GetInstruction(
            serverUrl,
            instructionId,
            instructionTimeoutSeconds,
            ApplyDocument,
            error =>
            {
                _sourceLabel = "local fallback";
                Debug.LogWarning($"[AssemblyGuide] Could not load '{instructionId}' from {serverUrl} " +
                                 $"({error}). Using the built-in steps.");
            });

        CollectParts();
        BuildButtons();

        _phase = Phase.Placing;
        SetGrabEnabled(true);
        Refresh();
    }

    /// <summary>
    /// Rebuilds the step list from a backend document. The document's per-step
    /// <c>required_slot_ids</c> is cumulative, so the parts a step actually introduces come
    /// from the objects list instead, where each object names the step that introduces it.
    /// </summary>
    void ApplyDocument(AssemblyDocumentDto document)
    {
        var bindings = new Dictionary<string, string>();
        foreach (var binding in slotBindings)
        {
            if (binding != null && !string.IsNullOrEmpty(binding.slotId))
                bindings[binding.slotId] = binding.partName;
        }

        var ordered = new List<AssemblyStepDto>(document.steps);
        ordered.Sort((a, b) => a.step_index.CompareTo(b.step_index));

        var built = new List<InstructionStep>();
        var unmapped = new List<string>();

        foreach (var step in ordered)
        {
            var partNames = new List<string>();
            foreach (var obj in document.objects)
            {
                if (obj == null || obj.step_index != step.step_index)
                    continue;

                if (bindings.TryGetValue(obj.slot_id, out var partName) && !string.IsNullOrEmpty(partName))
                    partNames.Add(partName);
                else
                    unmapped.Add(obj.slot_id);
            }

            if (partNames.Count == 0)
                continue;

            built.Add(new InstructionStep
            {
                title = $"Step {step.step_index + 1}",
                partNames = partNames.ToArray(),
                detail = step.instructions,
            });
        }

        if (unmapped.Count > 0)
        {
            Debug.LogWarning($"[AssemblyGuide] {unmapped.Count} slot(s) in '{document.instruction_id}' have no " +
                             $"binding to a child object and were skipped: {string.Join(", ", unmapped)}");
        }

        if (built.Count == 0)
        {
            _sourceLabel = "local fallback";
            Debug.LogWarning($"[AssemblyGuide] '{document.instruction_id}' produced no usable steps. " +
                             "Using the built-in steps.");
            return;
        }

        steps = built.ToArray();
        _sourceLabel = $"{document.instruction_id} v{document.version}";
        Debug.Log($"[AssemblyGuide] Loaded '{document.name}' ({_sourceLabel}) from {serverUrl}: " +
                  $"{steps.Length} step(s), {document.objects.Length} object(s).");
    }

    void CollectParts()
    {
        var localTop = 0f;

        for (var stepIndex = 0; stepIndex < steps.Length; stepIndex++)
        {
            var step = steps[stepIndex];
            if (step?.partNames == null)
                continue;

            foreach (var name in step.partNames)
            {
                var child = transform.Find(name);
                if (child == null)
                {
                    Debug.LogWarning($"[AssemblyGuide] No part named '{name}' under '{gameObject.name}'.");
                    continue;
                }

                var renderer = child.GetComponentInChildren<Renderer>();
                if (renderer == null)
                    continue;

                var localCenter = transform.InverseTransformPoint(renderer.bounds.center);
                var localMax = transform.InverseTransformPoint(renderer.bounds.max);
                localTop = Mathf.Max(localTop, localMax.y);

                _parts.Add(new Part
                {
                    Name = name,
                    StepIndex = stepIndex,
                    Renderer = renderer,
                    LocalCenter = localCenter,
                });
            }
        }

        _towerTop = localTop;
    }

    void BuildButtons()
    {
        foreach (var part in _parts)
        {
            var captured = part;
            var button = WorldButton.Create(
                $"Part Button ({part.Name})",
                "DONE",
                doneButtonSize,
                transform,
                DoneIdle,
                DoneHover);
            button.transform.localPosition = part.LocalCenter + new Vector3(0f, 0f, -buttonForwardOffset);
            button.Clicked += () => TogglePart(captured);
            button.SetVisible(false);
            part.Button = button;
        }

        var below = -0.09f;
        var forward = -buttonForwardOffset - 0.01f;

        _lockButton = WorldButton.Create(
            "Lock Button", "LOCK IN PLACE", wideButtonSize, transform,
            new Color(0.55f, 0.30f, 0.05f), new Color(1f, 0.55f, 0.10f));
        _lockButton.transform.localPosition = new Vector3(0f, below, forward);
        _lockButton.Clicked += Lock;

        _nextButton = WorldButton.Create(
            "Next Step Button", "NEXT STEP", wideButtonSize, transform,
            new Color(0.08f, 0.42f, 0.22f), new Color(0.15f, 0.85f, 0.40f));
        _nextButton.transform.localPosition = new Vector3(0f, _towerTop + 0.09f, forward);
        _nextButton.Clicked += AdvanceStep;
        _nextButton.SetVisible(false);

        // Navigation sits below the tower, where the lock button was before locking.
        var navX = navButtonSize.x * 0.5f + 0.008f;

        _backButton = WorldButton.Create(
            "Back Button", "< BACK", navButtonSize, transform, NavIdle, NavHover);
        _backButton.transform.localPosition = new Vector3(-navX, below, forward);
        _backButton.Clicked += GoBack;
        _backButton.SetVisible(false);

        _forwardButton = WorldButton.Create(
            "Forward Button", "SKIP >", navButtonSize, transform, NavIdle, NavHover);
        _forwardButton.transform.localPosition = new Vector3(navX, below, forward);
        _forwardButton.Clicked += SkipForward;
        _forwardButton.SetVisible(false);

        // Sits clear of the tower's left edge (-0.15) and the leftmost part button (-0.138).
        _instructionPanel = InfoPanel.Create(
            "Instruction Panel", transform, new Vector2(0.24f, 0.15f), new Color(0.05f, 0.07f, 0.11f));
        _instructionPanel.transform.localPosition = new Vector3(-0.30f, _towerTop * 0.6f, forward);
        _instructionPanel.SetVisible(false);
    }

    /// <summary>Applies every visual from the current state. The only place visuals are set.</summary>
    void Refresh()
    {
        var stepComplete = IsStepComplete(_stepIndex);

        foreach (var part in _parts)
        {
            switch (_phase)
            {
                case Phase.Placing:
                    Show(part, true);
                    Paint(part, HologramMaterials.Pending);
                    part.Button.SetVisible(false);
                    break;

                case Phase.Complete:
                    Show(part, true);
                    Paint(part, HologramMaterials.StepComplete);
                    part.Button.SetVisible(false);
                    break;

                default:
                    if (part.StepIndex < _stepIndex)
                    {
                        // Finished instructions stay on screen in green as a record.
                        Show(part, true);
                        Paint(part, HologramMaterials.StepComplete);
                        part.Button.SetVisible(false);
                    }
                    else if (part.StepIndex == _stepIndex)
                    {
                        Show(part, true);
                        Paint(part, stepComplete
                            ? HologramMaterials.StepComplete
                            : part.IsDone ? HologramMaterials.Done : HologramMaterials.Pending);
                        part.Button.SetLabel(part.IsDone ? "UNDO" : "DONE");
                        part.Button.SetColors(
                            part.IsDone ? UndoIdle : DoneIdle,
                            part.IsDone ? UndoHover : DoneHover);
                        part.Button.SetVisible(true);
                    }
                    else
                    {
                        Show(part, false);
                        part.Button.SetVisible(false);
                    }

                    break;
            }
        }

        _lockButton.SetVisible(_phase == Phase.Placing);

        var showNext = _phase == Phase.Assembling && stepComplete;
        if (showNext)
            _nextButton.SetLabel(_stepIndex >= LastStep ? "FINISH" : "NEXT STEP");
        _nextButton.SetVisible(showNext);

        _backButton.SetVisible(_phase == Phase.Complete || (_phase == Phase.Assembling && _stepIndex > 0));
        _forwardButton.SetVisible(_phase == Phase.Assembling && _stepIndex < LastStep);

        if (_phase == Phase.Assembling)
        {
            var step = steps[_stepIndex];
            _instructionPanel.SetText(
                $"Step {_stepIndex + 1} of {steps.Length}",
                string.IsNullOrEmpty(step.detail) ? step.title : step.detail);
            _instructionPanel.SetVisible(true);
        }
        else if (_phase == Phase.Complete)
        {
            _instructionPanel.SetText("Complete", "All instructions finished.");
            _instructionPanel.SetVisible(true);
        }
        else
        {
            _instructionPanel.SetVisible(false);
        }

        Report();
    }

    void Lock()
    {
        if (_phase != Phase.Placing)
            return;

        _phase = Phase.Assembling;
        _stepIndex = 0;

        SetGrabEnabled(false);
        if (_anchor != null)
            _anchor.LockInPlace();

        Refresh();
    }

    /// <summary>Marks a part placed, or takes it back if the user already marked it.</summary>
    void TogglePart(Part part)
    {
        if (_phase != Phase.Assembling || part.StepIndex != _stepIndex)
            return;

        part.IsDone = !part.IsDone;
        Refresh();
    }

    void AdvanceStep()
    {
        if (_phase != Phase.Assembling || !IsStepComplete(_stepIndex))
            return;

        if (_stepIndex >= LastStep)
            _phase = Phase.Complete;
        else
            _stepIndex++;

        Refresh();
    }

    /// <summary>Returns to the previous instruction, keeping whatever was already marked done.</summary>
    void GoBack()
    {
        if (_phase == Phase.Complete)
        {
            _phase = Phase.Assembling;
            _stepIndex = LastStep;
        }
        else if (_phase == Phase.Assembling && _stepIndex > 0)
        {
            _stepIndex--;
        }
        else
        {
            return;
        }

        Refresh();
    }

    /// <summary>Jumps to the next instruction without requiring the current one to be finished.</summary>
    void SkipForward()
    {
        if (_phase != Phase.Assembling || _stepIndex >= LastStep)
            return;

        _stepIndex++;
        Refresh();
    }

    bool IsStepComplete(int index)
    {
        var any = false;
        foreach (var part in _parts)
        {
            if (part.StepIndex != index)
                continue;

            any = true;
            if (!part.IsDone)
                return false;
        }

        return any;
    }

    void Report()
    {
        if (_hud == null)
            return;

        var total = _parts.Count;
        var done = 0;
        var inCurrentStep = 0;
        var doneInCurrentStep = 0;

        foreach (var part in _parts)
        {
            if (part.IsDone)
                done++;

            if (part.StepIndex != _stepIndex)
                continue;

            inCurrentStep++;
            if (part.IsDone)
                doneInCurrentStep++;
        }

        switch (_phase)
        {
            case Phase.Placing:
                _hud.SetProgress(0f, "Place the hologram, then LOCK");
                break;
            case Phase.Assembling:
                _hud.SetProgress(
                    total == 0 ? 0f : done / (float)total,
                    $"Step {_stepIndex + 1}/{steps.Length}   {doneInCurrentStep}/{inCurrentStep} placed");
                break;
            case Phase.Complete:
                _hud.SetProgress(1f, "Assembly complete");
                break;
        }
    }

    void SetGrabEnabled(bool enabled)
    {
        if (_grabbable != null)
            _grabbable.enabled = enabled;

        if (_handGrabs == null)
            return;

        foreach (var handGrab in _handGrabs)
        {
            if (handGrab != null)
                handGrab.enabled = enabled;
        }
    }

    static void Show(Part part, bool visible)
    {
        if (part.Renderer != null)
            part.Renderer.enabled = visible;
    }

    static void Paint(Part part, Material material)
    {
        if (part.Renderer != null && material != null)
            part.Renderer.sharedMaterial = material;
    }
}
