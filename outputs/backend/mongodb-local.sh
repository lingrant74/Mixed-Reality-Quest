#!/usr/bin/env bash
set -euo pipefail
# Resolve this task's persistent runtime/data paths, independent of caller cwd.
workspace=$(cd "$(dirname "$0")/../.." && pwd)
mongod="$workspace/work/mongodb-runtime/usr/bin/mongod"
data="$workspace/work/mongodb-data"
log="$workspace/work/mongodb.log"
case "${1:-start}" in
  start)
    # Refuse an occupied port instead of launching another server.
    python3 - <<'PY'
import socket
with socket.socket() as sock:
    if sock.connect_ex(('127.0.0.1', 27017)) == 0:
        raise SystemExit('Port 27017 is already in use; no duplicate MongoDB started.')
PY
    mkdir -p "$data"
    "$mongod" --dbpath "$data" --bind_ip 127.0.0.1 --port 27017 \
      --logpath "$log" --logappend --fork --pidfilepath "$data/mongod.pid"
    ;;
  stop)
    "$mongod" --dbpath "$data" --shutdown
    ;;
  *) echo 'Usage: mongodb-local.sh start|stop' >&2; exit 2 ;;
esac
