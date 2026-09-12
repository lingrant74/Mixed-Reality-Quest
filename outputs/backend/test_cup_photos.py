"""Run real /assist requests, recording results; labels never enter the request."""
import argparse
import base64
import json
import time
import uuid
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:8000")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    records = []
    for i in range(1, 6):
        image = Path(__file__).with_name("cup_photos") / f"photo-{i}.png"
        payload = {"request_id": uuid.uuid4().hex, "session_id": "cup-evaluation",
                   "instruction_id": "assembly-1", "step_index": 2, "placed_piece_ids": [],
                   "question": "Evaluate the required layers for this step.",
                   "snapshot": {"mime_type": "image/png", "view_type": "physical_photo",
                                "data_base64": base64.b64encode(image.read_bytes()).decode()}}
        started = time.perf_counter()
        request = Request(args.url.rstrip("/") + "/assist", data=json.dumps(payload).encode(),
                          headers={"Content-Type": "application/json"})
        try:
            with urlopen(request, timeout=180) as response:
                status, body = response.status, json.load(response)
        except HTTPError as exc:
            status, body = exc.code, json.load(exc)
        record = {"photo": i, "expected": "correct" if i in (3, 4) else "incorrect",
                  "latency_seconds": round(time.perf_counter() - started, 3),
                  "http_status": status, "response": body}
        records.append(record)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(records, indent=2) + "\n")
        print(json.dumps(record), flush=True)


if __name__ == "__main__":
    main()
