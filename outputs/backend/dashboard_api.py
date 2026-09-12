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
MAX_SOURCE = 300*1024*1024
# Manufacturer source-CAD archive formats accepted as the authoritative asset.
# Add new extensions here without touching the instruction/manifest schema.
SOURCE_CAD_FORMATS = {'f3z': 'fusion360_archive', 'f3d': 'fusion360_design'}
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
    new_draft=body.draft.model_dump()
    fields={'draft':new_draft}
    if p.get('demo_pending_mapping') and new_draft['steps']!=p['draft']['steps']:
        # The manufacturer actually edited the still-unmapped hardcoded demo
        # steps; stop auto-completing the mapping on a later GLB upload so a
        # real edit is never silently overwritten. An unchanged resave (e.g.
        # the app's own save-before-upload) leaves the pending flag alone.
        fields['demo_pending_mapping']=False
    return change(p,fields)


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


def source_format(filename):
    formats=', '.join('.'+f for f in sorted(SOURCE_CAD_FORMATS))
    if not filename or '/' in filename or '\\' in filename or len(filename)>200:
        fail(f'Upload a supported CAD source file ({formats}).')
    fmt=Path(filename).suffix.lower().lstrip('.')
    if fmt not in SOURCE_CAD_FORMATS: fail(f'Upload a supported CAD source file ({formats}).')
    return fmt


def store_upload(data, kind, filename=None):
    if kind=='model':
        parts=validate_glb(data); ext='.glb';mime='model/gltf-binary'
    elif kind=='source':
        parts=[]; ext='.'+source_format(filename); mime='application/octet-stream'
    else:
        try:
            with warnings.catch_warnings():
                warnings.simplefilter('error',Image.DecompressionBombWarning)
                with Image.open(BytesIO(data)) as im:
                    if im.format not in ('PNG','JPEG') or im.width*im.height>20_000_000: fail('Use a JPEG/PNG image up to 20 megapixels.')
                    im.load();out=BytesIO();im.convert('RGB').save(out,format='PNG');data=out.getvalue()
        except (OSError,ValueError,Image.DecompressionBombWarning,Image.DecompressionBombError): fail('Invalid or corrupt JPEG/PNG image.')
        parts=[];ext='.png';mime='image/png'
    stored_name=uuid.uuid4().hex+ext
    path=FILES/stored_name
    try:
        FILES.mkdir(parents=True,exist_ok=True)
        path.write_bytes(data)
    except OSError:
        fail('Local file storage is unavailable. Check disk space and permissions.',503)
    try: db().assets.insert_one({'_id':stored_name,'kind':kind,'mime':mime,'bytes':len(data),'original_filename':filename})
    except Exception:
        path.unlink(missing_ok=True);raise
    return {'url':'/assets/'+stored_name,'parts':parts,'filename':filename}


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


# Hardcoded MVP demo: a real Fusion parser is not implemented yet. Uploading a
# .f3z/.f3d always regenerates this fixed six-cup sequence into the draft, so the
# rest of the dashboard/manifest/viewer/publish pipeline has real steps to work
# with. Swap this one function out for an actual Fusion/CAD parser later without
# touching anything else.
HARDCODED_CUP_STEPS = [
    ('Place the first red cup at the left side of the bottom row.','up',()),
    ('Place the second red cup beside the first cup in the center of the bottom row.','up',()),
    ('Place the third red cup beside the second cup to complete the bottom row.','up',()),
    ('Place the fourth red cup upside down above the gap between the first and second cups.','down',(0,1)),
    ('Place the fifth red cup upside down above the gap between the second and third cups.','down',(1,2)),
    ('Place the sixth red cup upright on top of the two inverted middle-row cups.','up',(3,4)),
]


def generate_hardcoded_fusion_instructions(model):
    """Placeholder for real Fusion parsing. Always emits the fixed six-cup demo
    sequence; only fills in real GLB part IDs when exactly six parts are
    currently detected (never guesses a mapping otherwise)."""
    parts=(model or {}).get('parts') or []
    mapped=len(parts)==6
    version=model['version'] if (mapped and model) else None
    steps=[]
    for i,(text,direction,support_indices) in enumerate(HARDCODED_CUP_STEPS):
        steps.append({'instructions':text,'orientation':f'Opening {direction}.','opening_direction':direction,
            'part_ids':[parts[i]['id']] if mapped else [],
            'supporting_part_ids':[parts[j]['id'] for j in support_indices] if mapped else [],
            'target_image_url':None,'model_version':version})
    return steps,mapped


def attach_model(pid,revision,data,filename=None):
    p=product(pid);check_revision(p,revision)
    asset=store_upload(data,'model',filename);version=uuid.uuid4().hex
    old=p['model'] or {}
    model={**old,'version':version,'url':asset['url'],'filename':filename,
           'parts':[{'id':f'part_{version}_{x["node_index"]}',**x} for x in asset['parts']]}
    fields={'model':model}
    if p.get('demo_pending_mapping'):
        # The draft still holds the unmapped hardcoded demo steps from an earlier
        # Fusion-file-first upload; try to complete that mapping now, but only
        # ever from this specific pending state, never overwriting a manufacturer's
        # own edits made after the initial generation.
        steps,mapped=generate_hardcoded_fusion_instructions(model)
        fields['draft']={**p['draft'],'steps':steps}
        fields['demo_pending_mapping']=not mapped
    return change(p,fields)


