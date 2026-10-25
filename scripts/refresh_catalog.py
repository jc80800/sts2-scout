#!/usr/bin/env python3
"""Rebuild offline runtime pack from checked-in normalized facts; --fetch refreshes the public main API.
No source prose/art is retained. A new patch requires an explicitly reviewed lock update.
"""
import argparse, collections, datetime, hashlib, json, re, urllib.request
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
VERSION = 'v0.107.1'
API = 'https://spire-codex.com/api/'
CHARACTERS = {'ironclad', 'silent', 'defect', 'regent', 'necrobinder'}
def normalize(s): return ''.join(c for c in s.casefold() if c.isalnum())
def digest(b): return hashlib.sha256(b).hexdigest()
def write(path, value): path.write_text(json.dumps(value, indent=2, ensure_ascii=False)+'\n')
def fetch(route):
    with urllib.request.urlopen(API+route, timeout=60) as r: return r.read()
def facts(raw, kind):
    text = re.sub(r'\[[^]]+\]', '', raw.get('description','')).lower()
    tags = set()
    for tag, pattern in {'damage':r'\bdeal\b.*damage','block':r'\bblock\b','draw':r'\bdraw\b','energy':r'\benergy\b|\[energy','poison':r'\bpoison\b','shiv':r'\bshiv','doom':r'\bdoom\b','summons':r'\bsummon|\bosty\b','forge':r'\bforge\b|sovereign blade','stars':r'\bstar|\[star','orb':r'\borb|\bchannel\b|\bevoke\b','focus':r'\bfocus\b','strength':r'\bstrength\b','discard':r'\bdiscard','exhaust':r'\bexhaust','weak':r'\bweak\b','vulnerable':r'\bvulnerable\b','aoe':r'all enemies','retain':r'\bretain\b'}.items():
        if re.search(pattern,text): tags.add(tag)
    if raw.get('energy_gain'): tags.add('energy')
    if raw.get('star_cost') is not None or 'star' in raw.get('description','').lower(): tags.add('stars')
    if raw.get('cards_draw'): tags.add('draw')
    if raw.get('damage'): tags.update(['damage','frontloaded'])
    if raw.get('block'): tags.add('block')
    if tags & {'poison','doom','forge','strength','focus'} or raw.get('type')=='Power': tags.add('scaling')
    if raw.get('type')=='Power': tags.add('setup-heavy')
    if 'exhaust' in text and ('hand' in text or 'card' in text): tags.add('deck-thinning')
    values = dict(raw.get('vars') or {})
    for key in ['damage','block','cards_draw','energy_gain','hp_loss','hit_count']:
        if raw.get(key) is not None: values[key]=raw[key]
    for power in raw.get('powers_applied') or []: values['power:'+power['power_key']]=power['amount']
    if kind != 'Card':
        # Preserve factual quantities without redistributing flavor/rules prose.
        values['quantities']=[int(n) for n in re.findall(r'\b\d+\b',text)]
        if raw.get('merchant_price'): values['merchant_price']=raw['merchant_price']
    character = raw.get('color', raw.get('pool','shared')) or 'shared'
    f=dict(normalizedName=normalize(raw['name']),aliases=[],character=character,rarity=raw.get('rarity_key',raw['rarity']),type=raw.get('type'),cost='X' if raw.get('is_x_cost') else str(raw['cost']) if raw.get('cost') is not None else None,starCost='X' if raw.get('is_x_star_cost') else str(raw['star_cost']) if raw.get('star_cost') is not None else None,target=raw.get('target') or 'Unknown',rewardPool=kind=='Card' and character in CHARACTERS|{'colorless'} and raw['rarity'] in {'Common','Uncommon','Rare'},multiplayerOnly=bool(raw.get('multiplayer_only')),values=values,upgrade=raw.get('upgrade') or {},keywords=raw.get('keywords_key') or [],gameVersion=VERSION,sourceId=kind.lower()+'s')
    return dict(id=kind.lower()+':'+raw['id'].lower(),name=raw['name'],kind=kind,tags=sorted(tags),facts=f)

def import_data(raws, fetched):
    cards=json.loads(raws['cards']); by={c['id']:c for c in cards}
    # Main/beta discriminator facts cross-checked against STS2DB 2026-08-14.
    sentinels={'TAUNT':('Uncommon',1),'DEMON_FORM':('Rare',3),'WELL_LAID_PLANS':('Uncommon',1),'ACROBATICS':('Uncommon',1)}
    for id,(rarity,cost) in sentinels.items():
        assert (by[id]['rarity'],by[id]['cost'])==(rarity,cost), 'Main sentinel mismatch: '+id
    assert by['DEMON_FORM']['vars']['StrengthPower']==2
    assert not {'ABUNDANCE','BLADE_SYMPHONY','FADE'} & by.keys(), 'Beta-only cards present'
    entities=[facts(c,kind) for key,kind in [('cards','Card'),('relics','Relic'),('potions','Potion')] for c in json.loads(raws[key]) if not c['id'].startswith('FAKE_')]
    sources=[dict(id=key,url=API+key,gameVersion=VERSION,branch='main',sha256=digest(value),rights='Documented public API permitted; normalized game facts only. Game content © Mega Crit; API terms checked 2026-09-07.') for key,value in raws.items()]
    write(ROOT/'data/catalog/facts.json',dict(gameVersion=VERSION,branch='main',retrievedAt=fetched,sources=sources,entities=entities))

