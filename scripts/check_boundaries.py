"""Architectural guard, not a network sandbox or a formal proof of all OS behavior."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
errors = []
for path in (root / "src").rglob("*.cs"):
    if {"obj", "bin"} & set(path.parts):
        continue
    source = path.read_text()
    for pattern in [r"System\.Net", r"HttpClient|WebClient|WebRequest|TcpClient|UdpClient|Socket", r'https?://', r"OpenProcess|ReadProcessMemory|WriteProcessMemory|SendInput|SetWindowsHookEx", r"Process\.Start"]:
        if re.search(pattern, source):
            errors.append(f"{path.relative_to(root)}: forbidden runtime boundary: {pattern}")
    # All runtime file writes must flow through ScoutPaths or SQLite's scoped connection.
    if path.name != "ScoutPaths.cs" and re.search(r"File\.(Write|Append|Create|Move|Delete|Open)|new\s+(FileStream|StreamWriter)|Directory\.(Create|Delete|Move)", source):
        errors.append(f"{path.relative_to(root)}: unscoped file operation")
for path in (root / "src").rglob("*.csproj"):
    tree = ET.parse(path)
    for ref in tree.findall(".//ProjectReference"):
        if not (path.parent / ref.attrib["Include"]).resolve().is_relative_to(root / "src"):
            errors.append(f"{path}: runtime references development tools")
    for dep in tree.findall(".//PackageReference"):
        if dep.attrib["Include"] not in {"Microsoft.Data.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3", "Tesseract"}:
            errors.append(f"{path}: runtime dependency requires boundary review")
if len(sys.argv) > 1:
    artifact = Path(sys.argv[1])
    if not (artifact / "Sts2Scout.exe").exists():
        errors.append("Missing executable")
    if any("Seeder" in p.name or "Tests" in p.name for p in artifact.rglob("*")):
        errors.append("Development tool leaked into artifact")
    # Framework HTTP assemblies may exist in a self-contained .NET runtime. They are
    # not an application HTTP dependency. Application source/project graph is checked above.
if errors:
    print("\n".join(errors))
    raise SystemExit(1)
print("PASS: runtime has no HTTP use/configured endpoint, external project reference, game-memory/input API, or unscoped write API.")
