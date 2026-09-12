using System;
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
    }

    enum Phase
    {
        Placing,
        Assembling,
        Complete
    }

    [SerializeField]
    InstructionStep[] steps =
    {
        new InstructionStep { title = "Bottom row", partNames = new[] { "Cup 01", "Cup 02", "Cup 03" } },
        new InstructionStep { title = "Middle row", partNames = new[] { "Cup 04", "Cup 05" } },
        new InstructionStep { title = "Top cup", partNames = new[] { "Cup 06" } },
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

        CollectParts();
        BuildButtons();

        _hud = ProgressHud.Create();

        _phase = Phase.Placing;
        SetGrabEnabled(true);
        Refresh();
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
                    $"Step {_stepIndex + 1}/{steps.Length}  {steps[_stepIndex].title}  {doneInCurrentStep}/{inCurrentStep}");
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
