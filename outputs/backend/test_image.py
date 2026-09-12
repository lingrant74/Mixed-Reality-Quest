"""Send a local JPEG/PNG to the running backend using Python's standard library."""

import argparse
import base64
import json
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--url", default="http://127.0.0.1:8000")
    parser.add_argument("--mime-type", choices=["image/jpeg", "image/png"])
    parser.add_argument("--expect-mode", choices=["mock", "vision"], default="vision")
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("--question", default="What do I do next?")
    parser.add_argument("--step-index", type=int, default=0)
    args = parser.parse_args()
    mime = args.mime_type or {".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".png": "image/png"}.get(args.image.suffix.lower())
    if not mime:
        parser.error("Use a .jpg/.jpeg/.png file or specify --mime-type")
    payload = {
        "request_id": "test-request-1",
        "session_id": "test-session-1",
        "instruction_id": "assembly-1",
        "step_index": args.step_index,
        "placed_piece_ids": [],
        "question": args.question,
        "snapshot": {
            "mime_type": mime,
            "view_type": "headset",
            "data_base64": base64.b64encode(args.image.read_bytes()).decode("ascii"),
        },
    }
    req = Request(args.url.rstrip("/") + "/assist", data=json.dumps(payload).encode(),
                  headers={"Content-Type": "application/json"}, method="POST")
    started = time.perf_counter()
    try:
        with urlopen(req, timeout=args.timeout) as response:
            result = json.load(response)
    except HTTPError as exc:
        raise SystemExit(f"HTTP {exc.code}: {exc.read().decode()}") from exc
    except (URLError, TimeoutError) as exc:
        raise SystemExit(f"Connection/timeout error: {exc}") from exc
    finally:
        print(f"HTTP round-trip latency: {time.perf_counter() - started:.3f} seconds")
    assert result["request_id"] == payload["request_id"]
    assert result["step_index"] == payload["step_index"]
    assert result["mock"] is (args.expect_mode == "mock")
    assembly = json.loads(Path(__file__).with_name("assembly.json").read_text())
    assert set(result["highlight_piece_ids"]) <= {p["id"] for p in assembly["pieces"]}
    assert result["audio_url"] is None
    assert result["status"] in {"correct", "incorrect", "uncertain"}
    assert result["advance_step"] is False
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
