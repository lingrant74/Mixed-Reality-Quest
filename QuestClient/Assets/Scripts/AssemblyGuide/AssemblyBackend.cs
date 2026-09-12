using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Data transfer objects mirroring the backend's snake_case instruction schema exactly, so
/// Unity's built-in JsonUtility can read them without any extra packages. Unknown fields such
/// as <c>_id</c> are ignored by JsonUtility.
/// </summary>
[Serializable]
public class AssemblyObjectDto
{
    public string slot_id;
    public string object_type;
    public int step_index;
    public string required_opening_direction;
    public string[] supporting_slot_ids;
    public string placement_instructions;
}

[Serializable]
public class AssemblyStepDto
{
    public int step_index;
    public string instructions;

    /// <summary>Cumulative: every slot that must be present by the end of this step.</summary>
    public string[] required_slot_ids;

    public string target_image_path;
}

[Serializable]
public class AssemblyDocumentDto
{
    public string instruction_id;
    public int version;
    public string name;
    public string description;
    public AssemblyObjectDto[] objects;
    public AssemblyStepDto[] steps;
}

[Serializable]
public class AssemblySummaryDto
{
    public string instruction_id;
    public int version;
    public string name;
    public string description;
}

/// <summary>JsonUtility cannot read a top-level JSON array, so the response is wrapped.</summary>
[Serializable]
class AssemblySummaryList
{
    public AssemblySummaryDto[] items;
}

/// <summary>Read-only calls against the local assembly backend.</summary>
public static class AssemblyBackend
{
    public static IEnumerator ListInstructions(
        string baseUrl,
        float timeoutSeconds,
        Action<AssemblySummaryDto[]> onSuccess,
        Action<string> onError)
    {
        var url = $"{baseUrl.TrimEnd('/')}/instructions";

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{request.result} ({request.responseCode}) {request.error}");
                yield break;
            }

            AssemblySummaryList parsed = null;
            string failure = null;
            try
            {
                parsed = JsonUtility.FromJson<AssemblySummaryList>(
                    "{\"items\":" + request.downloadHandler.text + "}");
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }

            if (failure != null)
                onError?.Invoke($"could not parse response: {failure}");
            else if (parsed?.items == null || parsed.items.Length == 0)
                onError?.Invoke("server listed no assemblies");
            else
                onSuccess?.Invoke(parsed.items);
        }
    }

    public static IEnumerator GetInstruction(
        string baseUrl,
        string instructionId,
        float timeoutSeconds,
        Action<AssemblyDocumentDto> onSuccess,
        Action<string> onError)
    {
        var url = $"{baseUrl.TrimEnd('/')}/instructions/{UnityWebRequest.EscapeURL(instructionId)}";

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{request.result} ({request.responseCode}) {request.error}");
                yield break;
            }

            AssemblyDocumentDto document = null;
            string failure = null;
            try
            {
                document = JsonUtility.FromJson<AssemblyDocumentDto>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }

            if (failure != null)
                onError?.Invoke($"could not parse response: {failure}");
            else if (document?.steps == null || document.steps.Length == 0 || document.objects == null)
                onError?.Invoke("response contained no steps or objects");
            else
                onSuccess?.Invoke(document);
        }
    }
}
