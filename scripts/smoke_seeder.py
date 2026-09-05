"""Exercise the shipped development CLI with original fixtures, never online sources."""
from pathlib import Path
import datetime
import json
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
dotnet = sys.argv[1] if len(sys.argv) > 1 else 'dotnet'
cli = root / 'tools/Scout.Seeder/bin/Release/net10.0/Scout.Seeder.dll'
fixtures = root / 'tests/Scout.Tests/Fixtures'

def run(*args, success=True):
    result = subprocess.run([dotnet, str(cli), *map(str, args)], capture_output=True, text=True)
    if (result.returncode == 0) != success:
        raise AssertionError(result.stdout + result.stderr)
    return result

with tempfile.TemporaryDirectory(prefix='scout-seeder-smoke-') as folder:
    work = Path(folder)
    run('prepare', fixtures / 'sources.json', 'file:///scout-fixtures/source.txt', work / 'prompt.txt')
    assert 'BEGIN UNTRUSTED SOURCE' in (work / 'prompt.txt').read_text()
    run('prepare', fixtures / 'sources.json', 'https://not-allowlisted.invalid', work / 'bad.txt', success=False)
    run('ingest', fixtures / 'catalog.json', fixtures / 'sources.json', fixtures / 'ai-response.json', work / 'seed')
    batch = json.loads((work / 'seed/candidates.json').read_text())
    assert len(batch['claims']) == 2 and len(batch['conflicts']) == 1 and not batch['quarantine']
    run('compile', fixtures / 'catalog.json', work / 'seed', work / 'seed/review.json', 'smoke-1', work / 'pack.json', success=False)
    reviews = json.loads((work / 'seed/review.json').read_text())
    for review in reviews:
        review.update(decision='approved', reviewer='original-synthetic-fixture-test', reviewedAt=datetime.datetime.now(datetime.timezone.utc).isoformat())
    (work / 'seed/review.json').write_text(json.dumps(reviews))
    run('compile', fixtures / 'catalog.json', work / 'seed', work / 'seed/review.json', 'smoke-1', work / 'pack.json')
    assert len(json.loads((work / 'pack.json').read_text())['claims']) == 2
    run('compile', fixtures / 'catalog.json', work / 'seed', work / 'seed/review.json', 'smoke-1', work / 'pack.json', success=False)
    run('calibrate', work / 'calibration.json', fixtures / 'reward.pgm', 'screen', 'CardReward', '0', 'reward', '0', '0', '1', str(1 / 3), 'fixture-v1')
    calibration = json.loads((work / 'calibration.json').read_text())
    assert calibration['probes'][0]['kind'] == 'screen'
    run('calibrate', work / 'bad-calibration.json', fixtures / 'reward.pgm', 'bogus', 'CardReward', '0', 'reward', '0', '0', '1', str(1 / 3), 'fixture-v1', success=False)
print('PASS: CLI prepare, allowlist denial, ingest, review gate, compile, overwrite denial, and calibration smoke tests.')
