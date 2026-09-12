import copy
import json
import struct
import unittest
from pathlib import Path
from unittest.mock import patch
from fastapi import HTTPException
from dashboard_api import compile_instruction, validate_glb, store_upload, source_format, generate_hardcoded_fusion_instructions, Draft


class DashboardTests(unittest.TestCase):
    def product(self):
        return {'_id':'a'*32,'published_version':0,
            'model':{'version':'v1','filename':'Stand.glb','source':{'url':'/assets/'+'b'*32+'.f3z','filename':'Stand.f3z','format':'f3z'},
                'parts':[{'id':'part_a','name':'Base','node_index':0},{'id':'part_b','name':'Cap','node_index':1}]},
            'draft':{'name':'Stand','description':'Demo', 'steps':[{'instructions':'Place the base.','orientation':'Flat face down.','opening_direction':'down','part_ids':['part_a'],'supporting_part_ids':[],'model_version':'v1'},{'instructions':'Add the cap.','orientation':'Face up.','opening_direction':'up','part_ids':['part_b'],'supporting_part_ids':['part_a'],'model_version':'v1'}]}}
    def test_compile_cumulative_legacy_schema(self):
        d,manifest=compile_instruction(self.product())
        self.assertEqual(d['steps'][1]['required_slot_ids'],['part_a','part_b'])
        self.assertEqual(d['objects'][0]['supporting_slot_ids'],['table'])
        self.assertIn('Orientation: Flat face down.',d['objects'][0]['placement_instructions'])
    def test_manifest_reflects_source_parts_and_instructions(self):
        _,manifest=compile_instruction(self.product())
        self.assertEqual(manifest['schema_version'],1)
        self.assertEqual(manifest['product_id'],'a'*32)
        self.assertEqual(manifest['model_version'],'v1')
        self.assertEqual(manifest['assets']['source_cad'],{'filename':'Stand.f3z','type':'fusion360_archive'})
        self.assertEqual(manifest['assets']['render_model'],{'filename':'Stand.glb','type':'glb'})
        self.assertEqual(manifest['parts'],[{'part_id':'part_a','name':'Base','model_node_id':'0'},{'part_id':'part_b','name':'Cap','model_node_id':'1'}])
        self.assertEqual(manifest['instructions'][1],{'instruction_id':'step_002','step':2,'text':'Add the cap.',
            'parts':['part_b'],'requires_parts':['part_a'],'orientation':{'opening_direction':'up','guidance':'Face up.'}})
    def test_drafts_can_be_incomplete(self):
        self.assertEqual(Draft().steps,[])
        p=self.product();p['draft']['name']=''
        with self.assertRaises(HTTPException):compile_instruction(p)
    def test_missing_source_blocks_publish(self):
        p=self.product();p['model']['source']=None
        with self.assertRaises(HTTPException): compile_instruction(p)
    def test_missing_parts_and_replacement_review(self):
        for field,value in [('part_ids',['missing']),('model_version','old'),('supporting_part_ids',['part_b']),('orientation',''),('opening_direction',None)]:
            p=self.product();p['draft']['steps'][0][field]=value
            with self.assertRaises(HTTPException): compile_instruction(p)
    def test_duplicate_and_reordered_parts_rejected(self):
        p=self.product();p['draft']['steps'].reverse()
        with self.assertRaises(HTTPException):compile_instruction(p)
        p=self.product();p['draft']['steps'][1]['part_ids']=['part_a']
        with self.assertRaises(HTTPException):compile_instruction(p)
    def test_invalid_uploads(self):
        for data in [b'',b'not glb',b'glTF'+b'\0'*40]:
            with self.assertRaises(HTTPException): validate_glb(data)
        with self.assertRaises(HTTPException):store_upload(b'<svg></svg>','image')
    def test_valid_glb_parts(self):
        parts=validate_glb(Path(__file__).with_name('dashboard-demo.glb').read_bytes())
        self.assertEqual([p['name'] for p in parts],['Foundation','Column','Display cap'])
    def test_source_extension_validation(self):
        for filename in (None,'','Design.txt','Design','Design.f3d/../x','a'*201+'.f3z'):
            with self.assertRaises(HTTPException): store_upload(b'binary-cad-bytes','source',filename)
        self.assertEqual(source_format('Stacked Cup Assembly.f3z'),'f3z')
        self.assertEqual(source_format('Design.F3D'),'f3d')
    def test_hardcoded_generator_unmapped_without_six_parts(self):
        for model in (None,{'version':'v1','parts':[]},{'version':'v1','parts':[{'id':'a','name':'A','node_index':0}]*3}):
            steps,mapped=generate_hardcoded_fusion_instructions(model)
            self.assertFalse(mapped)
            self.assertEqual(len(steps),6)
            self.assertTrue(all(s['part_ids']==[] and s['model_version'] is None for s in steps))
    def test_hardcoded_generator_maps_six_real_parts_in_order(self):
        parts=[{'id':f'part_v1_{i}','name':f'Cup_{i+1}','node_index':i} for i in range(6)]
        model={'version':'v1','parts':parts}
        steps,mapped=generate_hardcoded_fusion_instructions(model)
        self.assertTrue(mapped)
        self.assertEqual([s['part_ids'] for s in steps],[[p['id']] for p in parts])
        self.assertEqual([s['supporting_part_ids'] for s in steps],
            [[],[],[],[parts[0]['id'],parts[1]['id']],[parts[1]['id'],parts[2]['id']],[parts[3]['id'],parts[4]['id']]])
        self.assertEqual([s['opening_direction'] for s in steps],['up','up','up','down','down','up'])
        self.assertTrue(all(s['model_version']=='v1' for s in steps))
        self.assertEqual(steps[0]['instructions'],'Place the first red cup at the left side of the bottom row.')
        self.assertEqual(steps[3]['instructions'],'Place the fourth red cup upside down above the gap between the first and second cups.')
        self.assertEqual(steps[5]['instructions'],'Place the sixth red cup upright on top of the two inverted middle-row cups.')
    def test_six_cup_fixture_has_six_parts(self):
        data=Path(__file__).with_name('dashboard-demo-6cup.glb').read_bytes()
        parts=validate_glb(data)
        self.assertEqual([p['name'] for p in parts],[f'Cup_{i+1}' for i in range(6)])
        json_size,json_kind=struct.unpack_from('<II',data,12)
        self.assertEqual(json_kind,0x4e4f534a)
        gltf=json.loads(data[20:20+json_size])
        self.assertEqual(gltf['asset']['generator'],'Assembly Studio reusable six-cup demo')
        self.assertGreater(gltf['accessors'][0]['count'],1000)
        self.assertEqual([n.get('rotation') for n in gltf['nodes']],
            [None,None,None,[1,0,0,0],[1,0,0,0],None])
        self.assertEqual([n['translation'][1] for n in gltf['nodes']],[0,0,0,2.35,2.35,4.7])
