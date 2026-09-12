"""Local assembly hints with explicit vision/mock modes. No placement decisions."""

import asyncio
import logging
import base64
import binascii
import warnings
from io import BytesIO
from typing import Literal

from fastapi import FastAPI, HTTPException
from PIL import Image, UnidentifiedImageError
from pydantic import BaseModel, Field
from starlette.concurrency import run_in_threadpool

from vision import ASSEMBLY, ModelFailure, Settings, infer, evaluate

MAX_IMAGE_BYTES = 10 * 1024 * 1024
MAX_IMAGE_PIXELS = 20_000_000

app = FastAPI(title="Local Assembly Assistant Backend")
settings = Settings()
inference_lock = asyncio.Lock()


class Snapshot(BaseModel):
    mime_type: Literal["image/jpeg", "image/png"]
    view_type: str = Field(min_length=1, max_length=100)
    data_base64: str = Field(min_length=1, max_length=4 * ((MAX_IMAGE_BYTES + 2) // 3))


class AssistRequest(BaseModel):
    request_id: str = Field(min_length=1)
    session_id: str = Field(min_length=1)
    instruction_id: str = Field(min_length=1)
    step_index: int = Field(ge=0, strict=True)
    placed_piece_ids: list[str]
    question: str | None = None
    snapshot: Snapshot


class AssistResponse(BaseModel):
    request_id: str
    step_index: int
    guidance: str
    highlight_piece_ids: list[str]
    audio_url: str | None
    mock: bool
    status: Literal["correct", "incorrect", "uncertain"]
    advance_step: bool


def validate_snapshot(snapshot: Snapshot) -> None:
    try:
        raw = base64.b64decode(snapshot.data_base64, validate=True)
    except (binascii.Error, ValueError) as exc:
        raise HTTPException(422, "snapshot.data_base64 must be valid raw Base64") from exc
    if not raw or len(raw) > MAX_IMAGE_BYTES:
        raise HTTPException(422, "Decoded image must be between 1 byte and 10 MiB")
    expected_format = {"image/jpeg": "JPEG", "image/png": "PNG"}[snapshot.mime_type]
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            with Image.open(BytesIO(raw)) as image:
                if image.format != expected_format:
                    raise HTTPException(422, "Image format does not match snapshot.mime_type")
                if image.width * image.height > MAX_IMAGE_PIXELS:
                    raise HTTPException(422, "Image exceeds 20 megapixels")
                image.verify()
            # verify() checks structure; reopening and loading also checks pixel decoding.
            with Image.open(BytesIO(raw)) as image:
                image.load()
    except (UnidentifiedImageError, OSError, SyntaxError, ValueError,
            Image.DecompressionBombError, Image.DecompressionBombWarning) as exc:
        raise HTTPException(422, "Invalid or corrupt JPEG/PNG image") from exc


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/assist", response_model=AssistResponse)
async def assist(request: AssistRequest) -> AssistResponse:
    await run_in_threadpool(validate_snapshot, request.snapshot)
    guidance, highlights = "Mock mode: cup placement has not been evaluated.", []
    status = "uncertain"
    if request.instruction_id != ASSEMBLY["instruction_id"] or request.step_index not in (0, 1, 2):
        raise HTTPException(422, {"code": "assembly_context_mismatch",
            "message": "Use assembly-1 and step_index 0, 1 or 2 for the cup demo",
            "request_id": request.request_id})
    if settings.mode == "vision":
        if inference_lock.locked():
            raise HTTPException(503, {"code": "model_busy", "message": "Another local inference request is running",
                                      "request_id": request.request_id})
        async with inference_lock:
            state = request.model_dump(exclude={"snapshot"})
            state["view_type"] = request.snapshot.view_type
            state["current_step"] = ASSEMBLY["steps"][request.step_index]
            try:
                output = await infer(settings, state, request.snapshot.data_base64)
                logging.getLogger("uvicorn.error").info("Cup layer checks: %s", output.model_dump_json())
                status, guidance = evaluate(output, request.step_index)
            except ModelFailure as exc:
                raise HTTPException(exc.status, {"code": exc.code, "message": exc.message,
                                                 "request_id": request.request_id}) from exc
    return AssistResponse(
        request_id=request.request_id,
        step_index=request.step_index,
        guidance=guidance,
        highlight_piece_ids=highlights,
        audio_url=None,
        mock=settings.mode == "mock",
        status=status,
        advance_step=status == "correct",
    )
