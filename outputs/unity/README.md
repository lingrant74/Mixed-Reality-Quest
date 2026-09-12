# Unity local backend example

The backend now evaluates the six-cup demo. The example DTO also parses
`status`, structured `issues`, hologram highlight IDs, and `advance_step`;
these are logged, not acted on automatically.
See `../backend/CUP_DEMO.md` for the current contract and step indices 0–2,
and `../backend/CUP_OBSERVATION_RESULTS.md` for measured model errors. Require manual
confirmation: `advance_step` is always false, even for `status: correct`.
For the current base integration, a valid PNG in mock mode always returns “The
right cup on the second row is upside down,” with `cup_5` highlighted and a
`flipped` issue. This is a fixed transport/UI fixture, not image analysis.

1. Copy `AssemblyBackendExample.cs` into your Unity project's `Assets/Scripts`.
   Attach it to one empty GameObject. Uses built-in JsonUtility and UnityWebRequest;
   no additional packages. Intended for Unity 2022.3/Unity 6.
2. Import a small supplied PNG/JPEG (for example 512 x 512). In its texture import
   settings choose Default type, enable **Read/Write**, disable compression
   (including Android overrides), and Apply. Use a non-HDR RGB/RGBA texture.
   Drag it into **Test Texture** on the component. PNG is the default outgoing
   encoding; enable **Encode As Jpeg** to send JPEG regardless of the source file extension.
3. Keep **Server Url** at `http://10.50.19.61:8000` or change it to the Dell's
   current LAN address. Enter Play mode: health runs once and logs `ok`.
4. Open the component's context menu and select **Send Test Texture**. Expect
   the assessment guidance (fixed guidance in mock mode), echoed request ID/step, empty highlights, null audio,
   and the selected mode's `mock` flag in the Console. **Check Backend Health** repeats health.
   For headset testing, wire Unity UI Button OnClick events to the public
   `CheckHealth()` and `SendTestTexture()` methods.

One shared per-component busy guard skips overlapping calls to either endpoint.
Use one instance. Requests default to 30 seconds; set the Inspector timeout to 150 seconds for the
backend’s 120-second model deadline. Completion,
failure, or component disabling releases the guard. Disabling aborts the request.
Networking uses a coroutine; texture encoding itself runs on the main thread,
so keep the initial test texture small. No retries or automatic step progression.

The request DTO uses the exact backend snake_case fields, including nested
`snapshot.mime_type`, `view_type`, and raw `data_base64`. `question` is optional
on the backend; this sample sends the Inspector string (empty is also valid).
The response text field is **guidance**, not message. It includes `audio_url`
as a nullable string. Payload is JSON, not multipart/form-data. The script never
logs outgoing Base64. Backend image limits are 10 MiB and 20 megapixels.

## Android / Quest local HTTP

In **Project Settings > Player > Android > Other Settings > Configuration**:

- Set **Internet Access** to **Require** so the app includes INTERNET permission.
- For a Development Build, set **Allow downloads over HTTP** to
  **Allowed in Development Builds**. For a non-development local demo build,
  use **Always Allowed**. This setting also applies to UnityWebRequest traffic.

Preserve the project's existing Meta/Android setup. If custom manifests or
network security configuration override these settings, have the teammate check
the merged manifest's INTERNET permission and cleartext HTTP policy; don't
replace the Meta manifest. This example requires no camera permission.

Quest and Dell must be on a LAN that permits device-to-device traffic, with TCP
8000 reachable on Dell. Campus/guest Wi-Fi can isolate clients. Test
`http://10.50.19.61:8000/health` from the teammate's machine first. Quest must
use the Dell IP, not localhost or 0.0.0.0. Native Unity Android requests do not
need browser CORS setup; this is not a WebGL example.

## Teammate verification

- Send PNG, then JPEG; verify mock response and matching step index.
- Double-click Send while a request is pending; expect a skipped-request log.
- Set a closed port to check connection error/timeout logging, then restore URL.
- To check HTTP body logging, temporarily set Server Url to
  `http://10.50.19.61:8000/not-a-route`; health should log HTTP 404 and its JSON
  body. Restore the URL afterward. HTTP 422 bodies are logged the same way.
- Repeat health and image submission on Quest.

Schema fields were checked against `backend/main.py`. No Unity Editor or C#
compiler was available in this workspace, so this example has not been compiled
or run in Unity/Quest. Camera capture is a separate task. AI inference stays on the Dell; no MongoDB added.

References:
- [Unity Android Player settings](https://docs.unity3d.com/6000.0/Documentation/Manual/class-PlayerSettingsAndroid.html)
- [Unity PNG encoding requirements](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ImageConversion.EncodeToPNG.html)

## Stored assembly instructions

The backend now provides `GET /instructions` and `GET /instructions/{instruction_id}` from local MongoDB. See `../backend/MONGODB_SETUP.md` for JSON examples and error handling. This example still handles health/assist only; instruction-fetch UI is not implemented. Manual confirmation remains required.
