"""Real HTTP dashboard checks; creates and removes only its own test records."""
import copy
import hashlib
import json
from pathlib import Path
from urllib.parse import quote
import httpx
from instructions import collection
from dashboard_api import FILES

root=Path(__file__).resolve().parent
report={};assets=[];pid=None;pid2=None;pid3=None;pid4=None
with httpx.Client(base_url='http://127.0.0.1:8000',timeout=40,trust_env=False) as c:
 def call(method,url,status=200,**kwargs):
  r=c.request(method,url,**kwargs)
  assert r.status_code==status,(url,r.status_code,r.text[:800])
  return r.json()
 try:
  demo_download=c.get('/dashboard/demo/stacked-red-cups.glb')
  assert demo_download.status_code==200
  assert demo_download.headers['content-type']=='model/gltf-binary'
  assert demo_download.content==(root/'dashboard-demo-6cup.glb').read_bytes()
  report['reusable_six_cup_glb_download']=True
  p4=call('POST','/dashboard/api/products',201);pid4=p4['_id'];url4='/dashboard/api/products/'+pid4
  p4=call('POST',url4+'/demo-model?revision='+str(p4['revision']));assets.append(p4['model']['url'])
  assert p4['model']['filename']=='stacked-red-cups-demo.glb'
  assert [part['name'] for part in p4['model']['parts']]==[f'Cup_{i}' for i in range(1,7)]
  assert len(p4['draft']['steps'])==6
  assert [step['part_ids'] for step in p4['draft']['steps']]==[[part['id']] for part in p4['model']['parts']]
  assert [step['opening_direction'] for step in p4['draft']['steps']]==['up','up','up','down','down','up']
  report['website_model_library_selects_six_cup_glb']=True
  seed=call('GET','/instructions/assembly-1')
  p=call('POST','/dashboard/api/products',201);pid=p['_id'];url='/dashboard/api/products/'+pid;iid='product_'+pid
  call('GET','/instructions/'+iid,404)
  assert iid not in [x['instruction_id'] for x in call('GET','/instructions')]
  report['draft_hidden']=True
  call('POST',url+'/model?revision='+str(p['revision']),422,content=b'not a glb')
  call('POST',url+'/source?revision='+str(p['revision'])+'&filename=Design.txt',422,content=b'not cad')
  call('POST','/dashboard/api/images',422,content=b'<svg></svg>')
  call('POST','/dashboard/api/images',413,content=b'x'*(8*1024*1024+1))
  call('GET','/assets/not-a-generated-file.png',404)
  report['invalid_uploads_and_size_rejected']=True
  p=call('POST',url+'/model?revision='+str(p['revision'])+'&filename=Stacked%20Cup%20Assembly.glb',content=(root/'dashboard-demo.glb').read_bytes());assets.append(p['model']['url'])
  source_name='Stacked Cup Assembly.f3z'
  p=call('POST',url+'/source?revision='+str(p['revision'])+'&filename='+source_name.replace(' ','%20'),content=b'fake-fusion-archive-bytes');assets.append(p['model']['source']['url'])
  assert p['model']['source']=={'url':p['model']['source']['url'],'filename':source_name,'format':'f3z'}
  cad_response=c.get(p['model']['source']['url'])
  assert cad_response.content==b'fake-fusion-archive-bytes'
  assert quote(source_name) in cad_response.headers.get('content-disposition','')
  report['source_asset_stored_and_retrievable']=True
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
  assert manifest['source_asset']=={'filename':source_name,'url':manifest['source_asset']['url'],'format':'f3z'}
  assert manifest['render_asset']=={'filename':'Stacked Cup Assembly.glb','url':manifest['model']['url'],'format':'glb'}
  assert iid in [x['instruction_id'] for x in call('GET','/instructions')]
  report['publish_and_http_assets']=True
  instructions_json=call('GET','/instructions/'+iid+'/manifest')
  assert instructions_json['schema_version']==1
  assert instructions_json['assets']['source_cad']=={'filename':source_name,'type':'fusion360_archive'}
  assert instructions_json['assets']['render_model']=={'filename':'Stacked Cup Assembly.glb','type':'glb'}
  assert [x['part_id'] for x in instructions_json['parts']]==ids
  assert [x['parts'] for x in instructions_json['instructions']]==[[ids[0]],[ids[1]],[ids[2]]]
  assert [x['requires_parts'] for x in instructions_json['instructions']]==[[],[ids[0]],[ids[1]]]
  report['instructions_manifest_matches_source_render_and_parts']=True
  bad=copy.deepcopy(draft);bad['steps'][0]['part_ids']=['missing']
  p=call('PUT',url,json={'revision':p['revision'],'draft':bad})
  call('POST',url+'/publish',422,json={'revision':p['revision']})
  assert call('GET','/instructions/'+iid)==published
  report['invalid_draft_preserves_publication']=True
  p=call('PUT',url,json={'revision':p['revision'],'draft':draft})
  p=call('POST',url+'/model?revision='+str(p['revision']),content=(root/'dashboard-demo.glb').read_bytes());assets.append(p['model']['url'])
  assert p['model']['parts'][0]['id']!=model['parts'][0]['id']
  assert p['model']['source']['filename']==source_name,'GLB replacement must preserve the existing source CAD reference'
  call('POST',url+'/publish',422,json={'revision':p['revision']})
  assert call('GET','/instructions/'+iid)==published
  assert call('GET','/instructions/'+iid+'/assets')==manifest
  report['model_replacement_requires_review']=True
  p=call('PUT',url,json={'revision':p['revision'],'draft':draft})
  old_version=p['model']['version'];parts_before_source_change=p['model']['parts']
  p=call('POST',url+'/source?revision='+str(p['revision'])+'&filename='+source_name.replace(' ','%20'),content=b'revised-fusion-archive-bytes');assets.append(p['model']['source']['url'])
  assert p['model']['version']!=old_version,'Replacing the source CAD file must bump the model version too'
  assert p['model']['parts']==parts_before_source_change,'Source-only replacement must not touch existing render parts'
  call('POST',url+'/publish',422,json={'revision':p['revision']})
  assert call('GET','/instructions/'+iid)==published
  report['source_replacement_requires_review']=True
  assert call('GET','/instructions/assembly-1')==seed
  assert call('GET','/health')=={'status':'ok'}
  report['seed_and_health_unchanged']=True

  # Hardcoded six-cup Fusion demo generator: source arrives first, GLB later.
  p2=call('POST','/dashboard/api/products',201);pid2=p2['_id'];url2='/dashboard/api/products/'+pid2;iid2='product_'+pid2
  cup_texts=['Place the first red cup at the left side of the bottom row.',
   'Place the second red cup beside the first cup in the center of the bottom row.',
   'Place the third red cup beside the second cup to complete the bottom row.',
   'Place the fourth red cup upside down above the gap between the first and second cups.',
   'Place the fifth red cup upside down above the gap between the second and third cups.',
   'Place the sixth red cup upright on top of the two inverted middle-row cups.']
  cup_directions=['up','up','up','down','down','up']
  p2=call('POST',url2+'/source?revision='+str(p2['revision'])+'&filename=Stacked%20Cup%20Assembly.f3z',content=b'fake-cup-assembly-f3z');assets.append(p2['model']['source']['url'])
  assert [s['instructions'] for s in p2['draft']['steps']]==cup_texts
  assert [s['opening_direction'] for s in p2['draft']['steps']]==cup_directions
  assert all(s['part_ids']==[] and s['model_version'] is None for s in p2['draft']['steps']),'No GLB yet: six steps generated but left unmapped'
  call('POST',url2+'/publish',422,json={'revision':p2['revision']})
  report['fusion_first_generates_six_unmapped_steps']=True
  p2=call('POST',url2+'/model?revision='+str(p2['revision'])+'&filename=Stacked%20Cup%20Assembly.glb',content=(root/'dashboard-demo-6cup.glb').read_bytes());assets.append(p2['model']['url'])
  cup_ids=[x['id'] for x in p2['model']['parts']]
  assert len(cup_ids)==6
  assert [s['part_ids'] for s in p2['draft']['steps']]==[[cid] for cid in cup_ids],'GLB arriving after Fusion must auto-map all six detected parts in order'
  expected_supports=[[],[],[],cup_ids[:2],cup_ids[1:3],cup_ids[3:5]]
  assert [s['supporting_part_ids'] for s in p2['draft']['steps']]==expected_supports
  assert all(s['model_version']==p2['model']['version'] for s in p2['draft']['steps'])
  report['glb_arriving_after_fusion_auto_maps_six_parts']=True
  p2['draft']['name']='Stacked Cup Assembly';p2['draft']['description']='Six-cup Fusion demo, hardcoded MVP generator.'
  p2=call('PUT',url2,json={'revision':p2['revision'],'draft':p2['draft']})
  p2=call('POST',url2+'/publish',json={'revision':p2['revision']})
  published2=call('GET','/instructions/'+iid2)
  assert [len(x['required_slot_ids']) for x in published2['steps']]==[1,2,3,4,5,6]
  assert [o['required_opening_direction'] for o in published2['objects']]==cup_directions
  manifest2=call('GET','/instructions/'+iid2+'/manifest')
  assert [x['text'] for x in manifest2['instructions']]==cup_texts
  assert [x['parts'] for x in manifest2['instructions']]==[[cid] for cid in cup_ids]
  assert [x['requires_parts'] for x in manifest2['instructions']]==expected_supports
  report['six_cup_demo_publishes_with_correct_dependencies']=True

  # Wrong part count: GLB with the wrong number of parts must never be silently mapped.
  p3=call('POST','/dashboard/api/products',201);pid3=p3['_id'];url3='/dashboard/api/products/'+pid3
  p3=call('POST',url3+'/model?revision='+str(p3['revision']),content=(root/'dashboard-demo.glb').read_bytes());assets.append(p3['model']['url'])
  p3=call('POST',url3+'/source?revision='+str(p3['revision'])+'&filename=Stacked%20Cup%20Assembly.f3z',content=b'fake-cup-assembly-f3z');assets.append(p3['model']['source']['url'])
  assert all(s['part_ids']==[] and s['model_version'] is None for s in p3['draft']['steps']),'A 3-part GLB must never be silently treated as the six-cup assembly'
  call('POST',url3+'/publish',422,json={'revision':p3['revision']})
  report['wrong_part_count_never_silently_mapped']=True
 finally:
  if pid: collection().database.products.delete_one({'_id':pid})
  if pid2: collection().database.products.delete_one({'_id':pid2})
  if pid3: collection().database.products.delete_one({'_id':pid3})
  if pid4: collection().database.products.delete_one({'_id':pid4})
  for asset in assets:
   filename=asset.rsplit('/',1)[-1]
   collection().database.assets.delete_one({'_id':filename});(FILES/filename).unlink(missing_ok=True)
(root/'DASHBOARD_VERIFICATION.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
