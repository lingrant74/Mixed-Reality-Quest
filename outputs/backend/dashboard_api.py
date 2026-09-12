"""Local manufacturer authoring. A single-document publish keeps drafts isolated."""
import asyncio
import json
import os
import re
import struct
import subprocess
import uuid
import warnings
from datetime import datetime, timezone
from io import BytesIO
from pathlib import Path
from typing import Literal

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import FileResponse
from PIL import Image
from pydantic import Field, ValidationError
from starlette.concurrency import run_in_threadpool
from instructions import StrictDocument, AssemblyDocument, collection

ROOT = Path(__file__).resolve().parents[2]
FILES = Path(os.getenv('DASHBOARD_ASSET_DIR', str(ROOT/'work/dashboard-assets'))).resolve()
MAX_GLB = 50*1024*1024
MAX_IMAGE = 8*1024*1024
router = APIRouter()
upload_lock = asyncio.Semaphore(2)


def fail(message, status=422):
    raise HTTPException(status, {'code':'dashboard_error','message':message})


def db():
    return collection().database


def now():
    return datetime.now(timezone.utc).isoformat()


class DraftStep(StrictDocument):
    instructions: str = Field(default='',max_length=700)
    part_ids: list[str] = Field(default_factory=list,max_length=500)
    orientation: str = Field(default='',max_length=200)
    opening_direction: Literal['up','down'] | None = None
    supporting_part_ids: list[str] = Field(default_factory=list,max_length=500)
    target_image_url: str | None = None
    model_version: str | None = None


class Draft(StrictDocument):
    name: str = Field(default='',max_length=150)
    description: str = Field(default='',max_length=2000)
    steps: list[DraftStep] = Field(default_factory=list,max_length=100)
    thumbnail_url: str | None = None


class SaveRequest(StrictDocument):
    revision: int = Field(ge=1)
    draft: Draft


class Revision(StrictDocument):
    revision: int = Field(ge=1)


def product(pid):
    if not re.fullmatch(r'[a-f0-9]{32}',pid): fail('Product not found',404)
    p=db().products.find_one({'_id':pid})
    if p is None: fail('Product not found',404)
    return p


def change(p, fields):
    result=db().products.update_one({'_id':p['_id'],'revision':p['revision']},
        {'$set':{**fields,'updated_at':now()},'$inc':{'revision':1}})
    if result.matched_count != 1: fail('This product changed in another tab. Reload before saving.',409)
    return product(p['_id'])


def check_revision(p, revision):
    if p['revision'] != revision: fail('This product changed in another tab. Reload before saving.',409)


def check_image_url(url):
    if url is not None:
        if not re.fullmatch(r'/assets/[a-f0-9]{32}\.png',url): fail('Choose an image uploaded through this dashboard.')
        if db().assets.find_one({'_id':url.split('/')[-1],'kind':'image'}) is None: fail('Target image is missing.')


@router.get('/dashboard/api/products')
def list_products():
    return list(db().products.find({}, {'draft.steps':0,'published':0}).sort('updated_at',-1))


@router.post('/dashboard/api/products',status_code=201)
def create_product():
    p={'_id':uuid.uuid4().hex,'revision':1,'updated_at':now(),'draft':Draft().model_dump(),
       'model':None,'published':None,'published_version':0,'published_revision':None}
    db().products.insert_one(p)
    return p


@router.get('/dashboard/api/products/{pid}')
def get_product(pid:str): return product(pid)


@router.put('/dashboard/api/products/{pid}')
def save_product(pid:str, body:SaveRequest):
    p=product(pid);check_revision(p,body.revision)
    check_image_url(body.draft.thumbnail_url)
    for step in body.draft.steps: check_image_url(step.target_image_url)
    return change(p,{'draft':body.draft.model_dump()})


