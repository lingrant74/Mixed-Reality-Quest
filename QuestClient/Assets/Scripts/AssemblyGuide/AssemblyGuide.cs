using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Drives the assembly walkthrough. The hologram starts free to move; once the user locks it
/// down it stays put and the guide reveals one instruction at a time, showing only the parts
/// that belong to the current step.
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

    [SerializeField] Vector2 doneButtonSize = new Vector2(0.062f, 0.028f);
    [SerializeField] Vector2 wideButtonSize = new Vector2(0.19f, 0.055f);
    [SerializeField] float buttonForwardOffset = 0.11f;

    class Part
    {
        public string Name;
        public Renderer Renderer;
        public Vector3 LocalCenter;
        public WorldButton DoneButton;
        public bool IsDone;
    }

    readonly List<Part> _parts = new List<Part>();

    Phase _phase = Phase.Placing;
    int _stepIndex;

    WorldButton _lockButton;
    WorldButton _nextButton;
    ProgressHud _hud;

    Grabbable _grabbable;
    AnchorOnRelease _anchor;
    HandGrabInteractable[] _handGrabs;

    float _towerTop;

    void Start()
    {
        _grabbable = GetComponent<Grabbable>();
        _anchor = GetComponent<AnchorOnRelease>();
        _handGrabs = GetComponentsInChildren<HandGrabInteractable>(true);

        CollectParts();
        BuildButtons();

        _hud = ProgressHud.Create();

        EnterPlacing();
    }

    void CollectParts()
    {
        var localTop = 0f;

        foreach (var step in steps)
        {
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
                    Renderer = renderer,
                    LocalCenter = localCenter,
                });
            }
        }

        _towerTop = localTop;
    }

    void BuildButtons()
    {
        var idle = new Color(0.10f, 0.34f, 0.62f);
        var hover = new Color(0.20f, 0.72f, 1f);

        foreach (var part in _parts)
        {
            var captured = part;
            var button = WorldButton.Create(
                $"Done Button ({part.Name})",
                "DONE",
                doneButtonSize,
                transform,
                idle,
                hover);
            button.transform.localPosition = part.LocalCenter + new Vector3(0f, 0f, -buttonForwardOffset);
            button.Clicked += () => MarkPartDone(captured);
            button.SetVisible(false);
            part.DoneButton = button;
        }

        _lockButton = WorldButton.Create(
            "Lock Button",
            "LOCK IN PLACE",
            wideButtonSize,
            transform,
            new Color(0.55f, 0.30f, 0.05f),
            new Color(1f, 0.55f, 0.10f));
        _lockButton.transform.localPosition = new Vector3(0f, -0.09f, -buttonForwardOffset - 0.01f);
        _lockButton.Clicked += Lock;

        _nextButton = WorldButton.Create(
            "Next Step Button",
            "NEXT STEP",
            wideButtonSize,
            transform,
            new Color(0.08f, 0.42f, 0.22f),
            new Color(0.15f, 0.85f, 0.40f));
        _nextButton.transform.localPosition = new Vector3(0f, _towerTop + 0.09f, -buttonForwardOffset - 0.01f);
        _nextButton.Clicked += AdvanceStep;
        _nextButton.SetVisible(false);
    }

    void EnterPlacing()
    {
        _phase = Phase.Placing;
        SetGrabEnabled(true);

        foreach (var part in _parts)
        {
            part.IsDone = false;
            Show(part, true);
            Paint(part, HologramMaterials.Pending);
            part.DoneButton.SetVisible(false);
        }

        _lockButton.SetVisible(true);
        _nextButton.SetVisible(false);
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

        _lockButton.SetVisible(false);
        EnterStep(0);
    }

    void EnterStep(int index)
    {
        _stepIndex = index;

        foreach (var part in _parts)
        {
            var inStep = IsInStep(part, index);
            part.IsDone = false;
            Show(part, inStep);
            if (inStep)
                Paint(part, HologramMaterials.Pending);
            part.DoneButton.SetVisible(inStep);
        }

        _nextButton.SetVisible(false);
        Report();
    }

    void MarkPartDone(Part part)
    {
        if (_phase != Phase.Assembling || part.IsDone || !IsInStep(part, _stepIndex))
            return;

        part.IsDone = true;
        Paint(part, HologramMaterials.Done);
        part.DoneButton.SetVisible(false);

        if (IsStepComplete(_stepIndex))
        {
            foreach (var other in _parts)
            {
                if (IsInStep(other, _stepIndex))
                    Paint(other, HologramMaterials.StepComplete);
            }

            var isFinalStep = _stepIndex >= steps.Length - 1;
            _nextButton.SetLabel(isFinalStep ? "FINISH" : "NEXT STEP");
            _nextButton.SetVisible(true);
        }

        Report();
    }

    void AdvanceStep()
    {
        if (_phase != Phase.Assembling || !IsStepComplete(_stepIndex))
            return;

        // Finished steps stay hidden so only the current instruction is on screen.
        foreach (var part in _parts)
        {
            if (IsInStep(part, _stepIndex))
            {
                Show(part, false);
                part.DoneButton.SetVisible(false);
            }
        }

        if (_stepIndex + 1 >= steps.Length)
        {
            _phase = Phase.Complete;
            _nextButton.SetVisible(false);
            Report();
            return;
        }

        EnterStep(_stepIndex + 1);
    }

    bool IsInStep(Part part, int index)
    {
        if (index < 0 || index >= steps.Length)
            return false;

        var names = steps[index].partNames;
        if (names == null)
            return false;

        foreach (var name in names)
        {
            if (name == part.Name)
                return true;
        }

        return false;
    }

    bool IsStepComplete(int index)
    {
        var any = false;
        foreach (var part in _parts)
        {
            if (!IsInStep(part, index))
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
        var completed = 0;
        for (var i = 0; i < _stepIndex; i++)
        {
            foreach (var part in _parts)
            {
                if (IsInStep(part, i))
                    completed++;
            }
        }

        var inCurrentStep = 0;
        var doneInCurrentStep = 0;
        foreach (var part in _parts)
        {
            if (!IsInStep(part, _stepIndex))
                continue;

            inCurrentStep++;
            if (part.IsDone)
                doneInCurrentStep++;
        }

        switch (_phase)
        {
            case Phase.Placing:
                _hud.SetProgress(0f, "Position the hologram, then press LOCK IN PLACE");
                break;
            case Phase.Assembling:
                completed += doneInCurrentStep;
                _hud.SetProgress(
                    total == 0 ? 0f : completed / (float)total,
                    $"Step {_stepIndex + 1} of {steps.Length} — {steps[_stepIndex].title}   ({doneInCurrentStep}/{inCurrentStep} cups)");
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
