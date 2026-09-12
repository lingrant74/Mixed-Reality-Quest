"""Insert the validated seed once; never replace existing/edited instructions."""
import json
from pathlib import Path
from pymongo.errors import DuplicateKeyError, PyMongoError
from instructions import AssemblyDocument, collection


class SeedConflict(Exception):
    pass


def seed_document(coll, document):
    data = AssemblyDocument.model_validate(document).model_dump()
    coll.create_index("instruction_id", unique=True)
    try:
        result = coll.insert_one(dict(data))
        return {"status": "inserted", "_id": str(result.inserted_id)}
    except DuplicateKeyError:
        existing = coll.find_one({"instruction_id": data["instruction_id"]})
        if existing is None:
            raise SeedConflict("A concurrent change occurred; retry after reviewing storage")
        database_id = str(existing.pop("_id"))
        if existing != data:
            raise SeedConflict("Existing instruction differs from seed; preserved unchanged. Review edits manually.")
        return {"status": "unchanged", "_id": database_id}


def main():
    document = json.loads(Path(__file__).with_name("instruction_seed.json").read_text())
    try:
        print(json.dumps(seed_document(collection(), document)))
        return 0
    except (SeedConflict, PyMongoError, ValueError) as exc:
        print(json.dumps({"status": "error", "message": str(exc)}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