def validate_glb(data):
    if len(data)<20 or data[:4]!=b'glTF': fail('Upload a binary glTF 2.0 (.glb) model.')
    _,version,length=struct.unpack_from('<III',data)
    if version!=2 or length!=len(data): fail('Invalid GLB header or file length.')
    try:
        size,kind=struct.unpack_from('<II',data,12)
        if kind!=0x4e4f534a or size>len(data)-20: fail('GLB JSON chunk is invalid.')
        gltf=json.loads(data[20:20+size])
        if gltf.get('asset',{}).get('version')!='2.0': fail('Only glTF 2.0 is supported.')
        for section in ('buffers','images'):
            if any('uri' in x for x in gltf.get(section,[])): fail('GLB must embed all buffers and images; external/data URI resources are unsupported.')
        unsupported={'KHR_draco_mesh_compression','EXT_meshopt_compression','KHR_texture_basisu'}
        if unsupported.intersection(gltf.get('extensionsUsed',[])): fail('Export an uncompressed GLB with PNG/JPEG textures; Draco, Meshopt and KTX2 are unsupported.')
        if gltf.get('extensionsRequired'): fail('Export a GLB without required extensions for this initial viewer.')
        if len(gltf.get('nodes',[]))>2000: fail('Model exceeds the 2,000-node limit.')
        nodes=gltf.get('nodes',[])
        scene=gltf.get('scene',0)
        roots=gltf['scenes'][scene]['nodes']
        seen=set()
        def visit(i):
            if i in seen: return
            seen.add(i)
            for child in nodes[i].get('children',[]): visit(child)
        for i in roots: visit(i)
        parts=[{'node_index':i,'name':nodes[i].get('name') or gltf['meshes'][nodes[i]['mesh']].get('name') or f'Part {i+1}'} for i in sorted(seen) if 'mesh' in nodes[i]]
        if not parts: fail('The default scene must contain at least one mesh part.')
    except (ValueError,KeyError,IndexError,TypeError,AttributeError,struct.error,RecursionError):
        fail('Invalid GLB scene or mesh structure.')
    # Khronos validator runs locally. No resource fetching callback is supplied.
    script=Path(__file__).with_name('validate_glb.cjs')
    try:
        node_path=os.getenv('DASHBOARD_NODE') or str(ROOT/(ROOT/'work/node-bin-path').read_text().strip())
        result=subprocess.run([node_path,str(script)],input=data,capture_output=True,timeout=25)
        report=json.loads(result.stdout)
        if result.returncode or report.get('errors',1): fail('GLB validation failed: '+str(report.get('message','invalid model'))[:220])
    except (OSError,ValueError,subprocess.TimeoutExpired): fail('Local GLB validator is unavailable or timed out.',503)
    return parts


def store_upload(data, kind):
    if kind=='model':
        parts=validate_glb(data); ext='.glb';mime='model/gltf-binary'
    else:
        try:
            with warnings.catch_warnings():
                warnings.simplefilter('error',Image.DecompressionBombWarning)
                with Image.open(BytesIO(data)) as im:
                    if im.format not in ('PNG','JPEG') or im.width*im.height>20_000_000: fail('Use a JPEG/PNG image up to 20 megapixels.')
                    im.load();out=BytesIO();im.convert('RGB').save(out,format='PNG');data=out.getvalue()
        except (OSError,ValueError,Image.DecompressionBombWarning,Image.DecompressionBombError): fail('Invalid or corrupt JPEG/PNG image.')
        parts=[];ext='.png';mime='image/png'
    filename=uuid.uuid4().hex+ext
    path=FILES/filename
    try:
        FILES.mkdir(parents=True,exist_ok=True)
        path.write_bytes(data)
    except OSError:
        fail('Local file storage is unavailable. Check disk space and permissions.',503)
    try: db().assets.insert_one({'_id':filename,'kind':kind,'mime':mime,'bytes':len(data)})
    except Exception:
        path.unlink(missing_ok=True);raise
    return {'url':'/assets/'+filename,'parts':parts}


async def read_upload(request, limit):
    chunks=[];size=0
    async for chunk in request.stream():
        size+=len(chunk)
        if size>limit: fail(f'Upload exceeds {limit//(1024*1024)} MiB limit.',413)
        chunks.append(chunk)
    if not size: fail('Choose a nonempty file.')
    return b''.join(chunks)


@router.post('/dashboard/api/images',status_code=201)
async def upload_image(request:Request):
    async with upload_lock:
        data=await read_upload(request,MAX_IMAGE)
        return await run_in_threadpool(store_upload,data,'image')


