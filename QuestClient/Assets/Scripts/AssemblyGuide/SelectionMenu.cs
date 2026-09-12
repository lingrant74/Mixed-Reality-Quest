using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Startup chooser listing the assemblies the backend offers. Follows the head loosely so it
/// stays reachable without feeling nailed to the user's face.
/// <para>
/// It is created before the server has answered and shows loading progress in its own header,
/// then fills in rows once the documents arrive.
/// </para>
/// </summary>
public class SelectionMenu : MonoBehaviour
{
    // Far enough back that six rows plus the header fit inside a comfortable vertical sweep
    // while the text stays well above the headset's resolution limit.
    const float Distance = 0.92f;
    const float RowWidth = 0.52f;
    const float RowHeight = 0.088f;
    const float RowGap = 0.012f;
    const float PreviewBox = 0.066f;

    // TMP world-space font sizes, where one unit is about 0.11 m of cap height. At the menu's
    // distance these give roughly 108, 56 and 48 arcminutes of cap height, keeping the title
    // at least as prominent as the row names it sits above.
    const float TitleFontSize = 0.26f;
    const float StatusFontSize = 0.135f;
    const float DetailFontSize = 0.115f;

    static readonly Color RowIdle = new Color(0.10f, 0.26f, 0.46f);
    static readonly Color RowHover = new Color(0.20f, 0.72f, 1f);

    public class Entry
    {
        public AssemblySummaryDto Summary;

        /// <summary>Null when the document could not be fetched; the row then has no preview.</summary>
        public AssemblyDocumentDto Document;
    }

    public event Action<Entry> Chosen;

    readonly List<GameObject> _rows = new List<GameObject>();
    TextMeshPro _title;
    TextMeshPro _status;
    bool _done;

    Func<string, string, Mesh> _meshFor;
    float _partHeight;
    float _partDiameter;

    public static SelectionMenu Create(float partHeight, float partDiameter, Func<string, string, Mesh> meshFor)
    {
        var root = new GameObject("Assembly Selection Menu");
        var menu = root.AddComponent<SelectionMenu>();
        menu._partHeight = partHeight;
        menu._partDiameter = partDiameter;
        menu._meshFor = meshFor;
        menu.BuildHeader();
        return menu;
    }

    void BuildHeader()
    {
        _title = Text("Title", transform, Vector3.zero, new Vector2(RowWidth, 0.04f),
            TitleFontSize, new Color(0.62f, 0.88f, 1f), TextAlignmentOptions.Center);
        _title.text = "CHOOSE AN ASSEMBLY";

        _status = Text("Status", transform, Vector3.zero, new Vector2(RowWidth, 0.03f),
            StatusFontSize, new Color(0.72f, 0.76f, 0.82f), TextAlignmentOptions.Center);

        PlaceHeader(0);
    }

    /// <summary>Keeps the rows centred on the view, with the header sitting above them.</summary>
    void PlaceHeader(int rowCount)
    {
        var top = rowCount * (RowHeight + RowGap) * 0.5f;
        _title.transform.localPosition = new Vector3(0f, top + 0.058f, 0f);
        _status.transform.localPosition = new Vector3(0f, top + 0.026f, 0f);
    }

    public void SetStatus(string status)
    {
        if (_status != null)
            _status.text = status ?? string.Empty;
    }

    public void SetEntries(List<Entry> entries)
    {
        foreach (var row in _rows)
            Destroy(row);
        _rows.Clear();

        SetStatus($"{entries.Count} available");
        PlaceHeader(entries.Count);

        var top = entries.Count * (RowHeight + RowGap) * 0.5f;
        for (var i = 0; i < entries.Count; i++)
            _rows.Add(BuildRow(entries[i], i, top));
    }

    GameObject BuildRow(Entry entry, int index, float top)
    {
        var summary = entry.Summary;
        var name = string.IsNullOrEmpty(summary.name) ? summary.instruction_id : summary.name;

        var button = WorldButton.Create(
            $"Assembly Row {index + 1}",
            name,
            new Vector2(RowWidth, RowHeight),
            transform,
            RowIdle,
            RowHover,
            faceCamera: false);

        button.transform.localPosition = new Vector3(
            0f, top - index * (RowHeight + RowGap) - RowHeight * 0.5f, 0f);

        // Leave room on the left for the miniature and let the name wrap onto a second line,
        // which keeps the glyphs large instead of shrinking a long name onto one line.
        var textLeft = PreviewBox + 0.022f;
        var textWidth = RowWidth - textLeft - 0.016f;
        button.SetLabelArea(
            new Vector3((textLeft + textWidth * 0.5f) - RowWidth * 0.5f, 0.008f, -0.004f),
            new Vector2(textWidth, RowHeight * 0.52f),
            wrap: true,
            alignment: TextAlignmentOptions.Center);

        if (entry.Document != null)
        {
            AssemblyPreview.Create(
                entry.Document,
                button.transform,
                new Vector3(-RowWidth * 0.5f + PreviewBox * 0.5f + 0.012f, 0f, -0.004f),
                new Vector2(PreviewBox, RowHeight * 0.82f),
                _partHeight,
                _partDiameter,
                _meshFor);

            var parts = entry.Document.objects?.Length ?? 0;
            var steps = entry.Document.steps?.Length ?? 0;
            var detail = Text($"Detail {index + 1}",
                button.transform,
                new Vector3((textLeft + textWidth * 0.5f) - RowWidth * 0.5f, -RowHeight * 0.32f, -0.004f),
                new Vector2(textWidth, 0.02f),
                DetailFontSize,
                new Color(0.68f, 0.82f, 0.95f),
                TextAlignmentOptions.Center);
            detail.text = $"{parts} parts  ·  {steps} steps";
        }

        var captured = entry;
        button.Clicked += () => Choose(captured);
        return button.gameObject;
    }

    TextMeshPro Text(string name, Transform parent, Vector3 localPosition, Vector2 rect,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        var text = go.AddComponent<TextMeshPro>();
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.rectTransform.sizeDelta = rect;
        return text;
    }

    void Choose(Entry entry)
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
