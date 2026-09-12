"""Local integration check. Explicit --restart briefly stops this workspace's DB."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid
import httpx
from instructions import collection
from seed_instructions import SeedConflict, seed_document


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--restart',action='store_true',required=True)
    parser.add_argument('--url',default='http://127.0.0.1:8000')
    parser.add_argument('--output',default=str(Path(__file__).with_name('MONGODB_VERIFICATION.json')))
    args=parser.parse_args()
    root=Path(__file__).resolve().parent
    seed=json.loads((root/'instruction_seed.json').read_text())
    coll=collection()
    report={}
    report['server_version']=coll.database.client.server_info()['version']
    report['seed_first']=seed_document(coll,seed)
    report['seed_repeat']=seed_document(coll,seed)
    report['instruction_count']=coll.count_documents({'instruction_id':'assembly-1'})
    assert report['instruction_count']==1
    # Isolate edit-conflict testing; never modify the real assembly record.
    temp=coll.database['verification_'+uuid.uuid4().hex]
    try:
        seed_document(temp,seed)
        temp.update_one({'instruction_id':'assembly-1'},{'$set':{'name':'Teammate edit'}})
        before=temp.find_one()
        try:
            seed_document(temp,seed)
            raise AssertionError('Edited record was not detected')
        except SeedConflict as exc:
            report['edited_seed_conflict']=str(exc)
        assert temp.find_one()==before
        report['edited_record_preserved']=True
    finally:
        temp.drop()
    with httpx.Client(base_url=args.url,timeout=10,trust_env=False) as client:
        r=client.get('/instructions');assert r.status_code==200
        report['list']=r.json()
        r=client.get('/instructions/assembly-1');assert r.status_code==200
        before=r.json();report['detail_before_restart']=before
        assert [len(s['required_slot_ids']) for s in before['steps']]==[3,5,6]
        r=client.get('/instructions/nonexistent-'+uuid.uuid4().hex)
        assert r.status_code==404
        report['missing']={'http_status':r.status_code,'body':r.json()}
        stopped=False
        try:
            subprocess.run([str(root/'mongodb-local.sh'),'stop'],check=True,capture_output=True)
            stopped=True
            report['unavailable']={}
            for route in ('/instructions','/instructions/assembly-1'):
                start=time.perf_counter();r=client.get(route)
                assert r.status_code==503 and r.json()['detail']['code']=='mongodb_unavailable'
                report['unavailable'][route]={'http_status':r.status_code,'body':r.json(),'seconds':round(time.perf_counter()-start,3)}
            r=client.get('/health');assert r.status_code==200 and r.json()=={'status':'ok'}
            report['health_while_database_stopped']=r.json()
        finally:
            if stopped:
                subprocess.run([str(root/'mongodb-local.sh'),'start'],check=True,capture_output=True)
        r=client.get('/instructions/assembly-1');assert r.status_code==200
        assert r.json()==before
        report['identical_after_restart']=True
        report['count_after_restart']=coll.count_documents({'instruction_id':'assembly-1'})
        assert report['count_after_restart']==1
        report['seed_after_restart']=seed_document(coll,seed)
    Path(args.output).write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('list','detail_before_restart')},indent=2))


if __name__=='__main__':
    main()
