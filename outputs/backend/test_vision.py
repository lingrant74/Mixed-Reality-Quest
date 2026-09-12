"""Deterministic adapter checks. Fake transports here are tests, never runtime fallback."""
import json
import asyncio
import os
import unittest
from unittest.mock import patch

import httpx
import vision
from cup_test_fixtures import observations


class OutputTests(unittest.TestCase):
    def test_invalid_orientation_and_empty_evidence(self):
        for field, value in [("opening", "sideways"), ("visual_evidence", " "), ("supported_by", ["invented"] )]:
            data=observations().model_dump();data["cups"][0][field]=value
            with self.assertRaises(vision.ModelFailure):
                vision.parse_output(json.dumps(data))

    def test_extra_actions_rejected(self):
        data=observations().model_dump();data["advance_step"]=True
        with self.assertRaises(vision.ModelFailure):
            vision.parse_output(json.dumps(data))

    def test_uncertainty_is_explicit(self):
        for scene in ("unclear", "rendered_only"):
            out = observations(scene)
            status, text = vision.evaluate(out, 2)
            self.assertEqual(status, "uncertain")
            self.assertIn("physical cups", text)

    def test_local_only_configuration(self):
        for url in ("https://ollama.com", "http://10.50.19.61:11434", "http://127.0.0.1:11434/path"):
            with patch.dict(os.environ, {"OLLAMA_URL": url}):
                with self.assertRaises(ValueError):
                    vision.Settings()

    def test_malformed_model_json(self):
        for value in ("not json", "{}", "[]"):
            with self.assertRaises(vision.ModelFailure):
                vision.parse_output(value)


class AdapterTests(unittest.IsolatedAsyncioTestCase):
    async def test_wall_clock_deadline(self):
        settings = vision.Settings()
        settings.timeout = 0.01
        async def slow(*args):
            await asyncio.sleep(1)
        with patch.object(vision, "_infer", side_effect=slow):
            with self.assertRaises(vision.ModelFailure) as cm:
                await vision.infer(settings, "image")
        self.assertEqual(cm.exception.status, 504)

    async def call(self, handler):
        real_client = httpx.AsyncClient
        def factory(**kwargs):
            return real_client(transport=httpx.MockTransport(handler), **kwargs)
        with patch.object(vision.httpx, "AsyncClient", side_effect=factory):
            return await vision.infer(vision.Settings(), "test-image")

    async def test_image_and_context_submitted(self):
        def handler(req):
            payload = json.loads(req.content)
            if req.url.path == "/api/show":
                return httpx.Response(200, json={"capabilities": ["vision"]})
            self.assertEqual(payload["messages"][-1]["images"][0], "test-image")
            self.assertEqual(len(payload["messages"]), 2)
            self.assertEqual(payload["messages"][1]["images"], ["test-image"])
            self.assertNotIn("assembly-1", json.dumps(payload))
            self.assertNotIn("openings down", json.dumps(payload))
            return httpx.Response(200, json={"done": True, "message": {"content": observations().model_dump_json()}})
        out = await self.call(handler)
        self.assertEqual(out.scene, "physical")

    async def test_model_errors(self):
        for response, code in [
            (httpx.Response(404), "model_not_installed"),
            (httpx.Response(500), "model_server_error"),
            (httpx.Response(200, json={"remote_model": "remote"}), "remote_model_forbidden"),
            (httpx.Response(200, json={"capabilities": ["completion"]}), "vision_model_required"),
            (httpx.Response(200, content=b"invalid"), "invalid_model_metadata"),
        ]:
            with self.assertRaises(vision.ModelFailure) as cm:
                await self.call(lambda req: response)
            self.assertEqual(cm.exception.code, code)

    async def test_connection_and_timeout(self):
        for error, code in [(httpx.ConnectError("offline"), "model_unavailable"),
                            (httpx.ReadTimeout("slow"), "model_timeout")]:
            def handler(req):
                raise error
            with self.assertRaises(vision.ModelFailure) as cm:
                await self.call(handler)
            self.assertEqual(cm.exception.code, code)


if __name__ == "__main__":
    unittest.main()
