from pathlib import Path
import hashlib
root=Path(__file__).resolve().parents[1]/'data/ocr'
assert hashlib.sha256((root/'eng.traineddata').read_bytes()).hexdigest()==(root/'eng.sha256').read_text().strip()
assert (root/'LICENSE').stat().st_size>1000
print('PASS: pinned offline OCR model hash and license')
