# Local assembly instruction storage

MongoDB Community **8.0.32**, Ubuntu 24.04 ARM64 package, is running on this
Dell's GB10 host. OS inspected: Ubuntu 24.04.4 LTS, `aarch64`. PyMongo 4.18.1 is
installed in the existing `work/backend-venv`; no replacement environment was
created. MongoDB is bound only to **127.0.0.1:27017**. Unity calls FastAPI, never
MongoDB directly. No Atlas or cloud database is used.

This task stores instructions only. `/assist` still uses its existing Python
cup rules and current-only VLM extraction; database edits do not change that
pipeline. `advance_step` remains **always false**, including for correct results.
Explicit mock mode and `/health` are unchanged. No vision accuracy improvement
is claimed. The future hologram/physical-image contract is deferred until sample
inputs exist. No numerical-pose interface was added.

## Start and stop on this workspace

Run from:

```sh
cd /home/dell/Documents/Codex/2026-09-12/file-home-dell-pictures-screenshots-screenshot
export MONGODB_URI=mongodb://127.0.0.1:27017
export MONGODB_DATABASE=assembly_assistant
outputs/backend/mongodb-local.sh start
work/backend-venv/bin/python outputs/backend/seed_instructions.py
ASSIST_MODE=vision work/backend-venv/bin/python -m uvicorn main:app \
  --app-dir outputs/backend --host 0.0.0.0 --port 8000
```

Both variables also have these defaults in code. To use a different local port,
set a loopback URI and change the database startup port together. Non-loopback
MongoDB origins are rejected. There is no automatic `.env` file loader.

**Check existing services before starting them:** the backend and MongoDB were
left running. The MongoDB script refuses an occupied port. Do not start a second
Uvicorn process on port 8000. Restart the existing backend to change configuration.
For teammate mock mode, use `ASSIST_MODE=mock` in the same backend command.

```sh
ss -ltnp | rg '27017|8000|11434'
outputs/backend/mongodb-local.sh stop
outputs/backend/mongodb-local.sh start
```

Persistent database files: `work/mongodb-data/`. Log: `work/mongodb.log`.
Binary: `work/mongodb-runtime/usr/bin/mongod`. These paths are resolved relative
to the script, so it works from other directories too. Do not delete the data
directory when updating code. The server is a local background process, not an
enabled systemd service; after a machine reboot, start it with the command above.
No authentication is configured for this loopback-only hackathon database.

## Installation record and reproduction

MongoDB's official [Ubuntu installation documentation](https://www.mongodb.com/docs/v8.0/tutorial/install-mongodb-on-ubuntu/)
lists Ubuntu 24.04 and ARM64 support. The official Community server package was
extracted locally instead of installing a system service. Existing system
libraries were sufficient; the actual server startup and CRUD checks passed.
Docker exists but the current user cannot access its socket; Docker was not used.

For a fresh equivalent workspace (skip extraction if already installed):

```sh
mkdir -p work/mongodb-runtime work/mongodb-data
curl -fLsS https://repo.mongodb.org/apt/ubuntu/dists/noble/mongodb-org/8.0/multiverse/binary-arm64/mongodb-org-server_8.0.32_arm64.deb \
  -o work/mongodb-server.deb
python3 - <<'PY'
import hashlib
from pathlib import Path
assert hashlib.sha256(Path('work/mongodb-server.deb').read_bytes()).hexdigest() == \
    '77b09ab0cc6f4f95cf65dd9e4335f868f1a24cfe4284bc123acc182c1f4a1be7'
PY
dpkg-deb -x work/mongodb-server.deb work/mongodb-runtime
work/backend-venv/bin/python -m pip install -r outputs/backend/requirements-lock.txt
```

The checksum was verified against the official repository's ARM64 package index.
The installed server reported `distmod: ubuntu2404`, `distarch: aarch64`, version
8.0.32. Installation downloads require network access; database runtime is local.

## Document schema and seeding

`instruction_seed.json` contains the complete six-cup document. `instructions.py`
validates it before seed insertion and when returning stored documents:

- `instruction_id`, positive integer `version`, `name`, `description`.
- `objects`: unique `slot_id`, `object_type`, zero-based `step_index`,
  `required_opening_direction` (`up` or `down`), `supporting_slot_ids`, and short
  `placement_instructions`.
- `steps`: unique contiguous `step_index`, `instructions`, cumulative
  `required_slot_ids`, nullable `target_image_path` (all currently null).
- Supports must be `table` or an existing slot from an earlier step. `table` is a
  reserved support label, not a physical cup ID. Required slots must exactly
  include all objects introduced through the current step. Steps return ordered.
- Unknown fields, invalid orientation, duplicate slots, forward/unknown supports,
  invalid indices and noncumulative requirements fail validation.

Objects belong to their introduction step; required step slots are cumulative:

