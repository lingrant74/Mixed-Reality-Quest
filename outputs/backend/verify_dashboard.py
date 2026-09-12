"""Real HTTP dashboard checks; creates and removes only its own test records."""
import copy
import hashlib
import json
from pathlib import Path
import httpx
from instructions import collection
from dashboard_api import FILES

root=Path(__file__).resolve().parent
report={};assets=[];pid=None
with httpx.Client(base_url='http://127.0.0.1:8000',timeout=40,trust_env=False) as c:
 def call(method,url,status=200,**kwargs):
  r=c.request(method,url,**kwargs)
  assert r.status_code==status,(url,r.status_code,r.text[:800])
  return r.json()
 try:
  seed=call('GET','/instructions/assembly-1')
  p=call('POST','/dashboard/api/products',201);pid=p['_id'];url='/dashboard/api/products/'+pid;iid='product_'+pid
  call('GET','/instructions/'+iid,404)
  assert iid not in [x['instruction_id'] for x in call('GET','/instructions')]
  report['draft_hidden']=True
  call('POST',url+'/model?revision='+str(p['revision']),422,content=b'not a glb')
  call('POST','/dashboard/api/images',422,content=b'<svg></svg>')
  call('POST','/dashboard/api/images',413,content=b'x'*(8*1024*1024+1))
  call('GET','/assets/not-a-generated-file.png',404)
  report['invalid_uploads_and_size_rejected']=True
  p=call('POST',url+'/model?revision='+str(p['revision']),content=(root/'dashboard-demo.glb').read_bytes());assets.append(p['model']['url'])
  image=call('POST','/dashboard/api/images',201,content=(root/'dashboard-demo.png').read_bytes());assets.append(image['url'])
  model=copy.deepcopy(p['model']);ids=[x['id'] for x in model['parts']]
  draft={'name':'API verification fixture','description':'Temporary dashboard test','thumbnail_url':image['url'],'steps':[{'instructions':'Place part '+str(i+1),'orientation':'Face down','opening_direction':'down','part_ids':[part],'supporting_part_ids':[] if i==0 else [ids[i-1]],'model_version':model['version'],'target_image_url':image['url']} for i,part in enumerate(ids)]}
  p=call('PUT',url,json={'revision':p['revision'],'draft':draft})
  assert call('GET',url)['draft']==p['draft'];report['save_reload']=True
  call('PUT',url,409,json={'revision':1,'draft':draft});report['stale_revision_rejected']=True
  p=call('POST',url+'/publish',json={'revision':p['revision']})
  published=call('GET','/instructions/'+iid)
  assert [len(x['required_slot_ids']) for x in published['steps']]==[1,2,3]
  assert published['steps'][0]['target_image_path'].startswith('http://127.0.0.1:8000/assets/')
  manifest=call('GET','/instructions/'+iid+'/assets')
  assert c.get(manifest['model']['url']).content==(root/'dashboard-demo.glb').read_bytes()
  assert iid in [x['instruction_id'] for x in call('GET','/instructions')]
  report['publish_and_http_assets']=True
  bad=copy.deepcopy(draft);bad['steps'][0]['part_ids']=['missing']
  p=call('PUT',url,json={'revision':p['revision'],'draft':bad})
  call('POST',url+'/publish',422,json={'revision':p['revision']})
  assert call('GET','/instructions/'+iid)==published
  report['invalid_draft_preserves_publication']=True
  p=call('PUT',url,json={'revision':p['revision'],'draft':draft})
  p=call('POST',url+'/model?revision='+str(p['revision']),content=(root/'dashboard-demo.glb').read_bytes());assets.append(p['model']['url'])
  assert p['model']['parts'][0]['id']!=model['parts'][0]['id']
  call('POST',url+'/publish',422,json={'revision':p['revision']})
  assert call('GET','/instructions/'+iid)==published
  assert call('GET','/instructions/'+iid+'/assets')==manifest
  report['model_replacement_requires_review']=True
  assert call('GET','/instructions/assembly-1')==seed
  assert call('GET','/health')=={'status':'ok'}
  report['seed_and_health_unchanged']=True
 finally:
  if pid: collection().database.products.delete_one({'_id':pid})
  for asset in assets:
   filename=asset.rsplit('/',1)[-1]
   collection().database.assets.delete_one({'_id':filename});(FILES/filename).unlink(missing_ok=True)
(root/'DASHBOARD_VERIFICATION.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