def generate():
    source=json.loads((ROOT/'data/catalog/facts.json').read_text())
    assert source['gameVersion']==VERSION and source['branch']=='main'
    entities=source['entities']; claims=[]
    # Conservative author heuristics: unsupported cards remain near neutral, not fabricated tier ranks.
    overrides={'adrenaline':8,'offering':7.8,'shrug_it_off':7,'pommel_strike':6.8,'backflip':7,'acrobatics':7,'leg_sweep':7.3,'coolheaded':7,'glacier':7.5,'sweeping_beam':6.5,'grave_warden':6.8,'sculpting_strike':6.7,'solar_strike':6.7,'bloodletting':6.3,'grand_finale':3.5,'meteor_strike':4,'demon_form':5.4}
    payoffs={'accelerant':'poison','mirage':'poison','outbreak':'poison','accuracy':'shiv','finisher':'shiv','feel_no_pain':'exhaust','dark_embrace':'exhaust','rupture':'self-damage','defragment':'orb','capacitor':'orb','biased_cognition':'orb','oblivion':'doom','times_up':'doom','seeking_edge':'forge','sword_sage':'forge','royalties':'stars'}
    for e in entities:
        f=e['facts'];tags=set(e['tags']);v=f['values'];key=e['id'].split(':')[1]
        if v.get('hp_loss',0)>0:tags.add('self-damage')
        risk=.6 if 'setup-heavy' in tags else 0
        cost=float(f['cost']) if f['cost'] and f['cost'].isdigit() else 1.5
        baseline=5.0
        if e['kind']=='Card':
            baseline+=.45*len(tags&{'draw','block','energy','aoe'})
            baseline+=min(1,max(0,(v.get('damage',0)*max(1,v.get('hit_count',1) or 1)+v.get('block',0))/max(1,cost)-6)*.12)
            baseline-=max(0,cost-1)*.35
            if key in payoffs: tags.add('archetype-payoff');risk+=.5
            elif tags&{'poison','shiv','orb','doom','forge','stars','exhaust','self-damage'}:tags.add('archetype-enabler')
            baseline=overrides.get(key,round(baseline,2))
            if f['rarity'] in {'Curse','Status'}:baseline=-5
            if f['rarity']=='Basic':baseline=3.5
        e.update(baseline=baseline,setupRisk=risk,tags=sorted(tags),baselineReason='Scout mechanics heuristic v1: immediate utility, cost and setup; uncertainty ±2 points. No measured win probability.' if key not in overrides else 'Scout utility judgment v1; conservative judgment informed by branch-aware class guides, uncertainty ±2 points.')
        if key in payoffs:
            tag=payoffs[key]
            e['tags'].append('needs:'+tag)
    # One service for later merchant/manual contexts; OCR is rewards only.
    service=dict(id='service:card_removal',name='Card Removal',kind='Service',baseline=5,tags=['deck-thinning'],setupRisk=0,baselineReason='Scout heuristic; removal depends on current deck and price.',facts=dict(normalizedName='cardremoval',aliases=[],character='shared',rarity='Service',type=None,cost=None,starCost=None,target='Deck',rewardPool=False,multiplayerOnly=False,values={},upgrade={},keywords=[],gameVersion=VERSION,sourceId='services'))
    entities.append(service)
    sources=source['sources']+[dict(id='services',url='https://sts2db.com/guides/pathing-deckbuilding/',gameVersion=VERSION,branch='main',sha256=digest(b'Card removal is a merchant service; value depends on deck and price.'),rights='Original factual summary; no source prose retained.')]
    ids=[e['id'] for e in entities];assert len(ids)==len(set(ids))
    names=[(e['kind'],e['facts']['character'],e['facts']['normalizedName']) for e in entities];assert len(names)==len(set(names)), 'duplicate normalized names'
    for c in CHARACTERS:assert sum(e['facts']['rewardPool'] and e['facts']['character']==c and not e['facts']['multiplayerOnly'] for e in entities)>=70
    pack=dict(schemaVersion=2,packVersion='scout-main-0.107.1-20260907.1',gameVersion=VERSION,entities=entities,claims=claims,catalog=dict(branch='main',generatedAt=source['retrievedAt'],sources=sources,strategyMethod='mechanics-v1; docs/data-and-strategy.md; baseline uncertainty ±2 points; context bonuses are original Scout heuristics'))
    write(ROOT/'data/strategy-pack.json',pack)
    print(collections.Counter(e['kind'] for e in entities))
    print('Solo normal reward pools:',{c:sum(e['facts']['rewardPool'] and e['facts']['character']==c and not e['facts']['multiplayerOnly'] for e in entities) for c in sorted(CHARACTERS)})

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--fetch',action='store_true');p.add_argument('--raw-dir',type=Path);p.add_argument('--accept-source-change',action='store_true');a=p.parse_args()
    if a.fetch or a.raw_dir:
        raws={key:(a.raw_dir/('sts2-'+key+'.json')).read_bytes() if a.raw_dir else fetch(key) for key in ['cards','relics','potions']}
        lock=ROOT/'data/catalog/facts.json'
        if a.fetch:
            news=json.loads(fetch('news'))['items']
            main=[n for n in news if n.get('feedname')=='steam_community_announcements' and 'Beta' not in n['title'] and re.search(r'v0\.\d+\.\d+',n['title'])]
            assert main and VERSION in main[0]['title'], 'New main patch: research and update VERSION and main sentinels before importing'
        if lock.exists() and not a.accept_source_change:
            previous=json.loads(lock.read_text())
            expected={s['id']:s['sha256'] for s in previous['sources']}
            assert all(digest(value)==expected[key] for key,value in raws.items()), 'Source changed: review diff/version, then explicitly --accept-source-change'
        import_data(raws,datetime.datetime.now(datetime.timezone.utc).isoformat())
    generate()
