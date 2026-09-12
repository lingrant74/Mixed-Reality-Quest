import asyncio,base64,json,sys,time
from pathlib import Path
sys.path.insert(0,'outputs/backend')
import httpx,vision
async def run():
 b=lambda p:base64.b64encode(Path(p).read_bytes()).decode()
 prompt=vision.SYSTEM_PROMPT.replace('The earlier\nREFERENCE message is the correct finished reference. The LAST user message\ncontains the CURRENT SNAPSHOT to evaluate, which may be correct or incorrect.', 'The FIRST image is the CURRENT SNAPSHOT to evaluate. The SECOND image is the correct finished REFERENCE. Evaluate image 1 only; it may be correct or incorrect.')
 payload={'model':'qwen3-vl:8b-instruct','stream':False,'format':vision.ModelOutput.model_json_schema(),'messages':[{'role':'system','content':prompt},{'role':'user','content':'IMAGE 1 = CURRENT SNAPSHOT. IMAGE 2 = REFERENCE. '+json.dumps({'assembly':vision.ASSEMBLY,'state':{'step_index':2,'current_step':vision.ASSEMBLY['steps'][2]}}),'images':[b('outputs/backend/cup_photos/photo-1.png'),b('outputs/backend/cup_photos/reference.png')]}],'options':{'temperature':0,'num_ctx':12288,'num_predict':650},'keep_alive':'10m'}
 t=time.perf_counter()
 async with httpx.AsyncClient(timeout=120,trust_env=False) as c:
  r=await c.post('http://127.0.0.1:11434/api/chat',json=payload)
 print(json.dumps({'latency_seconds':time.perf_counter()-t,'result':r.json()},indent=2))
asyncio.run(run())
