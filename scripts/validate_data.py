"""JSON Schema validation complements the runtime's strict semantic validator."""
import json,sys
from pathlib import Path
import jsonschema
root=Path(__file__).resolve().parents[1]
path=Path(sys.argv[1]) if len(sys.argv)>1 else root/'data/strategy-pack.json'
jsonschema.Draft202012Validator(json.loads((root/'schemas/strategy-pack.schema.json').read_text()), format_checker=jsonschema.FormatChecker()).validate(json.loads(path.read_text()))
print('PASS: strategy/catalog JSON schema')
