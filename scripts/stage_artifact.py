"""Stage documentation/licenses and a portable ZIP without uploading anything."""
from pathlib import Path
import json
import shutil
import sys
import zipfile

root = Path(__file__).resolve().parents[1]
out = Path(sys.argv[1]).resolve()
if not (out / 'Sts2Scout.exe').is_file():
    raise SystemExit('Publish the Windows application first')
for name in ('README.md', 'LICENSE'):
    shutil.copy2(root / name, out / name)
shutil.copytree(root / 'docs', out / 'docs', dirs_exist_ok=True)
assets = json.loads((root / 'src/Scout.Windows/obj/project.assets.json').read_text())
for folder in assets['packageFolders']:
    for name, library in assets['libraries'].items():
        if library['type'] != 'package':
            continue
        package = Path(folder) / library['path']
        if not package.exists():
            continue
        for item in package.rglob('*'):
            if item.is_file() and (any(word in item.name.lower() for word in ('license', 'notice', 'copying', 'copyright')) or item.suffix == '.nuspec'):
                target = out / 'licenses' / name.replace('/', '-') / item.relative_to(package)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(item, target)
archive = out.with_suffix('.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as zipped:
    for path in sorted(out.rglob('*')):
        if path.is_file():
            zipped.write(path, path.relative_to(out))
print(f'Staged {archive} ({archive.stat().st_size:,} bytes)')
