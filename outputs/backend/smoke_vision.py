"""Direct local Ollama image smoke test, without FastAPI."""
import argparse
import base64
import json
import time
from pathlib import Path
from urllib.request import Request, ProxyHandler, build_opener


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--model", default="qwen3-vl:8b-instruct")
    parser.add_argument("--timeout", type=float, default=300)
    args = parser.parse_args()
    payload = {
        "model": args.model,
        "stream": False,
        "messages": [{"role": "user", "content":
            "Describe what is visibly in this image in two short sentences. "
            "Are physical building blocks visible, or is this a screen/rendered image? "
            "Do not treat interface elements as physical blocks. Say uncertain if needed.",
            "images": [base64.b64encode(args.image.read_bytes()).decode()]}],
        "options": {"temperature": 0, "num_predict": 150, "num_ctx": 4096},
    }
    started = time.perf_counter()
    req = Request("http://127.0.0.1:11434/api/chat", data=json.dumps(payload).encode(),
                  headers={"Content-Type": "application/json"})
    with build_opener(ProxyHandler({})).open(req, timeout=args.timeout) as response:
        result = json.load(response)
    print(json.dumps({"latency_seconds": round(time.perf_counter() - started, 3),
                      "result": result}, indent=2))


if __name__ == "__main__":
    main()
