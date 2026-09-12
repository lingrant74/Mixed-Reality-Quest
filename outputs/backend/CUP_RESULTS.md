> Historical reference-based pipeline. Superseded by [observation-first results](CUP_OBSERVATION_RESULTS.md); advancement is now disabled for all assessments.

# Cup results — 2026-09-12

**Implementation is complete, but the current model is not reliable enough to
approve this demo unattended.** These are real local Ollama responses, not mock
responses. No filename, expected label, or photo number was sent to the model.
Expected labels exist only in the test runner/report. No image matching shortcut,
cloud call, retraining, or server-side session progression was used.

## Human inspection before testing

The user confirmed Photo 3 as the correct reference. It was copied unchanged
to `cup_photos/reference.png`. All five supplied photos are saved locally;
`cup_photos/manifest.json` records their hashes and inspection labels.

| Photo | Visible bottom / middle / top openings | Expected step-2 result |
| --- | --- | --- |
| 1 | Up / down / up | Incorrect: **all three** layers reversed |
| 2 | Down / down / down | Incorrect: middle reversed |
| 3 | Down / up / down | Correct reference |
| 4 | Down / up / down | Appears correct from another view |
| 5 | Up / down / up | Incorrect: **all three** layers reversed |

Photos 1 and 5 have incorrectly oriented middle cups as well as the bottom/top
errors named in the request. Photo 4 is more tightly framed and some exact
contacts are not directly visible, but the cup shapes/orientations and apparent
bridging match the intended arrangement. Hidden contacts are not independently
measured by these images.

## Final implementation: complete step-2 run

Same installed `qwen3-vl:8b-instruct`, Ollama 0.34.0, NVIDIA GB10, **100% GPU**,
12,288 context. Model weights remain unchanged. Every request carries the full
reference and the current image in separate, explicitly labeled messages.
The model describes observations, then produces typed layer checks; code computes
status, a bounded message and `advance_step`. Latency is measured HTTP round trip.

| Photo | Expected | Actual status | advance_step | Latency | Assessment |
| --- | --- | --- | --- | ---: | --- |
| 1 | incorrect | correct | true | 14.461 s | False acceptance |
| 2 | incorrect | correct | true | 9.484 s | False acceptance |
| 3 | correct | correct | true | 9.602 s | Correct classification; reference self-test |
| 4 | correct | correct | true | 9.430 s | Correct classification on this alternate view |
| 5 | incorrect | uncertain | false | 9.532 s | Abstained; did not detect visible orientation errors |

All five returned HTTP 200 and `mock: false`. **2/5 exact status matches**, two
false acceptances, and one abstention. Neither false acceptance is fixed by
schema validation: the underlying visual checks were wrong but structurally
valid. Do not confuse code validation with proof of physical correctness.

Actual guidance for Photos 1–4:

> All three layers look correct.

Actual guidance for Photo 5:

> Show a clearer view of the bottom layer(s), including cup orientation and supports.

The other fields echoed each request's ID and step 2, with empty highlights and
null audio. Full verbatim response objects are in **`cup-results-v3.json`**.
Raw model observations/checks are in **`cup-layer-checks-v3.log`**. For Photos 1
and 2 the observations describe the reference orientations rather than the
visible incorrect snapshot. Reference contamination is a plausible explanation,
not a proven model/runtime root cause.

## Earlier attempts retained, not hidden

- First isolated reference self-test: **18.090 s**, `uncertain`, all checks
  uncertain. It failed to recognize the confirmed correct reference.
- First full checklist run: all five `uncertain`, **8.361–8.725 s**. No false
  advancement, but no correct positive or error recognition. See `cup-results-v1.json`.
- Adding a brief visual observation before the checklist: all five `correct`,
  **9.519–14.323 s**; three false acceptances. See `cup-results-v2.json`.
- Separating reference/current messages produced the final run above; it did
  not resolve the false acceptances.
- A direct descriptive probe took **15.952 s** and correctly described top/middle
  orientation but incorrectly described the reference bottom cups as opening up.
  It also invented contradictory support descriptions; output hit the token limit.
  See `cup-descriptive-probe.json`.
- Reversing image order in a separate diagnostic on Photo 1 took **14.329 s**;
  it still misread middle orientation and called the physical scene `rendered_only`.
  This diagnostic was **not adopted** in the final backend. See `cup-order-probe.json`.

These few related images and retries are development checks, not an independent
accuracy benchmark. Warm caching and changed prompts affect measured latency.

## Deterministic verification and limits

**19 automated tests passed**, including API compatibility, success-only advance
recommendation, repeated requests retaining the supplied step, future-layer
filtering, per-layer mismatch/uncertainty aggregation, explicit mock mode, invalid
model output, connection failures, timeout, and responsive health during inference.
Transport stubs are used only in unit tests, never as runtime fallback.
The existing image client also exercised a live explicit mock server at step 2:
0.110 s, HTTP 200, `mock: true`, `status: uncertain`, `advance_step: false`.
That temporary mock server was stopped afterward; vision mode remains on 8000.

Photos 1–5 all contain full builds. They **do not establish step-0 or step-1
accuracy**. Collect bottom-only and bottom-plus-middle positives, isolated missing/
flipped cups and support errors, and obscured/cropped views. The complete capture
list and updated Unity JSON are in **`CUP_DEMO.md`**.

Backend remains local in vision mode on port 8000. It stores no session progress.
Unity should require human confirmation rather than automatically trust the
observed false `advance_step: true` recommendations. Unity compilation/headset
behavior remains untested. OpenShell is still a required next milestone.
No GitHub push was performed.
