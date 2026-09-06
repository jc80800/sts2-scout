"""Development-only research snapshot. Produces a candidate; never installs it automatically.
Use --offline DIR to reproduce from saved API responses, or --download DIR to refresh.
Raw responses remain outside the repository. No game images or prose enter the artifact.
"""
import argparse, datetime, hashlib, json, re, urllib.request
from pathlib import Path

BASE = 'https://spire-codex.com/api/'
FILES = {'cards':'cards', 'changelogs':'changelogs', 'stats':'stats', 'strategy':'guides/ironclad-tier-list'}

def generate(raw, retrieved):
    cards = json.loads((raw/'cards.json').read_bytes())
    history = json.loads((raw/'changelogs.json').read_bytes())
    stats = json.loads((raw/'stats.json').read_bytes())
    # API's game_version field is a SITE version. Verify the actual patch from title.
    patch_entries = [c for c in history if re.search(r'Slay the Spire 2 v\d', c['title'])]
    patch = re.search(r'Slay the Spire 2 v([\d.]+)', patch_entries[0]['title']).group(1)
    if patch != '0.107.1':
        raise ValueError('New patch requires review of extraction rules and strategy; update this guard after review')
    if len(cards) != stats['cards'] or len(cards) < 500 or len({c['id'] for c in cards}) != len(cards):
        raise ValueError('Incomplete/duplicate upstream catalog')
    digest = hashlib.sha256((raw/'cards.json').read_bytes()).hexdigest()
    def tags(c, text):
        result=set()
        if c['type']=='Attack': result.add('damage')
        for pattern, tag in [(r'gain.*?block|block.*?gain','block'),(r'\bdraw\b','draw'),(r'gain.*?\[energy','energy'),(r'apply.*?vulnerable','vulnerable'),(r'apply.*?weak','weak'),(r'\bexhaust\b','exhaust'),(r'discard|scry|put.*?(?:hand|pile)','selection'),(r'add.*?(?:wound|dazed|burn|status)','status-generation'),(r'exhaust.*?(?:card|hand)|discard.*?(?:card|hand)','status-handling')]:
            if re.search(pattern,text,re.S): result.add(tag)
        if 'Exhaust' in (c['keywords'] or []): result.add('exhaust')
        if re.search(r'(?:whenever|each time).*?exhaust',text,re.S): result.add('exhaust-payoff')
        if re.search(r'(?:whenever|each time).*?vulnerable',text,re.S): result.add('vulnerable-payoff'); result.discard('vulnerable')
        if c['type']=='Power' and re.search(r'strength|dexterity|focus|poison|forge|thorns',text): result.add('scaling')
        if re.search(r'whenever|if |for each|every time',text): result.add('conditional')
        if c['type'] in ('Curse','Status'): result.add('burden')
        return sorted(result)
    entities=[]
    for c in sorted(cards,key=lambda c:c['id']):
        text=c['description'].lower(); up=(c.get('upgrade_description') or c['description']).lower()
        bt=tags(c,text); ut=tags(c,up)
        mechanics=[f"target:{c['target']}"]
        for field in ['damage','block','hit_count','cards_draw','energy_gain','hp_loss','star_cost']:
            if c.get(field) is not None: mechanics.append(f'{field}:{c[field]}')
        for p in c.get('powers_applied') or []: mechanics.append(f"power:{p['power_key']}:{p['amount']}")
        for k,v in sorted((c.get('vars') or {}).items()):
            if isinstance(v,(int,float)): mechanics.append(f'variable:{k}:{v}')
        mechanics += ['keyword:'+k for k in c.get('keywords') or []]
        changes=[f'{k}:{v}' for k,v in sorted((c.get('upgrade') or {}).items())] or ['No upgrade delta supplied']
        uncertainty=['Structured extraction; conditional timing and dynamic effects require in-game verification. No complete rules simulation.']
        if 'conditional' in bt: uncertainty.append('Reported draw/damage values may require a trigger; not unconditional output.')
        if c.get('upgrade_description') is None: uncertainty.append('Upgrade text unavailable; use explicit deltas only.')
        cost='X' if c.get('is_x_cost') else str(c['cost']) if c['cost'] is not None else '-'
        entities.append(dict(id='sts2.'+c['id'].lower(),name=c['name'],kind='Card',baseline=5 if c['type'] not in ('Curse','Status') else -10,tags=bt,setupRisk=1 if 'conditional' in bt else 0,
          card=dict(color=c['color'],type=c['type'],rarity=c['rarity'],cost=cost,mechanics=mechanics,upgradeChanges=changes,upgradedTags=ut,
            source=dict(sourceUrl=BASE+'cards/'+c['id'],retrievedAt=retrieved,publishedAt=None,gameVersion=patch,evidenceType='mechanic',confidence=.8,evidenceHash=digest,license='Normalized factual reference from community API; Mega Crit owns game IP; see data/source-manifest.json',evidenceLocator=c['id']),uncertainty=uncertainty,multiplayerOnly=bool(c.get('multiplayer_only')))))
    # Small association-only prior, not a win-probability model. Missing sample sizes reduce confidence.
    strategy_bytes=(raw/'strategy.json').read_bytes(); strategy=json.loads(strategy_bytes)
    claims=[]
    for id,weight in [('offering',.5),('crimson_mantle',.5),('impervious',.4),('fiend_fire',.3),('cinder',-.3),('break',-.3)]:
        claims.append(dict(id='research.ironclad.'+id,kind='DeckNeed',subject='sts2.'+id,other=None,tag='research-prior',weight=weight,gameVersion=patch,
          source=dict(sourceUrl=BASE+'guides/ironclad-tier-list',retrievedAt=retrieved,publishedAt=strategy['date']+'T00:00:00Z',gameVersion=patch,evidenceType='correlation',confidence=.35,evidenceHash=hashlib.sha256(strategy_bytes).hexdigest(),license='Original brief summary of community API evidence; no copied prose',evidenceLocator='community ranking; per-card sample size unavailable'),reviewState='approved',reason='Community guide reports above-average outcomes' if weight>0 else 'Community guide reports below-average outcomes'))
    pack=dict(schemaVersion=1,packVersion='reward-heuristics-1-'+retrieved[:10],gameVersion=patch,entities=entities,claims=claims,catalog=dict(version='sts2-'+patch+'-'+digest[:12],retrievedAt=retrieved,expectedCards=len(entities),sourceHash=digest,strategyMethod='Scout authored deck-feature heuristics v1; neutral baseline; low-weight observational prior; no causal or win probability interpretation'))
    manifest=dict(retrievedAt=retrieved,applicableGameVersion=patch,patchEvidence=BASE+'changelogs',patchEvidenceNote='Site release 1.2.0 title identifies STS2 v0.107.1; site version fields are not game versions. Stable endpoint only; beta excluded.',sources=[dict(url=BASE+route,sha256=hashlib.sha256((raw/(key+'.json')).read_bytes()).hexdigest()) for key,route in FILES.items()],terms='https://github.com/ptrlrd/spire-codex/blob/main/API_TERMS.md',decision='Commit only normalized factual identifiers, numeric mechanics, keyword labels and original short strategy summaries; exclude game art, raw responses, source software and copied descriptions.',attribution='Game IP: Mega Crit. Factual extraction: Peter Lord and Spire Codex contributors.',review='Extraction mappings and six conservative strategy priors reviewed during implementation; individual complex-card mechanics are not exhaustively verified.',uncertainty=['577 entries complete relative to current stable API, not a guarantee of completeness of the installed game.','Guide samples mix player populations and omit per-card sample size; correlation priors limited to confidence 0.35.','No sourced act/ascension-specific card bonuses.','Upgrade deltas can use source-specific variable names; unavailable fields are explicit.'])
    return pack,manifest

if __name__=='__main__':
    p=argparse.ArgumentParser();g=p.add_mutually_exclusive_group(required=True);g.add_argument('--download',type=Path);g.add_argument('--offline',type=Path);p.add_argument('--retrieved-at',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();raw=a.download or a.offline
    if a.download:
        raw.mkdir(parents=True,exist_ok=True)
        for key,route in FILES.items():
            request=urllib.request.Request(BASE+route,headers={'User-Agent':'STS2-Scout-research/1'})
            with urllib.request.urlopen(request,timeout=30) as r: (raw/(key+'.json')).write_bytes(r.read(5_000_001))
    pack,manifest=generate(raw,a.retrieved_at)
    if a.output.exists(): raise SystemExit('Refusing to overwrite candidate')
    a.output.mkdir(parents=True)
    for name,obj in [('strategy-pack.json',pack),('source-manifest.json',manifest)]: (a.output/name).write_text(json.dumps(obj,indent=2,ensure_ascii=False)+'\n')
    print(f'Candidate: {len(pack["entities"])} cards. Review, validate, then install explicitly.')
