# Local six-cup assembly evaluator

Local FastAPI service with observation-first cup evaluation and explicit mock
mode, plus a local manufacturer **dashboard UI** for authoring, previewing, and
publishing assembly instructions for any GLB product model (not just the cup
demo). See [the updated Unity JSON contract](CUP_DEMO.md). [Local MongoDB](MONGODB_SETUP.md)
stores assembly instructions, dashboard product drafts/published versions, and
uploaded asset metadata; there is no incoming image storage or cloud inference.
Images provide advisory hints only; manual confirmation is mandatory and
`advance_step` is always false. See [vision setup and configuration](VISION_SETUP.md)
for Ollama startup, local-only restrictions, error responses, and the required
OpenShell milestone.

## Install and start

Run these commands from this backend directory (Python 3.10+):

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
ASSIST_MODE=vision .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

`ASSIST_MODE=mock` works instead of `vision` if you don't need `/assist` (the
dashboard, `/instructions`, and publishing don't use Ollama at all). Before
starting the server you also need MongoDB running — see
[Manufacturer dashboard (UI)](#manufacturer-dashboard-ui) below.

Unity should use `http://<BACKEND-MACHINE-LAN-IP>:8000` on the same local
network. `0.0.0.0` is a bind address, not a client destination. On Quest,
`localhost` refers to the headset. Allow inbound TCP 8000 if the machine's
firewall blocks it; Android/Unity builds must permit cleartext HTTP for this
local demo. Headset connectivity needs a separate device test.

## Manufacturer dashboard (UI)

The dashboard at **`http://127.0.0.1:8000/dashboard`** is where a manufacturer
uploads a GLB model, previews it in 3D, defines ordered assembly steps, maps
model parts to each step, and publishes the result for Unity/Quest to consume.
It is a static page (Three.js for the 3D preview) served by the same FastAPI
app — no separate frontend server or build step.

**Prerequisites:**

1. **MongoDB** running locally (drafts, published versions, and asset metadata
   are stored there). See [MONGODB_SETUP.md](MONGODB_SETUP.md) for full detail;
   the short version, from this backend directory:
   ```sh
   ./mongodb-local.sh start
   ```
2. **Node.js**, used only to run the bundled Khronos glTF validator against
   each uploaded `.glb`. If you already have Node installed, point the backend
   at it:
   ```sh
   export DASHBOARD_NODE=$(command -v node)
   ```
   Otherwise, run the one-time setup script to download a local Node runtime
   and the `three`/`gltf-validator` packages (the latter two are already
   vendored in this repo, so it only fetches Node):
   ```sh
   .venv/bin/python setup_dashboard_dependencies.py
   ```

**Start the backend** (from this directory):

```sh
MONGODB_URI=mongodb://127.0.0.1:27017 MONGODB_DATABASE=assembly_assistant \
  ASSIST_MODE=mock .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

Then open **http://127.0.0.1:8000/dashboard** in a browser.

**Workflow in the UI:**

1. **Create product** → gives you an empty draft.
2. **Upload GLB** → opens a website file list rather than the Mac file
   picker. Click **Upload** beside `stacked-red-cups-demo.glb`; a short progress
   bar confirms the action, then the bundled six-part demo and its six editable
   assembly steps are attached. **Next** and slider states 0–6 work immediately;
   source CAD is still required before publishing. More
   model-library sources can be implemented later. A local file can still be
   dragged directly onto the Product Model preview. Every model is validated (binary glTF 2.0, embedded resources only, no
   Draco/Meshopt/KTX2/required extensions, ≤2,000 nodes, ≤50 MiB) and rendered
   in the 3D preview; each mesh part gets a stable ID (`part_<modelVersion>_<nodeIndex>`).
3. **Upload source CAD** → the manufacturer's authoritative design file
   (`.f3z`/`.f3d`, ≤300 MiB). It is stored and made downloadable as-is —
   never parsed or rendered — and is **required before publishing**. Adding
   new source formats only means adding an entry to `SOURCE_CAD_FORMATS` in
   `dashboard_api.py`; no schema change needed.

   **MVP note:** uploading a source file does not actually parse it. It calls
   `generate_hardcoded_fusion_instructions()` in `dashboard_api.py`, which
   always emits the same fixed six-cup, 3-2-1 pyramid sequence into the draft,
   replacing whatever steps were there. The bottom three and top cup are
   upright, while both middle cups are inverted. The middle cups depend on
   their two neighboring bottom cups and the top cup depends on both middle
   cups. If the current model already has exactly six detected GLB
   parts, those six parts are mapped onto the six steps in detection order
   automatically; otherwise every step is left unmapped and flagged for
   review — the app never invents a mapping for the wrong part count.
   If the GLB is uploaded *after* the source (source-first), the same
   auto-mapping is retried once the GLB arrives, as long as you haven't
   manually edited the generated steps in the meantime. This is a clearly
   isolated placeholder for a future real Fusion/CAD parser; nothing else in
   the dashboard, manifest, viewer, or publish pipeline needs to change when
   that parser replaces it.

   `dashboard-demo-6cup.glb` is the reusable MVP render asset. It contains real
   open cup geometry with six nodes (`Cup_1`..`Cup_6`) in the corrected 3-2-1
   arrangement: bottom cups upright, middle cups inverted, and top cup upright.
   Its node transforms and mesh data live in the GLB itself, so the same model
   works outside the dashboard rather than relying on browser-only replacement
   geometry. `dashboard-demo.glb` remains a separate three-part validation
   fixture. GLBs with the wrong part count are still left unmapped for review.
4. **Review/edit steps** — instruction text, orientation text, opening
   direction (`up`/`down`, required by the current Unity schema), the parts
   introduced in that step, and optional supporting parts from earlier steps.
5. **Open Assembly Viewer** (button under the 3D preview, enabled once a GLB
   is uploaded) — a modal step-through viewer over the *draft* steps. A
   slider (`0` = nothing assembled, `N` = fully assembled) shows every part
   introduced by steps `1..K` cumulatively, highlights the part(s) introduced
   by the current step, and never hides non-mesh nodes (cameras, lights,
   groups). Previous/Next stay in sync with the slider and disable at the
   ends. The camera frames the full model once on open and is **not** reset
   on slider/Previous/Next moves — only the "Reset view" button does that.
   Moving the slider is pure client-side visualization; it never calls
   save or publish.
6. **Save draft** as often as you like — drafts can be incomplete and are
   **never** visible to Unity.
7. **Publish instructions** — validates the draft against the Unity schema
   (name, description, GLB model, source CAD file, and every step's
   instructions/orientation/opening direction/parts must be present and
   self-consistent) and, only if valid, atomically replaces the previously
   published version. A failed publish leaves the prior published version
   untouched, with a specific, human-readable error (e.g. "Step 2: add
   instructions, orientation, opening direction and selected parts.").

**Replacing the GLB or the source CAD file** bumps the model version and
never silently reassigns existing step mappings — every step is flagged
("Model mappings need review") and its part dropdowns keep the stale
selection labeled "Previous model part — reselect" until you manually fix it;
publishing is blocked until you do. Replacing one file preserves the other
(re-uploading the GLB keeps the existing source reference, and vice versa).
Note that re-uploading the *source* file always regenerates the hardcoded
six-cup draft steps (per the MVP behavior above), so a manually customized
draft will be replaced if you re-upload a source file after editing it.

**Unity/tooling-facing endpoints for a published product** (draft state is
never exposed on these):

```sh
curl http://127.0.0.1:8000/instructions                        # includes published products
curl http://127.0.0.1:8000/instructions/product_<product_id>    # full instruction document (Unity schema, unchanged)
curl http://127.0.0.1:8000/instructions/product_<product_id>/assets    # model/source/render URLs
curl http://127.0.0.1:8000/instructions/product_<product_id>/manifest  # source-CAD/render/parts/instructions manifest
```

The `/manifest` response follows a separate, richer schema
(`schema_version`, `product_id`, `model_version`, `assets.source_cad`,
`assets.render_model`, `parts[]`, `instructions[]`) that makes the
part/instruction/CAD-source relationships explicit for tooling; it does not
replace or alter the Unity-facing `AssemblyDocument` schema returned by
`/instructions/{id}` itself.

To verify the whole draft/publish flow end-to-end against a running server:

```sh
.venv/bin/python verify_dashboard.py
```

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
Dashboard draft/publish end-to-end checks, including model replacement,
stale-mapping review, the hardcoded six-cup Fusion demo generator (source
arriving before or after the GLB, and a wrong-part-count GLB never being
silently mapped), are recorded in [DASHBOARD_VERIFICATION.json](DASHBOARD_VERIFICATION.json)
and were re-verified through the actual browser UI: upload, 3D preview,
step/part mapping, save, invalid-publish rejection, publish, draft edits after
publish, a full backend restart, model replacement, blocked republish, and the
Assembly Viewer's cumulative slider/highlighting/camera-persistence behavior
all behave as documented above. `dashboard-demo-6cup.glb` is a synthetic
six-node box fixture with a demo-only procedural cup visualization — **no real six-cup
"Stacked Cup Assembly" GLB export exists anywhere in this project**; see the
note in the dashboard section above.

Verified locally with a running Uvicorn server: health, a PNG screenshot, a
generated JPEG, omitted/null question, and response identifiers. Malformed
Base64, non-image bytes, truncated JPEG, empty data, MIME mismatch, unsupported
MIME type, missing required fields, and negative/boolean step indices returned
HTTP 422. Unity/Quest and LAN connectivity have not been tested.

`requirements-lock.txt` records the exact dependency versions used for these
checks; install it instead of `requirements.txt` to reproduce that environment.
