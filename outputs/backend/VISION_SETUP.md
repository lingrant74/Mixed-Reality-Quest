# Local vision setup

All runtime inference is on the GB10. Ollama listens on loopback; Unity connects
only to FastAPI on port 8000. No cloud fallback, speech, or automatic
step advancement. Local MongoDB now stores instructions separately; see `MONGODB_SETUP.md`. **OpenShell integration remains a required next milestone**;
this direct Ollama smoke test does not fulfill it.

## Ollama

The official ARM64 runtime was extracted into `work/ollama-runtime` and model
weights are in `work/ollama-models`, relative to this task's workspace root.
From that root, start the installed runtime with:

```sh
OLLAMA_NO_CLOUD=1 OLLAMA_HOST=127.0.0.1:11434 \
  OLLAMA_MODELS="$PWD/work/ollama-models" OLLAMA_NUM_PARALLEL=1 \
  work/ollama-runtime/bin/ollama serve
```

For a fresh machine, install the official ARM64 Ollama distribution first.
Do not start a second Ollama process if one is already listening on that port.
If using a system Ollama service, set `OLLAMA_NO_CLOUD=1` in that service's
environment and restart it; setting the variable only on FastAPI is insufficient.

In another terminal:

```sh
work/ollama-runtime/bin/ollama pull qwen3-vl:8b-instruct
python3 outputs/backend/smoke_vision.py /absolute/path/to/local-image.png
work/ollama-runtime/bin/ollama ps
```

The model download requires internet access; image inference does not. Verify
the server log says `Ollama cloud disabled: true` and `ollama ps` shows GPU use.
Ollama initializes its normal `~/.ollama` local key/config directory; no cloud
login is needed. The large runtime and model weights are not source deliverables.

## Backend modes

From the backend directory, install requirements in your virtual environment.
For real inference:

```sh
ASSIST_MODE=vision OLLAMA_MODEL=qwen3-vl:8b-instruct \
  OLLAMA_URL=http://127.0.0.1:11434 OLLAMA_TIMEOUT_SECONDS=120 \
  .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

For explicit teammate mock testing:

```sh
ASSIST_MODE=mock .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

Restart FastAPI when changing mode/configuration. Default mode is `vision`.
Use one Uvicorn worker for this MVP's one-at-a-time inference guard.
`/health` remains a liveness check returning `{"status":"ok"}`; it does not
load the model or guarantee model readiness. An `/assist` request tests readiness.

The local URL is configurable but deliberately restricted to a loopback HTTP
origin, so inference cannot move to another machine. The adapter disables proxy
environment use and redirects, rejects cloud names/remote model metadata, and
checks that the installed model supports vision. It never downloads a model
while handling `/assist` and never silently falls back to mock.

## Cup contract and validation

See [the current six-cup Unity contract](CUP_DEMO.md). The cup evaluator replaces
five-block guidance. It preserves the request fields and existing successful
response fields, and adds `status` and `advance_step`.

The confirmed reference stays at `cup_photos/reference.png`. The retained
`CUP_REFERENCE_PATH` setting is unused by this experiment: no reference is loaded
or sent. Each model call receives only the incoming snapshot and a generic
observation prompt, without step rules, placed IDs or question text.

The model returns strict per-cup observations, including visual support for each
orientation. Python evaluates only required layers and emits bounded actionable
templates. `advance_step` is always false; manual confirmation is mandatory even
when status is correct. No model-generated free text or IDs are passed directly
to Unity. Observations are retained in server logs for evaluation, without image
Base64. Semantic image interpretation can still be wrong. See
[CUP_OBSERVATION_RESULTS.md](CUP_OBSERVATION_RESULTS.md) for actual failures.

Other model errors keep JSON `detail` with `code`, `message`, and `request_id`:
HTTP 422 for invalid input/context, 503 for unavailable/busy models, 504 for
model timeout, and 502 for upstream/invalid model output. Model URLs remain
loopback-only; there is no cloud or silent mock fallback.

## Tests

```sh
.venv/bin/python -m unittest discover -p 'test_*.py' -v
python3 test_image.py /absolute/path/to/image.png --step-index 2 --expect-mode vision --timeout 180
python3 test_image.py /absolute/path/to/image.png --expect-mode mock
```

The image client prints HTTP round-trip latency. Set Unity's existing configurable
request timeout above the backend's deadline (for example 150 seconds for a
120-second deadline), so Unity can receive the backend's JSON timeout response.

References: [Ollama ARM64 install](https://docs.ollama.com/linux),
[chat API](https://docs.ollama.com/api/chat),
[local-only mode](https://docs.ollama.com/faq#how-do-i-disable-ollama-cloud-features),
[model](https://ollama.com/library/qwen3-vl:8b-instruct).
