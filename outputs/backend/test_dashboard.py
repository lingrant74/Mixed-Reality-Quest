import copy
import unittest
from pathlib import Path
from unittest.mock import patch
from fastapi import HTTPException
from dashboard_api import compile_instruction, validate_glb, store_upload, Draft


class DashboardTests(unittest.TestCase):
    def product(self):
        return {'_id':'a'*32,'published_version':0,'model':{'version':'v1','parts':[{'id':'part_a'},{'id':'part_b'}]},'draft':{'name':'Stand','description':'Demo', 'steps':[{'instructions':'Place the base.','orientation':'Flat face down.','opening_direction':'down','part_ids':['part_a'],'supporting_part_ids':[],'model_version':'v1'},{'instructions':'Add the cap.','orientation':'Face up.','opening_direction':'up','part_ids':['part_b'],'supporting_part_ids':['part_a'],'model_version':'v1'}]}}
    def test_compile_cumulative_legacy_schema(self):
        d=compile_instruction(self.product())
        self.assertEqual(d['steps'][1]['required_slot_ids'],['part_a','part_b'])
        self.assertEqual(d['objects'][0]['supporting_slot_ids'],['table'])
        self.assertIn('Orientation: Flat face down.',d['objects'][0]['placement_instructions'])
    def test_drafts_can_be_incomplete(self):
        self.assertEqual(Draft().steps,[])
        p=self.product();p['draft']['name']=''
        with self.assertRaises(HTTPException):compile_instruction(p)
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
