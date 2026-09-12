const fs = require('fs');
const path = require('path');
const validator = require(path.resolve(__dirname, '../../work/gltf-validator'));
const data = fs.readFileSync(0);
validator.validateBytes(new Uint8Array(data), { maxIssues: 20 }).then(r => {
  process.stdout.write(JSON.stringify({errors:r.issues.numErrors,message:r.issues.messages.filter(x=>x.severity===0).map(x=>x.message).join('; ')}));
}).catch(e => {process.stdout.write(JSON.stringify({errors:1,message:String(e)}));process.exitCode=1;});
