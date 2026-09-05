$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '../artifacts/Sts2Scout-win-x64'
& dotnet publish (Join-Path $PSScriptRoot '../src/Scout.Windows/Scout.Windows.csproj') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& python (Join-Path $PSScriptRoot 'stage_artifact.py') $out
if ($LASTEXITCODE -ne 0) { throw 'Staging failed' }
& python (Join-Path $PSScriptRoot 'check_boundaries.py') $out
if ($LASTEXITCODE -ne 0) { throw 'Artifact boundary check failed' }
Write-Host "Publish directory: $out"
