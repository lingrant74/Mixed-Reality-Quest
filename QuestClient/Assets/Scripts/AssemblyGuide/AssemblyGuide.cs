using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives the assembly walkthrough. At startup the user picks an assembly from the backend;
/// the hologram is then generated from that document, positioned freely, locked to a spatial
/// anchor, and worked through one instruction at a time.
/// <para>
/// Every visual is derived from <see cref="_phase"/>, <see cref="_stepIndex"/> and each part's
/// done flag by <see cref="Refresh"/>, so navigating backwards or skipping ahead cannot
/// desync the display.
/// </para>
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class AssemblyGuide : MonoBehaviour
{
    class InstructionStep
    {
        public string Detail;
        public List<string> SlotIds = new List<string>();
    }

    enum Phase
    {
        Choosing,
        Placing,
        Assembling,
        Complete
    }

    [Header("Backend")]
    [SerializeField] string serverUrl = "http://10.50.19.61:8000";
    [SerializeField] float requestTimeoutSeconds = 6f;

    [Header("Geometry")]
    [Tooltip("Mesh used for parts whose type reads as a cup. Modelled base-at-origin.")]
    [SerializeField] Mesh cupMesh;

    [Header("Layout")]
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
    static readonly Color PanelBackground = new Color(0.05f, 0.07f, 0.11f);

    const string GeneratedPrefix = "Part ";

    class Part
    {
        public string SlotId;
        public string ObjectType;
        public int StepIndex;
        public Renderer Renderer;
        public Vector3 LocalCenter;
        public WorldButton Button;
        public bool IsDone;
    }

    readonly List<Part> _parts = new List<Part>();
    readonly List<InstructionStep> _steps = new List<InstructionStep>();

    Phase _phase = Phase.Choosing;
    int _stepIndex;

    WorldButton _lockButton;
    WorldButton _nextButton;
    WorldButton _backButton;
    WorldButton _forwardButton;
    ProgressHud _hud;
    InfoPanel _instructionPanel;
    InfoPanel _materialsPanel;

    Grabbable _grabbable;
    AnchorOnRelease _anchor;
    HandGrabInteractable[] _handGrabs;

    Mesh _boxMesh;
    float _partHeight = 0.1225f;
    float _partDiameter = 0.0992f;
    float _towerTop;
    string _assemblyName = string.Empty;
    string _sourceLabel = "built-in";

    int LastStep => _steps.Count - 1;

    void Start()
    {
        _grabbable = GetComponent<Grabbable>();
        _anchor = GetComponent<AnchorOnRelease>();
        _handGrabs = GetComponentsInChildren<HandGrabInteractable>(true);

        if (cupMesh != null)
        {
            var size = cupMesh.bounds.size;
            _partHeight = size.y;
            _partDiameter = Mathf.Max(size.x, size.z);
        }

        // The scene's authored cups are an editor preview only; clear them before the server
        // round trip so they are not left hanging behind the selection menu.
        ClearParts();

        SetGrabEnabled(false);
        StartCoroutine(Boot());
    }

    /// <summary>
    /// Lists the available assemblies, lets the user pick one, then builds everything from the
    /// chosen document. If the server cannot be reached at all we skip the menu and go straight
    /// to the built-in cup tower.
    /// </summary>
    IEnumerator Boot()
    {
        _phase = Phase.Choosing;

        var menu = SelectionMenu.Create(_partHeight, _partDiameter, MeshFor);
        menu.SetStatus("Contacting server...");

        AssemblySummaryDto[] available = null;
        yield return AssemblyBackend.ListInstructions(
            serverUrl, requestTimeoutSeconds,
            list => available = list,
            error => Debug.LogWarning($"[AssemblyGuide] Could not list assemblies from {serverUrl} ({error})."));

        if (available == null)
        {
            Destroy(menu.gameObject);
            Debug.LogWarning("[AssemblyGuide] Server unreachable; using the built-in red cup tower.");
            Begin(BuiltInAssembly.CupTower());
            yield break;
        }

        // Each document is fetched up front so every row can show a miniature of the real
        // assembly, and so picking a row needs no further round trip.
        var entries = new List<SelectionMenu.Entry>();
        for (var i = 0; i < available.Length; i++)
        {
            var summary = available[i];
            menu.SetStatus($"Loading {i + 1} of {available.Length}...");

            AssemblyDocumentDto loaded = null;
            yield return AssemblyBackend.GetInstruction(
                serverUrl, summary.instruction_id, requestTimeoutSeconds,
                document => loaded = document,
                error => Debug.LogWarning(
                    $"[AssemblyGuide] Could not load '{summary.instruction_id}' ({error})."));

            entries.Add(new SelectionMenu.Entry { Summary = summary, Document = loaded });
        }

        // The built-in tower is offered too: it is the only assembly guaranteed to match the
        // physical red cups, which makes it the reliable choice for a demo.
        var builtIn = BuiltInAssembly.CupTower();
        entries.Add(new SelectionMenu.Entry
        {
            Summary = new AssemblySummaryDto
            {
                instruction_id = builtIn.instruction_id,
                version = builtIn.version,
                name = builtIn.name,
                description = builtIn.description,
            },
            Document = builtIn,
        });

        menu.SetEntries(entries);

        SelectionMenu.Entry chosen = null;
        menu.Chosen += entry => chosen = entry;
        while (chosen == null)
            yield return null;

        var picked = chosen.Document;
        if (picked == null)
        {
            Debug.LogWarning(
                $"[AssemblyGuide] '{chosen.Summary.instruction_id}' never loaded; using the built-in tower.");
            picked = BuiltInAssembly.CupTower();
        }

        Begin(picked);
    }

    void Begin(AssemblyDocumentDto document)
    {
        _sourceLabel = document.instruction_id == BuiltInAssembly.Id
            ? "built-in"
            : $"{document.instruction_id} v{document.version}";

        ApplyDocument(document);
        BuildButtons();

        _phase = Phase.Placing;
        _stepIndex = 0;
        SetGrabEnabled(true);
        Refresh();
    }

    /// <summary>Returns to the selection menu so another assembly can be built.</summary>
    void Restart()
    {
        TearDown();

        if (_anchor != null)
            _anchor.Unlock();

        SetGrabEnabled(false);
        _stepIndex = 0;
        StartCoroutine(Boot());
    }

    void TearDown()
    {
        // Buttons are destroyed through their part references before the parts list is cleared.
        foreach (var part in _parts)
        {
            if (part.Button != null)
                Destroy(part.Button.gameObject);
        }

        ClearParts();
        _steps.Clear();

        DestroyWidget(_lockButton);
        DestroyWidget(_nextButton);
        DestroyWidget(_backButton);
        DestroyWidget(_forwardButton);
        DestroyWidget(_materialsPanel);

        // The progress bar is parented to the instruction panel, so it goes with it.
        DestroyWidget(_instructionPanel);

        _lockButton = null;
        _nextButton = null;
        _backButton = null;
        _forwardButton = null;
        _materialsPanel = null;
        _instructionPanel = null;
        _hud = null;
    }

    static void DestroyWidget(Component widget)
    {
        if (widget != null)
            Destroy(widget.gameObject);
    }

    /// <summary>
    /// Generates the hologram and the step list from a document. The per-step
    /// <c>required_slot_ids</c> is cumulative, so the parts a step introduces come from the
    /// objects list instead, where each object names the step that introduces it.
    /// </summary>
    void ApplyDocument(AssemblyDocumentDto document)
    {
        _assemblyName = string.IsNullOrEmpty(document.name) ? document.instruction_id : document.name;

        var poses = AssemblyLayout.Compute(document, _partHeight, _partDiameter);
        if (poses.Count == 0)
        {
            Debug.LogError($"[AssemblyGuide] '{document.instruction_id}' produced no parts.");
            return;
        }

        ClearParts();

        var combined = new Bounds();
        var first = true;
        foreach (var pose in poses)
        {
            var part = CreatePart(pose, out var localBounds);
            _parts.Add(part);

            if (first)
            {
                combined = localBounds;
                first = false;
            }
            else
            {
                combined.Encapsulate(localBounds);
            }
        }

        _towerTop = combined.max.y;
        FitCollider(combined);

        // Group parts into steps, preserving the document's step ordering.
        var byStep = new SortedDictionary<int, InstructionStep>();
        foreach (var step in document.steps)
        {
            if (step != null)
                byStep[step.step_index] = new InstructionStep { Detail = step.instructions };
        }

        foreach (var part in _parts)
        {
            if (!byStep.TryGetValue(part.StepIndex, out var step))
            {
                step = new InstructionStep { Detail = string.Empty };
                byStep[part.StepIndex] = step;
            }

            step.SlotIds.Add(part.SlotId);
        }

        _steps.Clear();
        foreach (var entry in byStep.Values)
        {
            if (entry.SlotIds.Count > 0)
                _steps.Add(entry);
        }

        // Step indices in the document may not be contiguous once empty steps are dropped, so
        // remap each part onto its position in the final list.
        var order = new Dictionary<int, int>();
        var next = 0;
        foreach (var entry in byStep)
        {
            if (entry.Value.SlotIds.Count > 0)
                order[entry.Key] = next++;
        }

        foreach (var part in _parts)
        {
            if (order.TryGetValue(part.StepIndex, out var remapped))
                part.StepIndex = remapped;
        }

        Debug.Log($"[AssemblyGuide] Built '{_assemblyName}' ({_sourceLabel}): " +
                  $"{_parts.Count} part(s) in {_steps.Count} step(s), {_towerTop:F3} m tall.");
    }

    Part CreatePart(AssemblyLayout.PartPose pose, out Bounds localBounds)
    {
        var go = new GameObject($"{GeneratedPrefix}{pose.SlotId}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pose.LocalPosition;
        go.transform.localRotation = pose.LocalRotation;

        var mesh = MeshFor(pose.ObjectType, pose.Instructions);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = HologramMaterials.Pending;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        localBounds = AssemblyLayout.TransformBounds(mesh.bounds, Matrix4x4.TRS(
            pose.LocalPosition, pose.LocalRotation, go.transform.localScale));

        return new Part
        {
            SlotId = pose.SlotId,
            ObjectType = string.IsNullOrEmpty(pose.ObjectType) ? "part" : pose.ObjectType,
            StepIndex = pose.StepIndex,
            Renderer = renderer,
            LocalCenter = localBounds.center,
        };
    }

    /// <summary>
    /// We only ship a cup mesh. Anything that does not read as a cup gets a neutral block, so
    /// an unfamiliar assembly is shown honestly rather than disguised as cups.
    /// </summary>
    Mesh MeshFor(string objectType, string instructions)
    {
        var looksLikeCup =
            (!string.IsNullOrEmpty(objectType) && objectType.IndexOf("cup", StringComparison.OrdinalIgnoreCase) >= 0) ||
            (!string.IsNullOrEmpty(instructions) && instructions.IndexOf("cup", StringComparison.OrdinalIgnoreCase) >= 0);

        if (looksLikeCup && cupMesh != null)
            return cupMesh;

        if (_boxMesh == null)
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boxMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
        }

        return _boxMesh;
    }

    void ClearParts()
    {
        _parts.Clear();

        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            // "Cup NN" children come from the original hand-authored tower.
            if (child.name.StartsWith(GeneratedPrefix) || child.name.StartsWith("Cup "))
                Destroy(child.gameObject);
        }
    }

    void FitCollider(Bounds localBounds)
    {
        var collider = GetComponent<BoxCollider>();
        if (collider == null || _parts.Count == 0)
            return;

        collider.center = localBounds.center;
        collider.size = localBounds.size;
    }

    void BuildButtons()
    {
        foreach (var part in _parts)
        {
            var captured = part;
            var button = WorldButton.Create(
                $"Done Button ({part.SlotId})", "DONE", doneButtonSize, transform, DoneIdle, DoneHover);
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
        // Doubles as the way back to the menu once the assembly is finished.
        _nextButton.Clicked += () =>
        {
            if (_phase == Phase.Complete)
                Restart();
            else
                AdvanceStep();
        };
        _nextButton.SetVisible(false);

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

        var sideOffset = _partDiameter * 3f + 0.15f;
        var instructionSize = new Vector2(0.24f, 0.15f);

        _instructionPanel = InfoPanel.Create(
            "Instruction Panel", transform, instructionSize, PanelBackground);
        _instructionPanel.transform.localPosition = new Vector3(-sideOffset, _towerTop * 0.6f, forward);
        _instructionPanel.SetVisible(false);

        // Parented to the instruction panel so the bar sits directly above the text it
        // describes and inherits the panel's billboard rotation instead of drifting from it.
        _hud = ProgressHud.Create(
            _instructionPanel.transform,
            new Vector3(0f, instructionSize.y * 0.5f + 0.014f, -0.002f),
            instructionSize.x * 0.92f);

        _materialsPanel = InfoPanel.Create(
            "Materials Panel", transform, new Vector2(0.21f, 0.15f), PanelBackground);
        _materialsPanel.transform.localPosition = new Vector3(sideOffset, _towerTop * 0.6f, forward);
        _materialsPanel.SetVisible(false);
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

        var showNext = _phase == Phase.Complete || (_phase == Phase.Assembling && stepComplete);
        if (_phase == Phase.Complete)
            _nextButton.SetLabel("CHOOSE ANOTHER");
        else if (showNext)
            _nextButton.SetLabel(_stepIndex >= LastStep ? "FINISH" : "NEXT STEP");
        _nextButton.SetVisible(showNext);

        _backButton.SetVisible(_phase == Phase.Complete || (_phase == Phase.Assembling && _stepIndex > 0));
        _forwardButton.SetVisible(_phase == Phase.Assembling && _stepIndex < LastStep);

        if (_phase == Phase.Placing)
        {
            _instructionPanel.SetText(
                _assemblyName, "Grab the hologram to position it, then press LOCK IN PLACE.");
            _instructionPanel.SetVisible(true);
        }
        else if (_phase == Phase.Assembling)
        {
            _instructionPanel.SetText($"Step {_stepIndex + 1} of {_steps.Count}", _steps[_stepIndex].Detail);
            _instructionPanel.SetVisible(true);
        }
        else if (_phase == Phase.Complete)
        {
            _instructionPanel.SetText("Complete", $"{_assemblyName} finished.");
            _instructionPanel.SetVisible(true);
        }
        else
        {
            _instructionPanel.SetVisible(false);
        }

        RefreshMaterials();
        Report();
    }

    /// <summary>Bill of materials, with how many of each type are still to be placed.</summary>
    void RefreshMaterials()
    {
        if (_materialsPanel == null)
            return;

        if (_phase == Phase.Choosing)
        {
            _materialsPanel.SetVisible(false);
            return;
        }

        var totals = new SortedDictionary<string, int>();
        var remaining = new SortedDictionary<string, int>();

        foreach (var part in _parts)
        {
            totals.TryGetValue(part.ObjectType, out var total);
            totals[part.ObjectType] = total + 1;

            if (!remaining.ContainsKey(part.ObjectType))
                remaining[part.ObjectType] = 0;
            if (!part.IsDone)
                remaining[part.ObjectType] = remaining[part.ObjectType] + 1;
        }

        var body = new System.Text.StringBuilder();
        foreach (var entry in totals)
        {
            var left = remaining[entry.Key];
            body.AppendLine(left > 0
                ? $"{entry.Key} x{entry.Value}   {left} left"
                : $"{entry.Key} x{entry.Value}   done");
        }

        if (_phase == Phase.Assembling)
        {
            var stepTotal = 0;
            var stepLeft = 0;
            foreach (var part in _parts)
            {
                if (part.StepIndex != _stepIndex)
                    continue;

                stepTotal++;
                if (!part.IsDone)
                    stepLeft++;
            }

            body.AppendLine();
            body.AppendLine($"This step: {stepLeft} of {stepTotal} left");
        }

        _materialsPanel.SetText("MATERIALS", body.ToString().TrimEnd());
        _materialsPanel.SetVisible(true);
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
                    $"Step {_stepIndex + 1}/{_steps.Count}   {doneInCurrentStep}/{inCurrentStep} placed");
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
