# sts2-scout

Windows-only, external single-player companion for Slay the Spire 2. Scout captures the foreground game client, recognizes calibrated reward/merchant patterns, ranks recognized choices with offline heuristics, and stores local observations and unconfirmed merchant decisions in SQLite.

**Initial implementation / calibration required.** No real STS2 screenshots or licensed catalog were supplied. The bundled strategy pack is intentionally empty. The app starts and captures without them, but useful game recognition and recommendations require a local calibrated profile and a reviewed, patch-matched catalog/strategy pack. Synthetic fixture results are not evidence of real-game accuracy. This is not a ready-to-use strategy database.

- [Exact Windows install, build and daily-use guide](docs/windows-guide.md)
- [Diagnostic capture and calibration](docs/calibration.md)
- [Development-only strategy seeding and review](docs/seeding.md)
- [Architecture, extension points and boundaries](docs/architecture.md)
- [Verification and remaining Windows smoke tests](docs/verification.md)

## Quick source build (Windows x64)

Install Git, Python 3.11+ (checks only), and the .NET 10 SDK, then open PowerShell:

```powershell
git clone https://github.com/jc80800/sts2-scout.git
cd sts2-scout
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore Sts2Scout.sln
dotnet build Sts2Scout.sln -c Release --no-restore
dotnet test tests/Scout.Tests/Scout.Tests.csproj -c Release --no-build --logger 'console;verbosity=normal'
dotnet format Sts2Scout.sln --verify-no-changes --no-restore
python scripts/check_boundaries.py
dotnet publish src/Scout.Windows/Scout.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/Sts2Scout-win-x64
.\artifacts\Sts2Scout-win-x64\Sts2Scout.exe
```

The shipped application makes no network requests, requires no API key or model, and contains no seeding executable. It neither reads game memory nor modifies game files, installs mods, hooks rendering, or sends game input. All application writes go under `%LOCALAPPDATA%\Sts2Scout`. Screenshots are not saved unless diagnostics are enabled and the save button is pressed. Build-time NuGet downloads are separate from runtime behavior.

Source is under the existing [GNU AGPL v3 license](LICENSE), with no warranty. Original synthetic fixtures are included under the same license. No third-party game text, art, or datasets are bundled. See [third-party notices](docs/third-party-notices.md) for software dependencies.