| Step | Introduced slots | Opening | Required slot count |
|---|---|---|---|
| 0 | bottom_left, bottom_center, bottom_right | down | 3 |
| 1 | middle_left, middle_right | up | 5 |
| 2 | top_center | down | 6 |

Middle left bridges bottom left/center; middle right bridges bottom center/right.
Top center bridges both middle slots. Cups are interchangeable; these IDs identify
assembly slots, never persistent cup identities.

The `instructions` collection has a unique index on `instruction_id`. One current
version is stored per instruction ID; version is metadata, not a second identity.
Seeding uses insert-only behavior:

```json
{"status":"inserted","_id":"6aa58ec440174d9453e95a77"}
```

An identical rerun returns `status: unchanged` with the same ID. Any difference
in an existing document (including version or teammate edits) produces a nonzero
exit with an explicit conflict; it does not update or replace the document.
Review and migrate edits manually. There is no forced-overwrite flag. Validation
is in the application, not a MongoDB collection validator: direct external writes
can bypass it, but invalid stored documents are rejected by the read API.

## Unity endpoints and example responses

Base URL: `http://10.50.19.61:8000` on the same LAN. Existing Quest HTTP settings
apply. Use GET requests without request bodies. These routes do not call Ollama.

`GET /instructions` returns a top-level array of summaries:

```json
[
  {
    "_id": "6aa58ec440174d9453e95a77",
    "instruction_id": "assembly-1",
    "version": 1,
    "name": "Six-cup assembly",
    "description": "Six interchangeable cups in three layers. IDs identify assembly slots, not physical cup identities."
  }
]
```

`GET /instructions/assembly-1` returns those fields plus all six `objects` and
three ordered `steps`. The complete actual response is saved in
[instruction-api-example.json](instruction-api-example.json). Representative
object and step entries (excerpts, not the full assembly):

```json
{
  "slot_id": "middle_left",
  "object_type": "cup",
  "step_index": 1,
  "required_opening_direction": "up",
  "supporting_slot_ids": ["bottom_left", "bottom_center"],
  "placement_instructions": "Place a cup in middle left with its opening facing up, supported by bottom_left and bottom_center."
}
```

```json
{
  "step_index": 1,
  "instructions": "Keep the bottom layer and add two cups with openings facing up, each bridging adjacent bottom cups.",
  "required_slot_ids": ["bottom_left", "bottom_center", "bottom_right", "middle_left", "middle_right"],
  "target_image_path": null
}
```

MongoDB `_id` values are serialized as strings. Unity can ignore them and address
assemblies using `instruction_id`. If using Unity JsonUtility for the list,
wrap its top-level array in a local `{"items": ...}` wrapper before parsing.
The existing C# assist example was not changed to fetch instructions in this task.

HTTP 404 for a missing instruction:

```json
{"detail":{"code":"instruction_not_found","message":"Instruction not found"}}
```

HTTP 503 when MongoDB is unavailable (both routes):

```json
{"detail":{"code":"mongodb_unavailable","message":"Local instruction storage is unavailable"}}
```

HTTP 500 if an externally edited stored document fails schema validation:

```json
{"detail":{"code":"invalid_instruction_document","message":"Stored instruction failed validation"}}
```

Treat non-2xx responses as errors and display/log their bodies. Database connection
and selection timeouts are bounded at two seconds. `/health` remains liveness
only and returns `{"status":"ok"}` even when MongoDB is down.

## Verified results

- Seed inserted `assembly-1`; repeated seeds returned unchanged and kept one record.
- Both real HTTP GET endpoints returned the assembly; `_id` was JSON-safe and
  cumulative required slot counts were 3, 5, 6.
- Edited-record conflict tested in an isolated temporary collection: rerun raised
  an error and preserved the edited document byte-for-byte at the BSON-value level.
  The temporary collection was removed; the actual assembly was not edited.
- Stopped the actual MongoDB process: both routes returned JSON 503, in 2.027 and
  2.005 seconds. `/health` stayed HTTP 200.
- Restarted MongoDB using the same directory: full response and database ID were
  identical, count remained one, and seed remained unchanged.
- Missing instruction returned JSON 404.
- All **31 backend tests passed**, including the existing 24 vision/API/rule tests.
- List/detail and restart checks ran locally on the Dell; Unity/Quest integration
  with the new routes has not been tested. No new VLM inference benchmark was run.

Machine-readable verification: [MONGODB_VERIFICATION.json](MONGODB_VERIFICATION.json).
To repeat (the integration check intentionally restarts the local database):

```sh
work/backend-venv/bin/python outputs/backend/verify_mongodb.py --restart
work/backend-venv/bin/python -m unittest discover -s outputs/backend -p 'test_*.py' -v
```

No blockers remain for this instruction-storage task. OpenShell integration is
still an outstanding required event milestone. No changes were committed or pushed.
