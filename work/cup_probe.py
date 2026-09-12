import base64,json,time
from pathlib import Path
from urllib.request import Request,urlopen
b=base64.b64encode(Path('outputs/backend/cup_photos/reference.png').read_bytes()).decode()
p={'model':'qwen3-vl:8b-instruct','stream':False,'messages':[{'role':'user','content':'Image 1 is a reference. Image 2 is the current photo. Describe the actual cups in image 2. How many cups are in each of the three layers and which way do their openings face? Use the visible open rims and closed bases; ignore logos. If visible, say so; if truly obscured, say uncertain.','images':[b,b]}],'options':{'temperature':0,'num_predict':220,'num_ctx':12288},'keep_alive':'10m'}
t=time.perf_counter()
with urlopen(Request('http://127.0.0.1:11434/api/chat',data=json.dumps(p).encode(),headers={'Content-Type':'application/json'}),timeout=120) as r:x=json.load(r)
print(json.dumps({'latency_seconds':time.perf_counter()-t,'result':x},indent=2))
