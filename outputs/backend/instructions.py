"""Validated instruction storage, independent of vision and session progress."""
import os
from functools import lru_cache
from typing import Literal
from urllib.parse import urlsplit

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field, ValidationError, model_validator
from pymongo import MongoClient
from pymongo.errors import PyMongoError


class StrictDocument(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, str_strip_whitespace=True)


class AssemblyObject(StrictDocument):
    slot_id: str = Field(pattern=r"^[a-z][a-z0-9_]*$")
    object_type: str = Field(min_length=1)
    step_index: int = Field(ge=0)
    required_opening_direction: Literal["up", "down"]
    supporting_slot_ids: list[str] = Field(min_length=1)
    placement_instructions: str = Field(min_length=1, max_length=500)


class AssemblyStep(StrictDocument):
    step_index: int = Field(ge=0)
    instructions: str = Field(min_length=1, max_length=1000)
    required_slot_ids: list[str] = Field(min_length=1)
    target_image_path: str | None = None


class AssemblyDocument(StrictDocument):
    instruction_id: str = Field(pattern=r"^[a-zA-Z0-9][a-zA-Z0-9_-]*$")
    version: int = Field(ge=1)
    name: str = Field(min_length=1)
    description: str = Field(min_length=1)
    objects: list[AssemblyObject] = Field(min_length=1)
    steps: list[AssemblyStep] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_relationships(self):
        slots = {o.slot_id: o for o in self.objects}
        if len(slots) != len(self.objects) or "table" in slots:
            raise ValueError("Slots must be unique; table is a reserved support")
        indices = [s.step_index for s in self.steps]
        if sorted(indices) != list(range(len(self.steps))):
            raise ValueError("Steps must be unique and contiguous starting at zero")
        for obj in self.objects:
            if obj.step_index not in indices:
                raise ValueError("Object refers to a missing step")
            supports = obj.supporting_slot_ids
            if len(set(supports)) != len(supports):
                raise ValueError("Duplicate support")
            for support in supports:
                if support != "table" and (support not in slots or slots[support].step_index >= obj.step_index):
                    raise ValueError("Support must be table or a slot from an earlier step")
        for step in self.steps:
            expected = {o.slot_id for o in self.objects if o.step_index <= step.step_index}
            if len(set(step.required_slot_ids)) != len(step.required_slot_ids) or set(step.required_slot_ids) != expected:
                raise ValueError("Required slots must include exactly all cumulative objects")
            if not any(o.step_index == step.step_index for o in self.objects):
                raise ValueError("Each step must introduce an object")
        self.steps.sort(key=lambda s: s.step_index)
        return self


@lru_cache(maxsize=1)
def mongo_client():
    uri = os.getenv("MONGODB_URI", "mongodb://127.0.0.1:27017")
    parsed = urlsplit(uri)
    if (parsed.scheme != "mongodb" or parsed.hostname != "127.0.0.1"
            or parsed.username or parsed.password or parsed.path not in ("", "/")
            or parsed.query or parsed.fragment):
        raise ValueError("MONGODB_URI must use a local 127.0.0.1 MongoDB origin")
    return MongoClient(uri, serverSelectionTimeoutMS=2000, connectTimeoutMS=2000,
                       socketTimeoutMS=2000, retryReads=False, retryWrites=False)


def collection():
    name = os.getenv("MONGODB_DATABASE", "assembly_assistant")
    return mongo_client()[name]["instructions"]


def serialize_document(raw):
    data = dict(raw)
    database_id = str(data.pop("_id"))
    validated = AssemblyDocument.model_validate(data)
    return {"_id": database_id, **validated.model_dump()}


def storage_error(exc):
    if isinstance(exc, ValidationError):
        return HTTPException(500, {"code": "invalid_instruction_document", "message": "Stored instruction failed validation"})
    return HTTPException(503, {"code": "mongodb_unavailable", "message": "Local instruction storage is unavailable"})


router = APIRouter()


@router.get("/instructions")
def list_instructions():
    try:
        documents = [serialize_document(raw) for raw in collection().find().sort("instruction_id", 1)]
        documents += [{'_id': p['_id'], **p['published']['instruction']} for p in
                      collection().database.products.find({'published': {'$ne': None}})]
        documents.sort(key=lambda d: d['instruction_id'])
        return [{k: d[k] for k in ("_id", "instruction_id", "version", "name", "description")} for d in documents]
    except (PyMongoError, ValidationError) as exc:
        raise storage_error(exc) from exc


@router.get("/instructions/{instruction_id}")
def get_instruction(instruction_id: str, request: Request):
    try:
        raw = collection().find_one({"instruction_id": instruction_id})
        if raw is None:
            if instruction_id.startswith('product_'):
                from dashboard_api import published_document
                p = collection().database.products.find_one({'_id': instruction_id[8:], 'published': {'$ne': None}})
                if p is not None:
                    return published_document(p, request)
            raise HTTPException(404, {"code": "instruction_not_found", "message": "Instruction not found"})
        return serialize_document(raw)
    except (PyMongoError, ValidationError) as exc:
        raise storage_error(exc) from exc