@router.post('/dashboard/api/products/{pid}/model')
async def upload_model(pid:str,revision:int,request:Request,filename:str|None=None):
    async with upload_lock:
        data=await read_upload(request,MAX_GLB)
        return await run_in_threadpool(attach_model,pid,revision,data,filename)


def attach_source(pid,revision,data,filename):
    p=product(pid);check_revision(p,revision)
    fmt=source_format(filename)
    asset=store_upload(data,'source',filename);version=uuid.uuid4().hex
    old=p['model'] or {}
    model={**old,'version':version,'source':{'url':asset['url'],'filename':filename,'format':fmt}}
    steps,mapped=generate_hardcoded_fusion_instructions(model)
    draft={**p['draft'],'steps':steps}
    return change(p,{'model':model,'draft':draft,'demo_pending_mapping':not mapped})


@router.post('/dashboard/api/products/{pid}/source')
async def upload_source(pid:str,revision:int,filename:str,request:Request):
    async with upload_lock:
        data=await read_upload(request,MAX_SOURCE)
        return await run_in_threadpool(attach_source,pid,revision,data,filename)


@router.get('/assets/{filename}')
def asset_file(filename:str):
    extensions='|'.join(['glb','png',*SOURCE_CAD_FORMATS])
    if not re.fullmatch(rf'[a-f0-9]{{32}}\.({extensions})',filename): fail('Asset not found',404)
    asset=db().assets.find_one({'_id':filename})
    path=FILES/filename
    if asset is None or not path.is_file(): fail('Asset not found',404)
    return FileResponse(path,media_type=asset['mime'],filename=asset.get('original_filename'),
                         headers={'X-Content-Type-Options':'nosniff'})


def build_manifest(product_id, d, model):
    """The source-CAD/render/parts/instruction manifest, independent of the Unity schema."""
    source=model['source']
    instructions=[{'instruction_id':f'step_{i+1:03d}','step':i+1,'text':s.instructions,
        'parts':s.part_ids,'requires_parts':s.supporting_part_ids,
        'orientation':{'opening_direction':s.opening_direction,'guidance':s.orientation}} for i,s in enumerate(d.steps)]
    return {'schema_version':1,'product_id':product_id,'model_version':model['version'],
        'assets':{
            'source_cad':{'filename':source['filename'],'type':SOURCE_CAD_FORMATS.get(source['format'],f"cad_{source['format']}")},
            'render_model':{'filename':model.get('filename') or 'model.glb','type':'glb'}},
        'parts':[{'part_id':x['id'],'name':x['name'],'model_node_id':str(x['node_index'])} for x in model['parts']],
        'instructions':instructions}


def compile_instruction(p):
    d=Draft.model_validate(p['draft']); model=p['model']
    if not d.name or not d.description or not model or not model.get('source') or not d.steps:
        fail('Add a name, description, GLB model, source CAD file, and at least one complete step before publishing.')
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
        instruction=AssemblyDocument.model_validate({'instruction_id':'product_'+p['_id'],'version':p['published_version']+1,
            'name':d.name,'description':d.description,'objects':objects,'steps':steps}).model_dump()
    except ValidationError: fail('Instructions do not satisfy the Unity schema. Check steps and supports.')
    return instruction, build_manifest(p['_id'], d, model)


@router.post('/dashboard/api/products/{pid}/publish')
def publish(pid:str,body:Revision):
    p=product(pid);check_revision(p,body.revision)
    instruction,manifest=compile_instruction(p)
    return change(p,{'published':{'instruction':instruction,'manifest':manifest,'model':p['model'],'published_at':now()},
                     'published_version':instruction['version'],'published_revision':p['revision']+1})


def published_document(p, request):
    raw={'_id':p['_id'],**p['published']['instruction']}
    for step in raw['steps']:
        if step['target_image_path']: step['target_image_path']=str(request.base_url).rstrip('/')+step['target_image_path']
    return raw


def find_published(instruction_id):
    if not instruction_id.startswith('product_'): return None
    return db().products.find_one({'_id':instruction_id.removeprefix('product_'),'published':{'$ne':None}})


@router.get('/instructions/{instruction_id}/assets')
def published_assets(instruction_id:str,request:Request):
    p=find_published(instruction_id)
    if p is None: fail('Published product not found',404)
    model=p['published']['model'];base=str(request.base_url).rstrip('/');source=model.get('source')
    return {'instruction_id':instruction_id,'version':p['published_version'],
        'model':{'version':model['version'],'url':base+model['url'],'parts':model['parts']},
        'source_asset':{'filename':source['filename'],'url':base+source['url'],'format':source['format']} if source else None,
        'render_asset':{'filename':model.get('filename'),'url':base+model['url'],'format':'glb'}}


@router.get('/instructions/{instruction_id}/manifest')
def published_manifest(instruction_id:str):
    p=find_published(instruction_id)
    if p is None or 'manifest' not in p['published']: fail('Published manifest not found',404)
    return p['published']['manifest']
