# Six-cup demo: Unity contract

The existing model is `qwen3-vl:8b-instruct`, running locally on GB10 through
Ollama. No cloud calls or retraining. The correct finished reference is stored
in `cup_photos/reference.png`; the user confirmed it is Photo 3. The reference remains stored but is not read or sent during observation extraction.
Only the current snapshot is shown to the model.

## Current base PNG fixture

When the backend runs with `ASSIST_MODE=mock`, every valid PNG currently returns
the fixed message **“The right cup on the second row is upside down.”** The
response highlights `cup_5` and includes a `flipped` issue at `middle_right`.
This proves the PNG request, JSON response, message display, and hologram-ID
path. It does not analyze the pixels. The structured issue contract also reserves
`misaligned` so real detection can replace this fixture later.

## Steps

| step_index | Required visible arrangement |
| --- | --- |
| 0 | Bottom: three cups, openings down |
| 1 | Bottom plus middle: two middle cups, openings up, each bridging adjacent bottom cups |
| 2 | Full build: add one top cup, opening down, bridging both middle cups |

Cups are interchangeable. Identity, logo rotation, background, lighting, framing,
and placement order within each layer are not criteria. No target coordinates
or distances are invented. Future layers are ignored, even when absent or wrong.
Earlier required layers are always checked. Occluded evidence is not inferred.

## Requests unchanged

`GET /health` still returns `{"status":"ok"}`. Send `POST /assist` to
`http://10.50.19.61:8000` with the same request fields:

```json
{
  "request_id": "unity-request-1",
  "session_id": "demo-session",
  "instruction_id": "assembly-1",
  "step_index": 2,
  "placed_piece_ids": [],
  "question": "Evaluate the required layers.",
  "snapshot": {
    "mime_type": "image/png",
    "view_type": "physical_photo",
    "data_base64": "<raw Base64 of incoming JPEG/PNG>"
  }
}
```

`question` may be omitted/null. It remains accepted for compatibility but is not sent to the observation model.
Neither step rules nor placed IDs nor request metadata are sent to that model;
Python applies the requested step after extraction.
`placed_piece_ids` remains accepted for compatibility, but is not evidence of
placement or identity and is not used to assign cup identities. Prefer `[]`.
JPEG/PNG strict Base64/decode/MIME/size validation is unchanged. Cup step indices
must be 0–2; other instruction IDs/steps return HTTP 422.

## Successful responses: two added fields

Illustrative response shape (not a claim that the model passed a particular photo):

```json
{
  "request_id": "unity-request-1",
  "step_index": 2,
  "guidance": "Turn the middle cups so both openings face up.",
  "highlight_piece_ids": [],
  "issues": [],
  "audio_url": null,
  "mock": false,
  "status": "incorrect",
  "advance_step": false
}
```

`status` is `correct`, `incorrect`, or `uncertain`. Code derives it from validated
per-cup observations; it is not copied from model prose:

- `incorrect`: at least one required visible criterion is reported mismatched.
  A separate obscured criterion does not erase a visible error.
- `uncertain`: no decisive required error, but some required count/orientation/
  support cannot be established, or the scene is unclear/rendered only.
- `correct`: every required criterion is reported matched in a physical scene.

`advance_step` is **always false**, including when `status` is `correct`.
Keep Unity's manual confirmation control. A correct classification means only
that the extracted observations satisfy Python's rules; it is not verified
physical success. No session state or step index is changed by the backend.
Unity should still match request ID and step index before displaying a result.

## Observation-first evaluation

1. The local VLM describes the current snapshot only. Each visible cup has a
   layer, camera-relative left/center/right position, opening `up`/`down`/`unknown`,
   short `visual_evidence`, visible `supported_by` labels and `support_evidence`.
   It also reports scene type and each layer's visibility. It receives no target
   reference, target counts, desired orientations, or correctness question.
2. Strict schema validation rejects malformed observations. Python checks required
   counts, unique expected positions, orientations and support relationships for
   the current step. A visibly missing cup or known wrong orientation is incorrect;
   obscured/missing evidence, unknown direction/support or ambiguous location is
   uncertain. Both prevent acceptance. A known error takes precedence over other
   uncertainty. Future layers do not need to be complete.

Observation strings are audit evidence, not proof of truth: schema validation
cannot verify that a claimed rim/interior exists in the pixels. The measured run
still contains hallucinated orientations and cups. The public response fields
remain unchanged; raw per-cup observations are recorded in server logs, without
image Base64, and in the evaluation report.

`highlight_piece_ids` is always `[]` for interchangeable cups. `audio_url` stays
null. `guidance` is a short deterministic template from layer errors (at most two
fixes per response, plus an uncertainty note if needed). Untrusted model prose
is not returned as actionable guidance, so it cannot invent distances or IDs.
The VLM's underlying observations can still be wrong; see measured results.

## Running and teammate checks

```sh
ASSIST_MODE=vision .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
ASSIST_MODE=mock .venv/bin/python -m uvicorn main:app --host 0.0.0.0 --port 8000
python3 test_image.py cup_photos/photo-3.png --step-index 2 --expect-mode vision
python3 test_cup_photos.py --output cup-results.json
.venv/bin/python -m unittest discover -p 'test_*.py' -v
```

Use one server at a time. Model/URL/timeout settings are unchanged; see
`VISION_SETUP.md`. Mock mode explicitly returns `mock: true`, `status: uncertain`,
and `advance_step: false`. It does not imply physical success. Health remains a
liveness check. Model/timeout failures retain the JSON error contract.

The supplied Unity example adds `string status` and `bool advance_step` to its
response DTO, validates them and logs them. It does not advance automatically.
Set its request timeout above the backend deadline, for example 150 seconds for
a 120-second deadline. Existing Android Internet/local HTTP settings still apply.
No Unity compilation/headset test was performed here.

## Additional photos required for intermediate-step accuracy

- Step 0 positives: **only three bottom cups**, all openings down, no middle/top;
  several views, different logo rotations and cup permutations.
- Step 0 negatives: one/two cups missing, one or more bottom cups opening up.
- Step 1 positives: correct bottom and middle, **no top cup**; varied views.
- Step 1 negatives: missing middle cup, flipped middle cup(s), clearly misplaced
  middle support, and a wrong bottom cup beneath an otherwise correct middle.
- Step 2 isolated negatives: top missing, top flipped only, top not bridging;
  separate bottom-only and middle-only orientation faults.
- Uncertainty cases at each step: hidden/cropped required cups or supports, blur,
  ambiguous rim/base visibility, and passthrough with ghost overlays hiding cups.

Capture known ground truth and views exposing the support contacts. Full-build
photos do not establish partial-build accuracy. Unit tests prove step filtering
in code, not whether the VLM sees partial builds correctly. OpenShell integration
remains a required separate milestone; no speech or automatic progress. Local
MongoDB instruction storage is documented separately in `MONGODB_SETUP.md`.
