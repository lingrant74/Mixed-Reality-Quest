import copy
import json
from pathlib import Path
import unittest
from unittest.mock import MagicMock, patch
from bson import ObjectId
from fastapi.testclient import TestClient
from pydantic import ValidationError
from pymongo.errors import DuplicateKeyError, ServerSelectionTimeoutError
import main
from instructions import AssemblyDocument
from seed_instructions import seed_document, SeedConflict

SEED = json.loads(Path(__file__).with_name('instruction_seed.json').read_text())


class InstructionTests(unittest.TestCase):
    def test_cumulative_and_ordering(self):
        d=copy.deepcopy(SEED); d['steps'].reverse()
        parsed=AssemblyDocument.model_validate(d)
        self.assertEqual([len(s.required_slot_ids) for s in parsed.steps],[3,5,6])

    def test_invalid_relationships(self):
        for mutation in (
            lambda d: d['steps'][1]['required_slot_ids'].remove('bottom_left'),
            lambda d: d['objects'][0]['supporting_slot_ids'].append('missing'),
            lambda d: d['objects'][0].update(supporting_slot_ids=['top_center']),
            lambda d: d['objects'][0].update(required_opening_direction='sideways'),
            lambda d: d['objects'][0].update(step_index=True),
            lambda d: d['objects'].append(d['objects'][0]),
            lambda d: d['steps'][0].update(step_index=1),
        ):
            d=copy.deepcopy(SEED); mutation(d)
            with self.assertRaises(ValidationError): AssemblyDocument.model_validate(d)

    def test_seed_preserves_edits(self):
        coll=MagicMock(); coll.insert_one.side_effect=DuplicateKeyError('duplicate')
        coll.find_one.return_value={'_id':ObjectId(),**SEED,'name':'Edited by teammate'}
        with self.assertRaises(SeedConflict): seed_document(coll,SEED)
        coll.update_one.assert_not_called(); coll.replace_one.assert_not_called()

    def test_seed_rerun(self):
        coll=MagicMock(); coll.insert_one.side_effect=DuplicateKeyError('duplicate')
        coll.find_one.return_value={'_id':ObjectId(),**SEED}
        self.assertEqual(seed_document(coll,SEED)['status'],'unchanged')
        coll.create_index.assert_called_once_with('instruction_id',unique=True)

    def test_detail_serializes_id_and_sorts(self):
        d=copy.deepcopy(SEED);d['steps'].reverse(); oid=ObjectId()
        with patch('instructions.collection') as c:
            c.return_value.find_one.return_value={'_id':oid,**d}
            r=TestClient(main.app).get('/instructions/assembly-1')
        self.assertEqual(r.status_code,200)
        self.assertEqual(r.json()['_id'],str(oid))
        self.assertEqual([s['step_index'] for s in r.json()['steps']],[0,1,2])

    def test_missing_and_unavailable(self):
        client=TestClient(main.app)
        with patch('instructions.collection') as c:
            c.return_value.find_one.return_value=None
            self.assertEqual(client.get('/instructions/missing').status_code,404)
            c.side_effect=ServerSelectionTimeoutError('offline')
            for path in ('/instructions','/instructions/assembly-1'):
                r=client.get(path)
                self.assertEqual(r.status_code,503)
                self.assertEqual(r.json()['detail']['code'],'mongodb_unavailable')
            self.assertEqual(client.get('/health').json(),{'status':'ok'})

    def test_invalid_stored_document(self):
        with patch('instructions.collection') as c:
            c.return_value.find_one.return_value={'_id':ObjectId(),**SEED,'version':0}
            r=TestClient(main.app).get('/instructions/assembly-1')
        self.assertEqual(r.status_code,500)
        self.assertEqual(r.json()['detail']['code'],'invalid_instruction_document')