def attach_model(pid,revision,data):
    p=product(pid);check_revision(p,revision)
    asset=store_upload(data,'model');version=uuid.uuid4().hex
    model={'version':version,'url':asset['url'],'parts':[{'id':f'part_{version}_{x["node_index"]}',**x} for x in asset['parts']]}
    return change(p,{'model':model})


@router.post('/dashboard/api/products/{pid}/model')
async def upload_model(pid:str,revision:int,request:Request):
    async with upload_lock:
        data=await read_upload(request,MAX_GLB)
        return await run_in_threadpool(attach_model,pid,revision,data)


@router.get('/assets/{filename}')
def asset_file(filename:str):
    if not re.fullmatch(r'[a-f0-9]{32}\.(glb|png)',filename): fail('Asset not found',404)
    asset=db().assets.find_one({'_id':filename})
    path=FILES/filename
    if asset is None or not path.is_file(): fail('Asset not found',404)
    return FileResponse(path,media_type=asset['mime'],headers={'X-Content-Type-Options':'nosniff'})


def compile_instruction(p):
    d=Draft.model_validate(p['draft']); model=p['model']
    if not d.name or not d.description or not model or not d.steps: fail('Add a name, description, GLB model and at least one complete step before publishing.')
    parts={x['id'] for x in model['parts']};used=set();objects=[];steps=[]
    for i,s in enumerate(d.steps):
        if not s.instructions or not s.orientation or not s.opening_direction or not s.part_ids:
            fail(f'Step {i+1}: add instructions, orientation, opening direction and selected parts.')
        if s.model_version!=model['version']: fail(f'Step {i+1}: review and reselect parts for the current model version.')
        if len(set(s.part_ids))!=len(s.part_ids) or not set(s.part_ids)<=parts or set(s.part_ids)&used:
            fail(f'Step {i+1}: select existing parts, introducing each part only once.')
        if len(set(s.supporting_part_ids))!=len(s.supporting_part_ids) or not set(s.supporting_part_ids)<=used:
            fail(f'Step {i+1}: supports must refer to parts introduced in earlier steps.')
        check_image_url(s.target_image_url)
        text=s.instructions+' Orientation: '+s.orientation
        if len(text)>500: fail(f'Step {i+1}: combined instruction and orientation must be at most 500 characters.')
        objects.extend({'slot_id':pid,'object_type':'model_part','step_index':i,'required_opening_direction':s.opening_direction,
            'supporting_slot_ids':s.supporting_part_ids or ['table'],'placement_instructions':text} for pid in s.part_ids)
        used.update(s.part_ids)
        steps.append({'step_index':i,'instructions':text,'required_slot_ids':[o['slot_id'] for o in objects],'target_image_path':s.target_image_url})
    try:
        return AssemblyDocument.model_validate({'instruction_id':'product_'+p['_id'],'version':p['published_version']+1,
            'name':d.name,'description':d.description,'objects':objects,'steps':steps}).model_dump()
    except ValidationError: fail('Instructions do not satisfy the Unity schema. Check steps and supports.')


@router.post('/dashboard/api/products/{pid}/publish')
def publish(pid:str,body:Revision):
    p=product(pid);check_revision(p,body.revision)
    instruction=compile_instruction(p)
    return change(p,{'published':{'instruction':instruction,'model':p['model'],'published_at':now()},
                     'published_version':instruction['version'],'published_revision':p['revision']+1})


def published_document(p, request):
    raw={'_id':p['_id'],**p['published']['instruction']}
    for step in raw['steps']:
        if step['target_image_path']: step['target_image_path']=str(request.base_url).rstrip('/')+step['target_image_path']
    return raw


@router.get('/instructions/{instruction_id}/assets')
def published_assets(instruction_id:str,request:Request):
    p=db().products.find_one({'_id':instruction_id.removeprefix('product_'),'published':{'$ne':None}}) if instruction_id.startswith('product_') else None
    if p is None: fail('Published product not found',404)
    return {'instruction_id':instruction_id,'version':p['published_version'],'model':{**p['published']['model'],
        'url':str(request.base_url).rstrip('/')+p['published']['model']['url']}}
