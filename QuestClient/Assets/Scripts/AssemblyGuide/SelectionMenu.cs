using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Startup chooser listing the assemblies the backend offers. Follows the head loosely so it
/// stays reachable without feeling nailed to the user's face.
/// </summary>
public class SelectionMenu : MonoBehaviour
{
    const float Distance = 0.75f;
    const float RowHeight = 0.052f;
    const float RowGap = 0.010f;

    static readonly Vector2 RowSize = new Vector2(0.34f, RowHeight);
    static readonly Color RowIdle = new Color(0.10f, 0.26f, 0.46f);
    static readonly Color RowHover = new Color(0.20f, 0.72f, 1f);

    public event Action<AssemblySummaryDto> Chosen;

    readonly List<WorldButton> _rows = new List<WorldButton>();
    InfoPanel _title;
    bool _done;

    public static SelectionMenu Create(AssemblySummaryDto[] entries)
    {
        var root = new GameObject("Assembly Selection Menu");
        var menu = root.AddComponent<SelectionMenu>();
        menu.Build(entries);
        return menu;
    }

    void Build(AssemblySummaryDto[] entries)
    {
        var total = entries.Length * (RowHeight + RowGap);
        var top = total * 0.5f;

        _title = InfoPanel.Create("Menu Title", transform, new Vector2(RowSize.x, 0.05f),
            new Color(0.04f, 0.06f, 0.10f));
        _title.transform.localPosition = new Vector3(0f, top + 0.045f, 0f);
        _title.SetText("CHOOSE AN ASSEMBLY", string.Empty);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var button = WorldButton.Create(
                $"Assembly Row {i + 1}",
                string.IsNullOrEmpty(entry.name) ? entry.instruction_id : entry.name,
                RowSize,
                transform,
                RowIdle,
                RowHover,
                faceCamera: false);
            button.transform.localPosition = new Vector3(0f, top - i * (RowHeight + RowGap) - RowHeight * 0.5f, 0f);
            button.Clicked += () => Choose(entry);
            _rows.Add(button);
        }
    }

    void Choose(AssemblySummaryDto entry)
    {
        if (_done)
            return;

        _done = true;
        Chosen?.Invoke(entry);
        Destroy(gameObject);
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        var head = camera.transform;
        var target = head.position + head.forward * Distance;
        transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));

        var forward = transform.position - head.position;
        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
