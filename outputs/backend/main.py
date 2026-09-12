"""Local assembly hints with explicit vision/mock modes. No placement decisions."""

import asyncio
import logging
import base64
import binascii
import os
import warnings
from io import BytesIO
from typing import Literal

from fastapi import FastAPI, HTTPException
from PIL import Image, UnidentifiedImageError
from pydantic import BaseModel, Field
from starlette.concurrency import run_in_threadpool

from vision import ASSEMBLY, ModelFailure, Settings, infer, evaluate
from instructions import router as instructions_router
from dashboard_api import router as dashboard_router
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from pathlib import Path
from pymongo.errors import PyMongoError

MAX_IMAGE_BYTES = 10 * 1024 * 1024
MAX_IMAGE_PIXELS = 20_000_000

app = FastAPI(title="Local Assembly Assistant Backend")
app.include_router(instructions_router)
app.include_router(dashboard_router)
app.mount('/dashboard/static', StaticFiles(directory=Path(__file__).with_name('dashboard')), name='dashboard-static')


@app.get('/dashboard', include_in_schema=False)
def dashboard_page():
    return FileResponse(Path(__file__).with_name('dashboard') / 'index.html')


@app.exception_handler(PyMongoError)
async def database_error(request, exc):
    return JSONResponse(status_code=503, content={'detail': {'code': 'mongodb_unavailable', 'message': 'Local storage is unavailable. Start MongoDB and try again.'}})
settings = Settings()
demo_fixture = os.getenv("ASSIST_DEMO_FIXTURE", "").strip()
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


class PlacementIssue(BaseModel):
    piece_id: Literal["cup_1", "cup_2", "cup_3", "cup_4", "cup_5", "cup_6"]
    location: Literal["bottom_left", "bottom_center", "bottom_right", "middle_left", "middle_right", "top_center"]
    issue_type: Literal["misaligned", "flipped"]
    message: str = Field(min_length=1, max_length=220)


class AssistResponse(BaseModel):
    request_id: str
    step_index: int
    guidance: str
    highlight_piece_ids: list[str]
    issues: list[PlacementIssue]
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
    guidance, highlights, issues = "Mock mode: cup placement has not been evaluated.", [], []
    status = "uncertain"
    if request.instruction_id != ASSEMBLY["instruction_id"] or request.step_index not in (0, 1, 2):
        raise HTTPException(422, {"code": "assembly_context_mismatch",
            "message": "Use assembly-1 and step_index 0, 1 or 2 for the cup demo",
            "request_id": request.request_id})
    fixture_active = demo_fixture == "middle_right_flipped" and request.snapshot.mime_type == "image/png"
    if fixture_active:
        # Fixed first-pass fixture. It proves PNG transport, feedback text and
        # hologram selection before real placement detection is connected.
        guidance = "The right cup on the second row is upside down."
        highlights = ["cup_5"]
        issues = [PlacementIssue(piece_id="cup_5", location="middle_right",
            issue_type="flipped", message=guidance)]
        status = "incorrect"
    if settings.mode == "vision" and not fixture_active:
        if inference_lock.locked():
            raise HTTPException(503, {"code": "model_busy", "message": "Another local inference request is running",
                                      "request_id": request.request_id})
        async with inference_lock:
            try:
                output = await infer(settings, request.snapshot.data_base64)
                logging.getLogger("uvicorn.error").info("Cup observations request_id=%s: %s", request.request_id, output.model_dump_json())
                status, guidance = evaluate(output, request.step_index)
            except ModelFailure as exc:
                raise HTTPException(exc.status, {"code": exc.code, "message": exc.message,
                                                 "request_id": request.request_id}) from exc
    return AssistResponse(
        request_id=request.request_id,
        step_index=request.step_index,
        guidance=guidance,
        highlight_piece_ids=highlights,
        issues=issues,
        audio_url=None,
        mock=settings.mode == "mock" or fixture_active,
        status=status,
        advance_step=False,  # Temporary manual-confirmation interlock, even for correct.
    )
