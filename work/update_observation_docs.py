from pathlib import Path
import json
root=Path('outputs/backend')
p=root/'CUP_DEMO.md'; s=p.read_text(); s=s.replace('Every inference\nreceives this image first and the incoming snapshot second. Reference is not\nevidence that hidden cups in the snapshot are correct.', 'The reference remains stored but is not read or sent during observation extraction.\nOnly the current snapshot is shown to the model.')
s=s.replace('It is supplied to the model, but the current demo\nreturns a layer-evaluation message rather than unrestricted question answering.', 'It remains accepted for compatibility but is not sent to the observation model.\nNeither step rules nor placed IDs nor request metadata are sent to that model;\nPython applies the requested step after extraction.')
s=s.replace('model checks;', 'per-cup observations;')
a=s.index('`advance_step` is exactly'); b=s.index('\n`highlight_piece_ids`',a)
s=s[:a]+'''`advance_step` is **always false**, including when `status` is `correct`.
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
''' +s[b:]
s=s.replace('Reference/model/timeout failures', 'Model/timeout failures'); p.write_text(s)
p=root/'VISION_SETUP.md';s=p.read_text();a=s.index('`CUP_REFERENCE_PATH`');b=s.index('\nOther model errors',a);s=s[:a]+'''The confirmed reference stays at `cup_photos/reference.png`. The retained
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
''' +s[b:];p.write_text(s)
p=root/'README.md';s=p.read_text().replace('reference-based cup evaluation','observation-first cup evaluation').replace('Unity remains responsible for acting on advancement recommendations.','manual confirmation is mandatory and `advance_step` is always false.').replace('real vision responses use model guidance','real vision responses use deterministic guidance from model observations').replace('[cup evaluation results](CUP_RESULTS.md)','[current observation-first results](CUP_OBSERVATION_RESULTS.md)');p.write_text(s)
p=Path('outputs/unity/README.md');s=p.read_text().replace('# Unity mock backend example','# Unity local backend example').replace('`../backend/CUP_RESULTS.md` for observed false acceptances. Require manual\nconfirmation for this demo.', '`../backend/CUP_OBSERVATION_RESULTS.md` for measured model errors. Require manual\nconfirmation: `advance_step` is always false, even for `status: correct`.').replace('the fixed guidance','the assessment guidance (fixed guidance in mock mode)').replace('Requests time out after 30 seconds (configurable); completion,','Requests default to 30 seconds; set the Inspector timeout to 150 seconds for the\nbackend’s 120-second model deadline. Completion,').replace('Camera capture is a separate task. No AI or MongoDB added.','Camera capture is a separate task. AI inference stays on the Dell; no MongoDB added.');p.write_text(s)
p=root/'CUP_RESULTS.md';s=p.read_text();p.write_text('> Historical reference-based pipeline. Superseded by [observation-first results](CUP_OBSERVATION_RESULTS.md); advancement is now disabled for all assessments.\n\n'+s)
rows=json.loads((root/'cup-observation-details.json').read_text())
report=['# Observation-first real-image evaluation','', 'Run on the GB10 with local `qwen3-vl:8b-instruct` through Ollama. Each request used step 2; the model received only the current image, with no reference or target rules. Temperature 0, context 12288, output limit 1800 tokens. No filename or expected label was supplied to inference. Photo 3 remains stored as the reference.','', 'All five requests returned HTTP 200, `mock: false`, `status: incorrect`, `advance_step: false`, empty highlights and null audio. Latencies below are measured HTTP round trips, not token-generation-only timing.','', '| Photo | Visually inspected ground truth | Extracted openings: bottom / middle / top | Status | Seconds |','|---|---|---|---|---|']
truth=['Incorrect: all layers reversed','Incorrect: middle inverted','Correct reference','Correct alternate view','Incorrect: all layers reversed']
for r,t in zip(rows,truth):
 o=r['extracted_observations']; groups=[' '.join(c['position']+':'+c['opening'] for c in o['cups'] if c['layer']==l) for l in ('bottom','middle','top')]
 # Find latency field without assuming report schema.
 latency=r.get('latency_seconds',r.get('elapsed_seconds'))
 report.append(f"| {r['photo']} | {t} | {' / '.join(groups)} | {r['response']['status']} | {latency} |")
report += ['', '## Interpretation', '', '- False acceptances: **0/3 incorrect photos**. False rejections: **2/2 correct photos**. Status matches: **3/5**.', '- Photo 1: all six extracted orientations match the visible cups; the response names only two bottom fixes because guidance is bounded.', '- Photo 2: classification is incorrect for the wrong reason. The model falsely described the middle as up and top as up; guidance incorrectly tells the user to flip the already-correct top. The actual error is the two down-facing middle cups.', '- Photos 3 and 4: bottom orientations were reversed by the model and a third middle cup was invented. Both correct builds were falsely rejected.', '- Photo 5: the reported directions match the actual wrong orientation pattern, but the model invented a third middle cup and corresponding support relationships.', '- All outputs claimed full visibility and a physical scene; those claims are model observations, not independent verification.', '- The previous reference-based run falsely accepted Photos 1 and 2. This run eliminates those false acceptances but rejects both correct images and remains misleading on Photo 2. These five measurements do not establish an overall accuracy improvement or reliability. Manual confirmation remains required.', '', '## Actual responses and visual evidence', '', 'The following evidence is verbatim model output and may contradict the pixels. Full structured observations, supports, identifiers, latencies and responses are in [cup-observation-details.json](cup-observation-details.json).']
for r in rows:
 report += ['', f"### Photo {r['photo']}", '', '**Actual guidance:** '+r['response']['guidance'], '', '| Layer | Position | Opening | Model visual evidence |', '|---|---|---|---|']
 for c in r['extracted_observations']['cups']:
  report.append('| '+' | '.join(str(c[k]).replace('|','/').replace('\n',' ') for k in ('layer','position','opening','visual_evidence'))+' |')
report += ['', '## Verification and remaining work', '', '24 automated tests passed for deterministic rules, current-only model payload, schema validation, errors, mock mode and the unconditional manual interlock. Recorded observations were replayed through the final Python evaluator and produced identical status/guidance. These tests do not prove the model observations are true.', '', 'Intermediate-step accuracy remains untested. Collect bottom-only positive builds for step 0, bottom-plus-middle positives without a top for step 1, missing/flipped cups at each layer, isolated support errors, and occluded/blurred/overlay-obscured views. See [CUP_DEMO.md](CUP_DEMO.md) for the full photo checklist.', '', 'Unity/Quest compilation and headset testing are unverified. OpenShell integration remains an outstanding required milestone. No cloud inference, retraining, MongoDB, speech, or automatic session progress was added.', '']
(root/'CUP_OBSERVATION_RESULTS.md').write_text('\n'.join(report))
print(rows[0].keys())
