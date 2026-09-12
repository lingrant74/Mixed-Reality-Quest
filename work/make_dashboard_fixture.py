import struct,json
from pathlib import Path
# Three separate named boxes; a synthetic upload fixture, not a user product.
verts=[];normals=[]
faces=[([(1,-1,-1),(1,1,-1),(1,1,1),(1,-1,1)],(1,0,0)),([(-1,-1,1),(-1,1,1),(-1,1,-1),(-1,-1,-1)],(-1,0,0)),([(-1,1,-1),(-1,1,1),(1,1,1),(1,1,-1)],(0,1,0)),([(-1,-1,1),(-1,-1,-1),(1,-1,-1),(1,-1,1)],(0,-1,0)),([(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)],(0,0,1)),([(1,-1,-1),(-1,-1,-1),(-1,1,-1),(1,1,-1)],(0,0,-1))]
for points,n in faces:
 for i in [0,1,2,0,2,3]:verts.extend(points[i]);normals.extend(n)
a=struct.pack('<'+str(len(verts))+'f',*verts);b=struct.pack('<'+str(len(normals))+'f',*normals);binary=a+b
names=['Foundation','Column','Display cap']
d={'asset':{'version':'2.0','generator':'Assembly Studio synthetic test fixture'},'scene':0,'scenes':[{'nodes':[0,1,2]}], 'nodes':[{'mesh':i,'name':name,'translation':[0,[0,1.2,2.5][i],0],'scale':[[1.4,.2,1],[.3,1,.3],[1,.15,.7]][i]} for i,name in enumerate(names)],'buffers':[{'byteLength':len(binary)}],'bufferViews':[{'buffer':0,'byteOffset':0,'byteLength':len(a),'target':34962},{'buffer':0,'byteOffset':len(a),'byteLength':len(b),'target':34962}],'accessors':[{'bufferView':0,'componentType':5126,'count':36,'type':'VEC3','min':[-1,-1,-1],'max':[1,1,1]},{'bufferView':1,'componentType':5126,'count':36,'type':'VEC3'}],'meshes':[{'name':name,'primitives':[{'attributes':{'POSITION':0,'NORMAL':1},'material':i}]} for i,name in enumerate(names)],'materials':[{'pbrMetallicRoughness':{'baseColorFactor':c,'metallicFactor':.1,'roughnessFactor':.55}} for c in [[.25,.45,.32,1],[.73,.77,.65,1],[.7,.4,.23,1]]]}
j=json.dumps(d).encode();j+=b' '*((-len(j))%4);data=struct.pack('<III',0x46546c67,2,12+8+len(j)+8+len(binary))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(binary),0x004e4942)+binary
Path('outputs/backend/dashboard-demo.glb').write_bytes(data)
