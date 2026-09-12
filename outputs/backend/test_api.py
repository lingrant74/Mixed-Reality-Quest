"""API contract/failure tests; Ollama is substituted only inside these tests."""
import asyncio
import base64
from io import BytesIO
import unittest
from unittest.mock import AsyncMock, patch

import httpx
from PIL import Image
import main
from vision import ModelFailure
from cup_test_fixtures import observations


class ApiTests(unittest.IsolatedAsyncioTestCase):
    async def test_correct_still_requires_manual_confirmation(self):
        out = observations()
        with patch.object(main, "infer", new_callable=AsyncMock, return_value=out):
            for _ in range(2):
                response = await self.client.post("/assist", json=self.payload)
                self.assertEqual(response.json()["status"], "correct")
                self.assertEqual(response.json()["issues"], [])
                self.assertFalse(response.json()["advance_step"])
                self.assertEqual(response.json()["step_index"], 0)

    async def asyncSetUp(self):
        self.original_mode = main.settings.mode
        main.settings.mode = "vision"
        main.inference_lock = asyncio.Lock()
        self.client = httpx.AsyncClient(transport=httpx.ASGITransport(app=main.app), base_url="http://test")
        image = BytesIO()
        Image.new("RGB", (8, 8), "red").save(image, "PNG")
        self.payload = {"request_id": "r", "session_id": "s", "instruction_id": "assembly-1",
            "step_index": 0, "placed_piece_ids": [], "snapshot": {"mime_type": "image/png",
            "view_type": "test_texture", "data_base64": base64.b64encode(image.getvalue()).decode()}}

    async def asyncTearDown(self):
        await self.client.aclose()
        main.settings.mode = self.original_mode

    async def test_health_and_explicit_mock(self):
        self.assertEqual((await self.client.get("/health")).json(), {"status": "ok"})
        main.settings.mode = "mock"
        with patch.object(main, "infer", new_callable=AsyncMock) as call:
            response = await self.client.post("/assist", json=self.payload)
        self.assertEqual(response.status_code, 200)
        data=response.json()
        self.assertTrue(data["mock"])
        self.assertEqual(data["status"],"incorrect")
        self.assertEqual(data["guidance"],"The right cup on the second row is upside down.")
        self.assertEqual(data["highlight_piece_ids"],["cup_5"])
        self.assertEqual(data["issues"],[{"piece_id":"cup_5","location":"middle_right",
            "issue_type":"flipped","message":"The right cup on the second row is upside down."}])
        call.assert_not_called()

    async def test_response_contract_and_state(self):
        output = observations("unclear")
        with patch.object(main, "infer", new_callable=AsyncMock, return_value=output) as call:
            response = await self.client.post("/assist", json=self.payload)
        self.assertEqual(response.status_code, 200)
        data = response.json()
        self.assertEqual(set(data), {"request_id", "step_index", "guidance", "highlight_piece_ids", "issues", "audio_url", "mock", "status", "advance_step"})
        self.assertEqual((data["request_id"], data["step_index"], data["mock"], data["audio_url"]), ("r", 0, False, None))
        self.assertEqual(data["status"], "uncertain")
        self.assertEqual(data["issues"], [])
        self.assertFalse(data["advance_step"])
        self.assertEqual(len(call.call_args.args), 2)
        self.assertEqual(call.call_args.args[1], self.payload["snapshot"]["data_base64"])


    async def test_json_failures_without_fallback(self):
        for status, code in [(503, "model_unavailable"), (504, "model_timeout"), (502, "invalid_model_output")]:
            with patch.object(main, "infer", new_callable=AsyncMock,
                              side_effect=ModelFailure(status, code, "Test failure")):
                response = await self.client.post("/assist", json=self.payload)
            self.assertEqual(response.status_code, status)
            self.assertEqual(response.json()["detail"], {"code": code, "message": "Test failure", "request_id": "r"})
            self.assertFalse(main.inference_lock.locked())

    async def test_invalid_image_and_context_skip_inference(self):
        for change in ({"step_index": 5}, {"instruction_id": "unknown"},
                       {"snapshot": {"mime_type": "image/png", "view_type": "test", "data_base64": "!!!"}}):
            with patch.object(main, "infer", new_callable=AsyncMock) as call:
                response = await self.client.post("/assist", json={**self.payload, **change})
            self.assertEqual(response.status_code, 422)
            call.assert_not_called()

    async def test_busy_request_and_responsive_health(self):
        started, release = asyncio.Event(), asyncio.Event()
        async def slow(*args):
            started.set()
            await release.wait()
            return observations("unclear")
        with patch.object(main, "infer", side_effect=slow):
            first = asyncio.create_task(self.client.post("/assist", json=self.payload))
            await asyncio.wait_for(started.wait(), 2)
            try:
                busy = await self.client.post("/assist", json=self.payload)
                self.assertEqual(busy.status_code, 503)
                self.assertEqual(busy.json()["detail"]["code"], "model_busy")
                self.assertEqual((await self.client.get("/health")).status_code, 200)
            finally:
                release.set()
                await first
        self.assertFalse(main.inference_lock.locked())


if __name__ == "__main__":
    unittest.main()
