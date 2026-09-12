using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Attach one instance to a scene GameObject. No camera capture or AI.
public class AssemblyBackendExample : MonoBehaviour
{
    public string serverUrl = "http://10.50.22.165:8000";
    public Texture2D testTexture;
    public bool encodeAsJpeg = false;
    [Range(1, 100)] public int jpegQuality = 85;
    public string sessionId = "unity-test-session";
    public string instructionId = "assembly-1";
    [Min(0)] public int stepIndex = 0;
    public string[] placedPieceIds = new string[0];
    public string question = "What do I do next?";
    public string viewType = "test_texture";
    [Min(1)] public int timeoutSeconds = 30;
    public bool checkHealthOnStart = true;

    public bool IsBusy { get; private set; }
    private UnityWebRequest activeRequest;

    [Serializable] public class Snapshot
    {
        public string mime_type;
        public string view_type;
        public string data_base64;
    }

    [Serializable] public class AssistRequest
    {
        public string request_id;
        public string session_id;
        public string instruction_id;
        public int step_index;
        public string[] placed_piece_ids;
        public string question;
        public Snapshot snapshot;
    }

    [Serializable] public class AssistResponse
    {
        public string request_id;
        public int step_index;
        public string guidance;
        public string[] highlight_piece_ids;
        public PlacementIssue[] issues;
        public string audio_url; // Backend returns JSON null in mock mode.
        public bool mock;
        public string status;
        public bool advance_step; // Temporarily always false; require manual confirmation.
    }

    [Serializable] public class PlacementIssue
    {
        public string piece_id; // cup_1 .. cup_6; map to the matching hologram.
        public string location;
        public string issue_type; // "misaligned" or "flipped".
        public string message;
    }

    [Serializable] public class HealthResponse { public string status; }

    private void Start()
    {
        if (checkHealthOnStart) CheckHealth();
    }

    [ContextMenu("Check Backend Health")]
    public void CheckHealth() { Begin(false); }

    [ContextMenu("Send Test Texture")]
    public void SendTestTexture() { Begin(true); }

    private void Begin(bool assist)
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            Debug.LogWarning("Backend example must be enabled in Play mode.", this);
            return;
        }
        if (IsBusy)
        {
            Debug.LogWarning("Backend request already in progress; request skipped.", this);
            return;
        }
        IsBusy = true; // Shared guard for health AND assist, before coroutine starts.
        StartCoroutine(Send(assist));
    }

    private AssistRequest BuildPayload()
    {
        if (testTexture == null || !testTexture.isReadable)
            throw new InvalidOperationException("Assign a test texture with Read/Write enabled.");
        if ((long)testTexture.width * testTexture.height > 20000000)
            throw new InvalidOperationException("Test texture exceeds backend's 20 megapixel limit.");
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(instructionId) ||
            stepIndex < 0 || string.IsNullOrEmpty(viewType) || viewType.Length > 100)
            throw new InvalidOperationException("Check sessionId, instructionId, stepIndex and viewType.");

        byte[] bytes = encodeAsJpeg ? testTexture.EncodeToJPG(jpegQuality) : testTexture.EncodeToPNG();
        if (bytes == null || bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024)
            throw new InvalidOperationException("Image encoding failed or exceeds backend's 10 MiB limit.");
        return new AssistRequest
        {
            request_id = Guid.NewGuid().ToString("N"),
            session_id = sessionId,
            instruction_id = instructionId,
            step_index = stepIndex,
            placed_piece_ids = placedPieceIds ?? new string[0],
            question = question,
            snapshot = new Snapshot
            {
                mime_type = encodeAsJpeg ? "image/jpeg" : "image/png",
                view_type = viewType,
                data_base64 = Convert.ToBase64String(bytes)
            }
        };
    }

    private IEnumerator Send(bool assist)
    {
        AssistRequest payload = null;
        try
        {
            // Inner catch reports setup/encoding/send failures; outer finally releases the guard.
            UnityWebRequestAsyncOperation operation;
            try
            {
                string root = (serverUrl ?? "").Trim().TrimEnd('/');
                Uri uri;
                if (!Uri.TryCreate(root, UriKind.Absolute, out uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                    throw new InvalidOperationException("Set a valid HTTP(S) server URL.");
                if (assist)
                {
                    payload = BuildPayload();
                    activeRequest = new UnityWebRequest(root + "/assist", "POST");
                    activeRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
                    activeRequest.downloadHandler = new DownloadHandlerBuffer();
                    activeRequest.SetRequestHeader("Content-Type", "application/json");
                }
                else activeRequest = UnityWebRequest.Get(root + "/health");
                activeRequest.timeout = Mathf.Max(1, timeoutSeconds);
                operation = activeRequest.SendWebRequest();
            }
            catch (Exception ex)
            {
                Debug.LogError("Backend request could not start: " + ex.Message, this);
                yield break;
            }

            yield return operation;
            string body = activeRequest.downloadHandler.text;
            if (activeRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Backend {activeRequest.result}: HTTP {activeRequest.responseCode}; " +
                    $"{activeRequest.error}\nResponse body: {body}", this);
                yield break;
            }

            try
            {
                if (assist)
                {
                    AssistResponse response = JsonUtility.FromJson<AssistResponse>(body);
                    if (response == null || response.request_id != payload.request_id ||
                        response.step_index != payload.step_index || response.guidance == null ||
                        response.highlight_piece_ids == null || response.issues == null)
                        throw new InvalidOperationException("Response fields/identifiers do not match the request contract.");
                    if ((response.status != "correct" && response.status != "incorrect" && response.status != "uncertain") ||
                        response.advance_step)
                        throw new InvalidOperationException("Invalid cup status/advance recommendation.");
                    foreach (PlacementIssue issue in response.issues)
                        if (issue == null || string.IsNullOrEmpty(issue.piece_id) ||
                            string.IsNullOrEmpty(issue.location) ||
                            (issue.issue_type != "misaligned" && issue.issue_type != "flipped") ||
                            string.IsNullOrEmpty(issue.message))
                            throw new InvalidOperationException("Invalid placement issue in backend response.");
                    Debug.Log($"Assist request={response.request_id}, step={response.step_index}, mock={response.mock}, status={response.status}, advance_step={response.advance_step}\n" +
                        $"Guidance: {response.guidance}\nHighlights: [{string.Join(", ", response.highlight_piece_ids)}]\n" +
                        $"Issues: {string.Join(" | ", Array.ConvertAll(response.issues, issue => issue.location + ": " + issue.issue_type))}\n" +
                        $"Audio URL: {response.audio_url ?? "null"}\nRaw response: {body}", this);
                }
                else
                {
                    HealthResponse response = JsonUtility.FromJson<HealthResponse>(body);
                    if (response == null || response.status != "ok")
                        throw new InvalidOperationException("Unexpected health response.");
                    Debug.Log("Backend health: " + response.status + "\n" + body, this);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Backend response parse error: {ex.Message}\nResponse body: {body}", this);
            }
        }
        finally
        {
            if (activeRequest != null) activeRequest.Dispose();
            activeRequest = null;
            IsBusy = false;
        }
    }

    private void OnDisable()
    {
        if (activeRequest != null) activeRequest.Abort();
        StopAllCoroutines();
        if (activeRequest != null) activeRequest.Dispose();
        activeRequest = null;
        IsBusy = false;
    }
}
