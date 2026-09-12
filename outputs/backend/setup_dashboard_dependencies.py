"""One-time official downloads. Runtime uses only local vendored dependencies."""
import base64
import hashlib
import io
import json
from pathlib import Path
import tarfile
import urllib.request

ROOT=Path(__file__).resolve().parents[2]
THREE_FILES={'LICENSE','package.json','build/three.module.js','build/three.core.js','examples/jsm/controls/OrbitControls.js','examples/jsm/loaders/GLTFLoader.js','examples/jsm/utils/BufferGeometryUtils.js'}

def package(name,version,dest,keep=None):
    if (dest/'package.json').exists():
        print(f'{name}: reusing local files');return
    metadata=json.load(urllib.request.urlopen(f'https://registry.npmjs.org/{name}/{version}'))
    data=urllib.request.urlopen(metadata['dist']['tarball']).read()
    algorithm,digest=metadata['dist']['integrity'].split('-',1)
    assert base64.b64encode(hashlib.new(algorithm,data).digest()).decode()==digest
    with tarfile.open(fileobj=io.BytesIO(data)) as archive:
        for member in archive.getmembers():
            name=member.name.removeprefix('package/')
            if not member.isfile() or '..' in Path(name).parts or name.startswith('/') or (keep and name not in keep):continue
            path=dest/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(archive.extractfile(member).read())

package('three','0.180.0',ROOT/'outputs/backend/dashboard/vendor/three',THREE_FILES)
package('gltf-validator','2.0.0-dev.3.10',ROOT/'work/gltf-validator')
version='v24.21.0'; filename=f'node-{version}-linux-arm64.tar.xz'
node=ROOT/f'work/node-runtime/node-{version}-linux-arm64/bin/node'
if not node.exists():
    base=f'https://nodejs.org/dist/{version}/'
    hashes=urllib.request.urlopen(base+'SHASUMS256.txt').read().decode()
    expected=next(line.split()[0] for line in hashes.splitlines() if line.endswith(filename))
    data=urllib.request.urlopen(base+filename).read()
    assert hashlib.sha256(data).hexdigest()==expected
    with tarfile.open(fileobj=io.BytesIO(data),mode='r:xz') as archive:
        archive.extractall(ROOT/'work/node-runtime',filter='data')
(ROOT/'work/node-bin-path').write_text(str(node.relative_to(ROOT)))
print('Local dashboard dependencies ready.')
