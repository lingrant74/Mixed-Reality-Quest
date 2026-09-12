# Verification — 2026-09-12

## Hardware and installation

- ARM64 Dell host; `nvidia-smi` confirmed NVIDIA GB10, driver 580.173.02, CUDA 13.0.
- GPU access initially failed inside the restricted execution environment.
  After execution permissions changed, GPU discovery and inference succeeded.
- No pre-existing Ollama executable, running service, or model cache was found
  in the inspected standard locations.
- Installed official Ollama 0.34.0 in the workspace and downloaded
  `qwen3-vl:8b-instruct`, Q4_K_M, model digest
  `0533d74300e4f9bc367d675d4e64ffd073d50ff16a2b4096cc2e8a1cf8c96319`.
- Ollama logged `Ollama cloud disabled: true`; `ollama ps` reported **100% GPU**,
  4096 context, approximately 5.8 GB loaded. No cloud login/inference was used.

## Real image tests

Input: the user's local 1920 x 1200 PNG screenshot of the GitHub repository.
It is a screen image without physical assembly blocks.

| Request | Measured wall-clock latency | Result |
| --- | ---: | --- |
| Direct Ollama smoke test, cold model | 41.422 s | Correctly identified a GitHub screen capture, not physical objects |
| Final `/assist`, warm model | 4.727 s | Explicit uncertainty, screenshot identified, empty highlights, `mock: false` |
| `/assist`, another question, same image | 1.623 s | Identified screen view and lack of physical evidence, `mock: false` |
| Explicit mock mode on a temporary test port | 0.051 s | Original mock guidance, `mock: true` |

Cold model loading accounted for 18.114 s of the direct test. Warm requests may
benefit from model/prompt/image caching; these are individual observations, not
a latency benchmark or promise for new images.

The first integration attempt misclassified screenshot evidence while giving a
generic assembly hint (4.363 s). The internal JSON schema and prompt were changed
to classify evidence before guidance. Both subsequent screenshot tests returned
uncertainty correctly. This is why semantic accuracy remains explicitly
unverified beyond the observed cases; structured validation is not a proof of
visual correctness.

Final response example:

```json
{
  "request_id": "test-request-1",
  "step_index": 0,
  "guidance": "Uncertain: the image does not establish clear physical placement; rendered targets are not physical blocks. You're viewing a GitHub repository page, not a physical building block setup. Unity must validate placement. Confirm the red block's position in Unity before proceeding.",
  "highlight_piece_ids": [],
  "audio_url": null,
  "mock": false
}
```

## Failure and contract checks

All **14** unittest cases passed, covering field/ID preservation, explicit mock
selection, uncertainty handling, extra-action/unknown-highlight rejection,
malformed output, busy requests, responsive health, image/context rejection,
local-only configuration, model failures, and the total request deadline.
These deterministic tests substitute Ollama responses only inside test code.

Additional checks exercised the API handler against an actual closed local port
(503 `model_unavailable`), a nonexistent model in the real Ollama server
(503 `model_not_installed`), and an intentionally tiny real adapter deadline
(504 `model_timeout`). Each returned JSON with its code, message and request ID;
none returned a mock success. The separate mock test server was stopped afterward.

## Current state and remaining work

FastAPI was left running in vision mode on `0.0.0.0:8000`; Ollama on
`127.0.0.1:11434` with cloud features disabled. Processes are foreground sessions,
not installed boot services. See `VISION_SETUP.md` for restart commands.

The Unity success contract is unchanged. Existing Unity clients need a timeout
long enough for cold inference (recommend 150 s with the 120 s backend deadline).
No Unity/headset test, real physical-block photo, occlusion scene, or mixed
passthrough/ghost overlay scene was available for this verification.
The predefined five-block context is provisional and must be aligned with Unity.

**OpenShell integration remains a required next milestone.** It is not implemented
by this direct local Ollama integration. No speech, MongoDB, or automatic step
advancement was added. These changes are local and have not been pushed to GitHub.
