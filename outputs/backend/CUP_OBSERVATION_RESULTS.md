# Observation-first real-image evaluation

Run on the GB10 with local `qwen3-vl:8b-instruct` through Ollama. Each request used step 2; the model received only the current image, with no reference or target rules. Temperature 0, context 12288, output limit 1800 tokens. No filename or expected label was supplied to inference. Photo 3 remains stored as the reference.

All five requests returned HTTP 200, `mock: false`, `status: incorrect`, `advance_step: false`, empty highlights and null audio. Latencies below are measured HTTP round trips, not token-generation-only timing.

| Photo | Visually inspected ground truth | Extracted openings: bottom / middle / top | Status | Seconds |
|---|---|---|---|---|
| 1 | Incorrect: all layers reversed | left:up center:up right:up / left:down right:down / center:up | incorrect | 23.178 |
| 2 | Incorrect: middle inverted | left:down center:down right:down / left:up right:up / center:up | incorrect | 23.21 |
| 3 | Correct reference | left:up center:up right:up / left:up center:up right:up / center:down | incorrect | 27.372 |
| 4 | Correct alternate view | left:up center:up right:up / left:up center:up right:up / center:down | incorrect | 29.981 |
| 5 | Incorrect: all layers reversed | left:up center:up right:up / left:down center:down right:down / center:up | incorrect | 23.592 |

## Interpretation

- False acceptances: **0/3 incorrect photos**. False rejections: **2/2 correct photos**. Status matches: **3/5**.
- Photo 1: all six extracted orientations match the visible cups; the response names only two bottom fixes because guidance is bounded.
- Photo 2: classification is incorrect for the wrong reason. The model falsely described the middle as up and top as up; guidance incorrectly tells the user to flip the already-correct top. The actual error is the two down-facing middle cups.
- Photos 3 and 4: bottom orientations were reversed by the model and a third middle cup was invented. Both correct builds were falsely rejected.
- Photo 5: the reported directions match the actual wrong orientation pattern, but the model invented a third middle cup and corresponding support relationships.
- All outputs claimed full visibility and a physical scene; those claims are model observations, not independent verification.
- The previous reference-based run falsely accepted Photos 1 and 2. This run eliminates those false acceptances but rejects both correct images and remains misleading on Photo 2. These five measurements do not establish an overall accuracy improvement or reliability. Manual confirmation remains required.

## Actual responses and visual evidence

The following evidence is verbatim model output and may contradict the pixels. Full structured observations, supports, identifiers, latencies and responses are in [cup-observation-details.json](cup-observation-details.json).

### Photo 1

**Actual guidance:** Turn the bottom left cup so its opening faces down. Turn the bottom center cup so its opening faces down.

| Layer | Position | Opening | Model visual evidence |
|---|---|---|---|
| bottom | left | up | open rim visible, interior visible above body, tapering sides |
| bottom | center | up | open rim visible, interior visible above body, tapering sides |
| bottom | right | up | open rim visible, interior visible above body, tapering sides |
| middle | left | down | small closed base visible above flared open rim below |
| middle | right | down | small closed base visible above flared open rim below |
| top | center | up | open rim visible, interior visible above body, tapering sides |

### Photo 2

**Actual guidance:** Turn the top center cup so its opening faces down.

| Layer | Position | Opening | Model visual evidence |
|---|---|---|---|
| bottom | left | down | The cup is inverted, with its flared rim resting on the table and its tapered base pointing up. The interior is not visible from this angle. |
| bottom | center | down | The cup is inverted, with its flared rim resting on the table and its tapered base pointing up. The interior is not visible from this angle. |
| bottom | right | down | The cup is inverted, with its flared rim resting on the table and its tapered base pointing up. The interior is not visible from this angle. |
| middle | left | up | The cup is upright, with its flared rim visible at the top and its tapered base resting on the bottom layer. The interior is visible above the rim. |
| middle | right | up | The cup is upright, with its flared rim visible at the top and its tapered base resting on the bottom layer. The interior is visible above the rim. |
| top | center | up | The cup is upright, with its flared rim visible at the top and its tapered base resting on the middle layer. The interior is visible above the rim. |

### Photo 3

**Actual guidance:** Turn the bottom left cup so its opening faces down. Turn the bottom center cup so its opening faces down. Other required observations remain uncertain.

| Layer | Position | Opening | Model visual evidence |
|---|---|---|---|
| bottom | left | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the red exterior is facing up. |
| bottom | center | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the red exterior is facing up. |
| bottom | right | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the red exterior is facing up. |
| middle | left | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the red exterior is facing up. |
| middle | center | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the red exterior is facing up. |
| middle | right | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the red exterior is facing up. |
| top | center | down | The cup is upright, showing its closed base. The open rim is not visible as it is covered by the cup above it. The red exterior is facing down. |

### Photo 4

**Actual guidance:** Turn the bottom left cup so its opening faces down. Turn the bottom center cup so its opening faces down. Other required observations remain uncertain.

| Layer | Position | Opening | Model visual evidence |
|---|---|---|---|
| bottom | left | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the cup's tapered shape is clear from the bottom up. |
| bottom | center | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the cup's tapered shape is clear from the bottom up. |
| bottom | right | up | The cup is inverted, showing its open rim and interior. The white base is visible on the wooden surface, and the cup's tapered shape is clear from the bottom up. |
| middle | left | up | The cup is upright, showing its open rim and interior. The white base is visible on the wooden surface, and the cup's tapered shape is clear from the bottom up. |
| middle | center | up | The cup is upright, showing its open rim and interior. The white base is visible on the wooden surface, and the cup's tapered shape is clear from the bottom up. |
| middle | right | up | The cup is upright, showing its open rim and interior. The white base is visible on the wooden surface, and the cup's tapered shape is clear from the bottom up. |
| top | center | down | The cup is inverted, showing its closed base and tapering sides. The open rim is not visible as it is covered by the middle layer cups. |

### Photo 5

**Actual guidance:** Turn the bottom left cup so its opening faces down. Turn the bottom center cup so its opening faces down. Other required observations remain uncertain.

| Layer | Position | Opening | Model visual evidence |
|---|---|---|---|
| bottom | left | up | open rim visible, interior visible above body, cup body flares outward at rim |
| bottom | center | up | open rim visible, interior visible above body, cup body flares outward at rim |
| bottom | right | up | open rim visible, interior visible above body, cup body flares outward at rim |
| middle | left | down | small closed base visible above flared open rim below, cup body tapers inward |
| middle | center | down | small closed base visible above flared open rim below, cup body tapers inward |
| middle | right | down | small closed base visible above flared open rim below, cup body tapers inward |
| top | center | up | open rim visible, interior visible above body, cup body flares outward at rim |

## Verification and remaining work

24 automated tests passed for deterministic rules, current-only model payload, schema validation, errors, mock mode and the unconditional manual interlock. Recorded observations were replayed through the final Python evaluator and produced identical status/guidance. These tests do not prove the model observations are true.

Intermediate-step accuracy remains untested. Collect bottom-only positive builds for step 0, bottom-plus-middle positives without a top for step 1, missing/flipped cups at each layer, isolated support errors, and occluded/blurred/overlay-obscured views. See [CUP_DEMO.md](CUP_DEMO.md) for the full photo checklist.

Unity/Quest compilation and headset testing are unverified. OpenShell integration remains an outstanding required milestone. No cloud inference, retraining, MongoDB, speech, or automatic session progress was added.
