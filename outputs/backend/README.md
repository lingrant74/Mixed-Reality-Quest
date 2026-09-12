# Local six-cup assembly evaluator

Local FastAPI service with observation-first cup evaluation and explicit mock
mode. See [the updated Unity JSON contract](CUP_DEMO.md). [Local MongoDB](MONGODB_SETUP.md) stores assembly instructions only; no incoming image storage or cloud inference. Images provide advisory
hints only; manual confirmation is mandatory and `advance_step` is always false.
See [vision setup and configuration](VISION_SETUP.md) for Ollama startup,
local-only restrictions, error responses, and the required OpenShell milestone.

## Install and start

Run these commands from this backend directory (Python 3.10+):

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
ASSIST_MODE=vision .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

Unity should use `http://<BACKEND-MACHINE-LAN-IP>:8000` on the same local
network. `0.0.0.0` is a bind address, not a client destination. On Quest,
`localhost` refers to the headset. Allow inbound TCP 8000 if the machine's
firewall blocks it; Android/Unity builds must permit cleartext HTTP for this
local demo. Headset connectivity needs a separate device test.

## API

- `GET /instructions` lists assemblies; `GET /instructions/{instruction_id}` returns the full document. See [MongoDB setup, responses and verification](MONGODB_SETUP.md).
- `GET /health` returns `{"status":"ok"}`.
- `POST /assist` accepts the payload below. `question` can be omitted or null.
- Interactive API documentation: `http://127.0.0.1:8000/docs`.

```json
{
  "request_id": "request-1",
  "session_id": "session-1",
  "instruction_id": "assembly-1",
  "step_index": 0,
  "placed_piece_ids": [],
  "question": "What do I do next?",
  "snapshot": {
    "mime_type": "image/png",
    "view_type": "headset",
    "data_base64": "<raw Base64 image bytes>"
  }
}
```

Use raw Base64 without a `data:image/...;base64,` prefix or line breaks.
Supported MIME types are `image/jpeg` and `image/png`; the decoded image must
match that type, decode successfully, and fit within 10 MiB and 20 megapixels.
Invalid payloads/images return HTTP 422. `step_index` is a zero-based,
integer 0, 1 or 2 for the cup demo. `view_type` is a caller-defined label, such as `headset`.

Successful responses echo `request_id` and `step_index`. This example shows
explicit `ASSIST_MODE=mock`; real vision responses use deterministic guidance from model observations and
`mock: false`, with the same fields:

```json
{
  "request_id": "request-1",
  "step_index": 0,
  "guidance": "Mock mode: cup placement has not been evaluated.",
  "highlight_piece_ids": [],
  "audio_url": null,
  "mock": true,
  "status": "uncertain",
  "advance_step": false
}
```

## Test a local image

With the server running, in another terminal:

```sh
curl http://127.0.0.1:8000/health
python3 test_image.py /absolute/path/to/image.png
python3 test_image.py /absolute/path/to/image.jpg --url http://192.168.1.100:8000
python3 test_image.py /absolute/path/to/image.png --expect-mode mock
```

The test script sends the image, checks the response contract, and prints
round-trip latency. It defaults to expecting vision mode. It does not require
third-party Python packages.

## Verification

See [current observation-first results](CUP_OBSERVATION_RESULTS.md). [Earlier block-model smoke tests](VERIFICATION.md) are historical.

Verified locally with a running Uvicorn server: health, a PNG screenshot, a
generated JPEG, omitted/null question, and response identifiers. Malformed
Base64, non-image bytes, truncated JPEG, empty data, MIME mismatch, unsupported
MIME type, missing required fields, and negative/boolean step indices returned
HTTP 422. Unity/Quest and LAN connectivity have not been tested.

`requirements-lock.txt` records the exact dependency versions used for these
checks; install it instead of `requirements.txt` to reproduce that environment.
