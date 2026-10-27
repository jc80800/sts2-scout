# Ironclad card-reward research (STS2 main branch 0.107.1)

Research date: 2026-10-25. Scope: Ironclad three-card rewards, plus colorless cards where they can appear in rewards. This is research and algorithm design; no Scout code was changed. Raw downloaded pages and the throwaway engine port used for the critique were kept outside the repository.

**Evidence labels used throughout**

| Label | Meaning |
|---|---|
| **[M]** | Verified mechanic: stable 0.107.1 card or relic text from the Spire Codex stable API, or official patch notes |
| **[E]** | Named expert opinion (Baalorlord, Jorbs, Nat1Gaming), attributed and dated |
| **[D]** | Observational data from community run uploads. It shows association, not causation, and is always confounded by player skill, run state and who chose to pick the card |
| **[C]** | Unnamed community or forum opinion |
| **[H]** | Scout's own hypothesis. Every number in this category is an **initial tuning hypothesis, not a measured fact** |

---

## 0. Headline findings

1. **The patch is still 0.107.1, but most fresh community content is not.** Main is still v0.107.1. Beta v0.109.0, v0.110.0 and v0.111.0 (July–August 2026) changed several Ironclad reward cards, and much 2026-H2 data and commentary comes from beta players. Section 1 lists every conflict.
2. **The current engine's deck-size penalty never affects which card wins.** `-0.04 × deckSize` is identical for all three offers, so it can only matter against Skip, and Skip doesn't exist yet. The same applies to the freshness discount, which multiplies every score by 0.9.
3. **Most current ranking errors come from tags, not weights.** Headbutt is tagged `draw`+`selection`, Havoc `draw`, and Anger `selection`. Self-exhausting cards (Impervious, Dominate, Tremble) take an "exhaust conflict" penalty meant for cards that exhaust *other* cards. Dismantle, Bully, Cruelty and Colossus aren't recognized as Vulnerable payoffs. Barricade, Juggling, One-Two Punch, Stampede and Stone Armor have no tags, so they dodge every penalty.
4. **Count-based saturation starts too early.** The starter deck already has 6 `damage`-tagged cards against a target of 3, so every attack gets −1 from floor 1. Meanwhile `draw` and `selection` targets are fixed at 2 for the whole run, so a mis-tagged card gets +2 per tag.
5. **When a card is picked matters as much as which card.** The two experts agree on this, and both large datasets show it. Exhaust and Block engine pieces (Feel No Pain, Dark Embrace, Burning Pact, True Grit, Body Slam) are among the worst Act 1 picks and among the few positive Act 3 picks in Untapped's A7+ data [D].
6. **Skip is a real choice, and its value rises over the run.** About 5% of rewards are skipped after the first fight, rising to about 16–22% by the sixth Act 1 reward [D, Gamer of Passion]. In Untapped's data, 56 of 80 Ironclad reward cards have a negative Act 3 run-win-rate delta when picked [D].
7. **Colorless cards cannot appear in ordinary rewards** without Dingy Rug ("Card rewards can now contain Colorless cards" [M]) or specific events [wiki.gg]. Scout's OCR currently admits all colorless cards as reward candidates.
8. **The most valuable missing inputs** are, in order: current/max HP, floor or reward index within the act, relics, and the act boss (visible on the map). Potions and map details come later.

---

## 1. Patch compatibility

### 1.1 What is current

| Item | Status | Evidence |
|---|---|---|
| Main branch | **v0.107.1** (Major Update #2) | [Steam: Major Update #2](https://store.steampowered.com/news/app/2868840/view/710026912607505280); [SpireBuilds roadmap](https://www.spirebuilds.com/guides/slay-the-spire-2-next-update-roadmap), checked by its author 2026-10-14: "Main v0.107.1 and optional Beta v0.111.0" |
| Scout catalog | 0.107.1, retrieved 2026-09-06 | `data/source-manifest.json` |
| Spire Codex stable `cards` endpoint on 2026-10-25 | **Byte-identical to Scout's snapshot** (SHA-256 `fa908e4c…39b8`, 577 cards) | Re-downloaded and hashed during this research |
| Beta branch | v0.109.0 (2026-07-17), v0.109.1 hotfix, v0.110.0 (2026-07-30), v0.111.0 (2026-08-14) | [patched.gg v0.111.0](https://patched.gg/games/slay-the-spire-2/beta-patch-notes-v01110), [patched.gg v0.110.0](https://patched.gg/games/slay-the-spire-2/beta-patch-notes-v01100), [patched.gg v0.109.0](https://patched.gg/games/slay-the-spire-2/beta-patch-notes-v01090), [PCGamesN v0.109](https://www.pcgamesn.com/slay-the-spire-2/patch-notes-july-17), [Spire Codex v0.109 recap](https://spire-codex.com/guides/v0109-patch-recap) and [v0.110 recap](https://spire-codex.com/guides/v0110-patch-recap) |

### 1.2 Beta changes to Ironclad reward cards: do not merge these with 0.107.1

| Card | 0.107.1 stable [M] | Beta change | Consequence for Scout |
|---|---|---|---|
| Demon Form | 3-cost, +2 (3) Strength per turn, Rare | v0.109: +3 (4) | Beta-era praise of Demon Form does not transfer |
| Expect a Fight | 2 (1)-cost, gain 1 energy per Attack in hand, then no more energy this turn | v0.109 removed the energy lock; v0.111 **reworked** it to a 3-cost skill: 15 (16) Block + 5 (8) per Strength | Three different cards share one name. Jorbs's Untapped note ("You don't need much Strength for this to be good") reads like the v0.111 version. Baalorlord's Whirlwind note (turn damage into energy "alongside cards like Expect a Fight") fits stable. **Do not use either note without a version check** |
| Forgotten Ritual | Energy 3 (4) only if you Exhausted a card this turn | v0.111: unconditional | Stable version is a conditional Exhaust payoff |
| Rampage | 9 damage, +5 (+9) per play | v0.111: 10, +5 (+10) | Minor |
| Mangle | 15 (20) damage | v0.110: 20 (26) | Beta data overrates the stable version |
| Pact's End | 17 (23) | v0.110: 18 (24) | Minor |
| Bloodletting | Common | v0.109: Uncommon | Offer frequencies differ |
| Taunt | Uncommon, 7 (8) Block, 1 (2) Vulnerable | v0.109: Common, 6 (8) Block | Offer frequencies and block values differ |
| Cruelty / Dominate | Cruelty Rare, Dominate Uncommon | v0.109: rarities swapped | Rare-reward analyses from beta differ |
| Juggling | as written | v0.110 fixed copying auto-played attacks | Behavior may differ |
| Rend (colorless) | 2-cost, 15 + 5 (8) per unique debuff | v0.111: 1-cost, 10 (12) | |
| Card-reward rarity | as shipped | v0.110: "Fixed Uncommon cards not showing up as often as intended" | Offer distributions in mixed-patch data are inconsistent |

Relics also changed on beta: Toasty Mittens (v0.110), Fur Coat (v0.110: 8 combats; stable text says 7 [M]) and Diamond Diadem (v0.109).

### 1.3 Sources that mix versions or games

- **Untapped card pages** show a "v0.111.0" banner sitewide. Their stats are labelled "A7+ Ironclad Singleplayer Runs" but don't state a patch range. Treat them as **mixed or unknown patch**. One data-quality warning: Tank (multiplayer-only per Spire Codex) shows 1,300 Act 1 single-player reward offers.
- **Baalorlord's tier list** page title says "v0.111.0", but its body says "Last Updated: August 21, 2026 · v0.107.1 · Main Branch", and its card texts match stable. Treat it as 0.107.1, with that ambiguity noted.
- **Gamer of Passion** (2026-10-03) pools v0.98–v0.111, which includes beta. It also checks v0.108–v0.111 separately, and that subset is mostly beta.
- **Spire Codex tier list/boss guide** (2026-07-15/16): "main branch" as of mid-July 2026, which is v0.107.1.
- **Slay the Spire 1 contamination.** Several SEO guide summaries recommended Heavy Blade, Flex, Spot Weakness, Limit Break or Grapple. None of these is in Scout's 0.107.1 Ironclad reward pool. STS1 Steam threads (app 646570) dominate search results for "skip" and "deck size". **Only cite STS1 material for general principles, never for card-specific claims.**
- Clash, Dual Wield and Entrench appear on Baalorlord's list but are **event cards** [M]. Maul, Whistle and Relax (in beta notes) are **Ancient event cards**. None of them is a reward card.

---

## 2. Evidence-backed strategy brief (Ironclad)

### 2.1 Core framework: cards do jobs, and a deck's needs change by stage

- **Jobs.** Jorbs groups cards by job: frontloaded damage, frontloaded AoE, frontloaded block, scaling, and card draw/deck manipulation [E, [Jorbs, 2026-03-06](https://sts2.untapped.gg/en/articles/slay-the-spire-deckbuilding-strategy-solving-the-spire-with-jobs)]. His key heuristic is to judge a card by the fights you are about to face, not by its late-game ceiling. He suggests adding "two or three Frontloaded Damage cards" at the start of each run: fewer with a damage relic, one more if you plan many Act 1 elites. The article predates STS2's release and uses STS1 examples, so the specific numbers are a hypothesis for STS2.
- **Frontload versus scaling.** "To get scaling, you must sacrifice frontload" [E, [Baalorlord, 2026-03-04](https://sts2.untapped.gg/en/articles/core-deckbuilding-concepts-in-slay-the-spire)]. Damage per turn is the first thing to evaluate because every fight needs it, while block's value varies by fight.
- **Data agrees on timing.** "In Act 1, cards that help in the fight you are in win more runs. Cards that need setup before they do anything lose runs when you take them early" [D, [Gamer of Passion, 2026-10-03](https://gamerofpassion.com/slay-the-spire-2-card-win-rates/); 1,146,495 solo runs, v0.98–v0.111]. Its method compares runs offered the same card with similar runs (same character, ascension, version and floor) and scales by pick rate. This is better than raw "win rate when in deck", but still observational. **It explicitly does not rate Skip.**
- **The timing flip in Untapped's A7+ data [D].** Feel No Pain −8% Act 1 act-win-rate delta vs +4% Act 3 run delta; Dark Embrace −10% vs +6%; Burning Pact −7% vs +2%; Body Slam −10% vs +4%. Frontload attacks move the other way: Dismantle +4% Act 1 but −14% Act 3; Hemokinesis −2% vs −16%; Twin Strike +2% vs −10%. Untapped doesn't publish the exact definition of these deltas, so use only their direction.

### 2.2 Ironclad principles, exceptions and sources

| # | Principle | Exceptions / disagreement | Evidence |
|---|---|---|---|
| P1 | Early Act 1 needs frontload damage first; block becomes more important later | Block still matters in Act 1 at high ascension ("A9 is mostly an Act 1 problem": only ~40% of A9 runs leave Act 1 [D, GoP]). An existing damage relic or potion lowers the need [E, Jorbs] | [E] Jorbs; [E] Baalorlord; [D] GoP |
| P2 | Conditional payoffs need enough enablers *before* they're good. Feel No Pain "becomes a great card once you have 3 or 4 sources of Exhaust" (Baalorlord, quoted by GoP); Vicious: "You only need 2 or 3 sources of Vulnerable to make this excellent" | **Vicious is disputed.** Baalorlord rates it S; Jorbs: "not an awesome card to have in your deck against tough fights in Act 1" [E, Untapped Vicious page]. Act 1 data sides with caution (−5.4% GoP; −10% Untapped Act 1). With enablers present, the data can't say who is right | [E] both; [D] GoP, Untapped |
| P3 | Cheap draw is broadly good: Battle Trance "0-cost card draw makes this a no-brainer for any deck that doesn't already have a lot of draw effects" [E, Baalorlord]. Cards that draw exactly 1 don't increase cycle time compared with skipping [E, Baalorlord concepts] | Battle Trance prevents further draw that turn [M]: anti-synergy with a second copy and with Dark Embrace/Vicious turns | [E]; [D] Battle Trance +4.9% GoP, Act 1 pick rate 66% |
| P4 | Ironclad energy is scarce, so energy and free plays are valuable when the deck has expensive cards or energy sinks. Demon Form "Often feels far too slow for 3 energy, given the overall scarcity of energy" [E, Baalorlord]. 0-cost cards are great "As long as you've got something to spend your energy on" [E, [Baalorlord 2-cost article, 2026-03-09](https://sts2.untapped.gg/en/articles/in-defense-of-2-cost-cards)] | Pyre: Jorbs says it's "especially valuable on lower ascensions … It is quite slow" [E]. Expensive cards are worth more per draw because each card is seen once per cycle [E, 2-cost article] | [E] |
| P5 | Vulnerable is a real Ironclad axis with many payoffs: Dismantle, Bully, Dominate, Cruelty, Colossus, Molten Fist and Vicious [M]. "Getting Vulnerable onto enemies reliably is one of the first things Ironclad decks want to do" [E, Jorbs on Dismantle] | Payoffs without sources are dead. Artifact blocks debuffs ("Beware of enemies with Artifact!" [E, Baalorlord on Dominate]) | [M], [E] |
| P6 | Uppercut is "Ironclad's only source of Weak, which makes this a must for bosses" [E, Baalorlord] | Shockwave (colorless) also applies Weak [M], but only with Dingy Rug/events | [E]; [D] Uppercut +4.5% GoP |
| P7 | Offering is the strongest broad pick: S on Baalorlord, Nat1 and Spire Codex; taken 91% of the time in Act 1 [D, Untapped]; +5.3% [D, GoP] | Costs 6 HP [M]. At very low HP, it's weaker (hypothesis) | Broad agreement |
| P8 | Unmovable and Crimson Mantle are strong defensive rares. Jorbs: Unmovable "Single-handedly makes most decks able to Block almost every attack"; Crimson Mantle has the largest Ironclad early-pick value in GoP (+5.7%) | Small rare samples (~5,300 Act 1 offers) | [E], [D] |
| P9 | Block density about 33% is a reasonable target for high-level play. Baalorlord's hypergeometric figures for a 30-card deck drawing 5 reproduce exactly: 89.1% chance of at least one block at 10/30, 70.2% at 6/30, and a 16.5% chance of a nearly all-block hand at 15/30 | "Not every run wants the same amount of block"; decks that draw more tolerate more or less [E] | [E] plus verified arithmetic |
| P10 | Upgrades: aim for 33–50% upgrade density in winning high-difficulty runs; "Adding new upgraded cards is generally quite desirable" [E, Baalorlord concepts]. Some cards are upgrade-dependent: Vicious "A must-upgrade", Whirlwind "Needs an upgrade", Pommel Strike "Really wants an upgrade", Infernal Blade "Much nicer with an upgrade to 0-cost", Havoc "a lot more desirable if you can upgrade it". Feed: "Don't upgrade this card" [E, all Baalorlord] | | [E] |
| P11 | Some cards are traps without support. Havoc: "a bad floor 1 pick" [E, Baalorlord], F [E, Nat1], Act 1 pick rate 4%. Cinder: random exhaust (D [E, Nat1]; Spire Codex "trap"). Juggling/Drum of Battle need a specific deck [D, GoP −9.3%/−7.3%] | Juggling's Act 3 delta is slightly positive (+1%) [D] | [E], [D] |
| P12 | Strong frontload commons fade. Anger is a strong Act 1 pick (+5% act) but strongly negative after Act 1 (Act 2 pick rate 6%, −8%) [D]; similar for Breakthrough, Twin Strike and Iron Wave. Jorbs: commons like Breakthrough "are in charge of getting you that far" | Strength decks keep multi-hit commons (Sword Boomerang "ends up scaling very well with Strength … even in Act 3" [E, Jorbs]) | [E], [D] |
| P13 | Rares and boss rewards: forum players argue that skipping the Act 1 boss reward is almost always wrong (paraphrased), and that a rare can justify pushing the deck toward it [C, [Steam thread, Aug 2026](https://steamcommunity.com/app/2868840/discussions/0/592939664045771026/?l=english)] | Disputed by the thread's author. The experts rate many Ironclad rares low (Baalorlord: Demon Form D, Hellraiser C, Tank D) | [C], [E] |
| P14 | Corruption is an Ancient card in 0.107.1 [M], not a reward card. "Because of its rarity, Corruption doesn't feel very strong in this game" [E, Jorbs] | Nat1 rates it A | [M], [E] |

### 2.3 Notable disagreements (preserve them; don't average them away)

| Card | Positions |
|---|---|
| Demon Form | Baalorlord D (too slow for 3 energy) · Nat1 A (per page summary: the ultimate scaling option, at the cost of a 3-energy turn) · Untapped Act 1 +11% act, but Act 2 −4% and Act 3 0% [D] · buffed on beta (so the devs likely saw it as weak) |
| Dark Embrace | Baalorlord S · Jorbs "a bit slow for this game" · Nat1 D · Untapped −10% Act 1, +6% Act 3 [D] |
| Vicious | Baalorlord S vs Jorbs C-tier caution; Act 1 data negative |
| Break | Baalorlord S, Nat1 S vs Spire Codex "trap" (~31% win rate). Break comes from Ancient transforms (e.g. Archaic Tooth), so its win rate is confounded by how it's obtained. **Not a reward card**; excluded from scenarios |
| Blood Wall | Jorbs S vs Nat1 C (per GoP); data unstable across ascension/patch [D] |
| Feel No Pain | Jorbs "class-defining" vs Act 1 data −5.9% (GoP). Both can be true: it's an Act 2–3 engine card |

### 2.4 Evidence gaps

- Reddit and video evidence were added on 2026-10-26; see §10. The first pass could not reach either: Reddit's JSON API and Anthropic's fetch tools are blocked, and no transcript tool was installed. The second pass used Reddit RSS, the Arctic Shift Reddit archive and `yt-dlp` auto-captions.
- Untapped's win-rate delta definitions and patch range are unpublished.
- **No source rates Skip directly.** GoP says so explicitly.
- Rare-chance mechanics (the STS1-style "rare offset") are not verified for STS2. A wiki page describes it, but search results mixed STS1 fandom pages with it.
- Several timing questions need in-game verification. Does Crimson Mantle's start-of-turn HP loss trigger Rupture ("on your turn")? Baalorlord says the two are "Nice" together, but the rules weren't verified. Does a second Unmovable stack?

---

## 3. Critique of the current engine

To make the critique concrete, `DeckRanking.Rank` was ported line by line to a throwaway script (including the ordinal ID tiebreak; the freshness multiplier was omitted because it can't change order) and run on the 20 scenarios in §6. Current-engine picks are listed there; it matches the preferred or acceptable answer in **8 of 20**. Those scenarios were written by this research, so the number is illustrative, not a benchmark.

| Problem | Where in `DeckRanking.cs` | Effect observed | Fix (rule) |
|---|---|---|---|
| **Deck-size penalty is the same for every offer** | `Add(-owned.Length * .04, …)` | It can't change which offer ranks first; it only shifts all scores. With no Skip option, deck size has zero effect on the decision | R1, R7 |
| **Blanket duplicate penalty** (−1.5 per copy) | `Add(-copies * 1.5, …)` | Dismantle #2 in a Vulnerable deck ranks last (S05). Pommel Strike+ #2 ranks last (S14). Barricade #2 (completely dead [M]) takes the same −1.5 as a good duplicate (S06) | R6 |
| **Count-based saturation ignores magnitude** | `target = max(3, N/4)` for damage/block; 2 for draw/selection | Strikes count the same as Bludgeon. The starter deck already exceeds the damage target, so every attack gets −1 from floor 1. Draw and selection stay "needed" at a fixed 2 | R2, R3 |
| **Mis-tags drive picks** | Catalog tags | Headbutt (`draw`,`selection`) wins S05 and S11. Havoc (`draw`) wins S04. Anger (`selection`) wins S01, the right pick for the wrong reason | R5 (role annotations) |
| **Self-Exhaust treated as removing your tools** | `exhaust` tag → −0.5 "local conflict" | Impervious, Dominate, Tremble, Offering and Molten Fist are penalized; they only exhaust themselves [M] | R5 |
| **Payoff and enabler confused** | Ashen Strike and Pact's End tagged `exhaust` (enabler), not payoffs; Corruption tagged `exhaust-payoff` | Ashen Strike is credited as an enabler and penalized as a conflict | R5 |
| **`conditional` → setupRisk = 1 for every card** | `refresh_catalog.py` | Flame Barrier's unconditional 12 Block is risk-penalized. Dismantle and Dominate are penalized even when the deck supplies Vulnerable | R4 |
| **Untagged cards escape penalties** | Barricade, Juggling, One-Two Punch, Stampede, Stone Armor, Primal Force, Cruelty have no tags | Juggling ranks first in S18 and Infernal Blade first in S14 | R13 |
| **Single Vulnerable/Exhaust counts** | `Count("vulnerable") * 1.2` | Bash (2 turns, 2 energy) counts the same as Tremble (3 turns, 1 energy). No frequency, duration or energy model | R4 |
| **Upgrade value is a flat +1** | `if (choice.Upgraded) Add(1, …)` | Pommel Strike+ (draw 2) gets the same +1 as Strike+ | R11 |
| **No Skip** | — | Can't recommend skipping in S03, S04 or S18 | R1 |

---

## 4. Candidate rules

The format for each rule: purpose · inputs · activation · effect (including Skip) · why · exceptions/conflicts · example / counterexample · sources · patch · confidence. "Weights" are **initial tuning hypotheses [H]**.

### 4A. Rules usable with current inputs (deck with upgrades, act, ascension, three offers)

**R1 — Skip is a ranked option**
- *Purpose:* make "none of these" a first-class answer.
- *Inputs:* the marginal score of each offer (R2–R11).
- *Activation:* always.
- *Effect:* Skip has value 0, because the deck is unchanged. Recommend Skip when the best offer is below a stage threshold θ: [H] θ = −0.5 for the first three rewards of Act 1, 0 for the rest of Act 1, +0.5 in Act 2 and +1.0 in Act 3, in the same units as R2.
- *Why:* every added card slows the redraw of key cards [E, Baalorlord: "always consider whether you would like to skip"]. Skip rates rise through Act 1 [D, GoP], and most Act 3 picks are associated with lower run win rates [D, Untapped].
- *Exceptions:* boss and elite rare rewards (R12); Act 1 frontload deficit (R2 dominates).
- *Example:* S03, a working Act 2 engine offered three weak commons → Skip. *Counterexample:* S01, floor 1: any frontload attack beats Skip.
- *Sources:* Baalorlord concepts; GoP; Untapped. *Patch:* principle is patch-independent. *Confidence:* high for the principle, low for θ.

**R2 — Job-need ledger by stage**
- *Purpose:* convert "this deck needs X" into scores.
- *Inputs:* deck, act; reward index once available (N2).
- *Activation:* always.
- *Effect:* compute deck capability per job (frontload damage, AoE, block/mitigation, scaling, draw/cycle, energy) from **magnitudes** (damage and block per play, cost, hits), not tag counts. Need weights by stage [H]:

  | Stage | Frontload dmg | AoE | Block | Scaling | Draw/cycle | Energy |
  |---|---|---|---|---|---|---|
  | Act 1, rewards 1–3 | 1.0 | 0.5 | 0.5 | 0.2 | 0.4 | 0.3 |
  | Act 1 later | 0.8 | 0.5 | 0.7 | 0.5 | 0.5 | 0.4 |
  | Act 2 | 0.5 | 0.5 | 0.9 | 0.8 | 0.6 | 0.5 |
  | Act 3 | 0.3 | 0.4 | 0.9 | 1.0 | 0.7 | 0.5 |

- *Why:* jobs framework [E, Jorbs]; frontload-before-scaling [E, Baalorlord]; act timing [D].
- *Exceptions:* AoE need is lower when single-target frontload is strong [E, Jorbs].
- *Example:* S01 Anger/Twin Strike. *Counterexample:* S11, Act 2 with ample frontload: a scaling card wins.
- *Confidence:* framework high; weights low.

**R3 — Marginal value with diminishing returns (prevents double counting)**
- *Purpose:* reward filling gaps and stop piling on already-covered jobs.
- *Inputs:* R2 capabilities.
- *Effect:* score = Σⱼ wⱼ · [Uⱼ(Cⱼ(D ∪ c)) − Uⱼ(Cⱼ(D))]. Uⱼ is concave, for example Uⱼ(x) = min(x, Tⱼ) + 0.25 · max(0, x − Tⱼ) [H], where Tⱼ is the stage target. Recompute deck capability with the card added rather than adding per-tag bonuses.
- *Why:* evaluating the whole deck once makes saturation, dilution and duplicates fall out of one calculation, instead of three overlapping penalties.
- *Conflicts:* replaces the current per-tag `(target − count)/target` lines.
- *Confidence:* medium (standard approach; untested here).

**R4 — Condition reliability for conditional effects**
- *Purpose:* separate real synergy from hoped-for synergy.
- *Inputs:* enabler roles in the deck (R5).
- *Activation:* the card has a conditional component.
- *Effect:* multiply only the conditional component by reliability r ∈ [0, 1]; unconditional parts score at full value (Flame Barrier's 12 Block is unconditional). Initial r [H]:
  - **Vulnerable payoffs** (Dismantle, Bully, Dominate, Cruelty, Colossus, Vicious, Molten Fist): r = min(1, S/3), where S counts Vulnerable sources weighted by uptime: Tremble 1.0, Bash 0.8 (+0.2 if upgraded), Taunt 0.6, Uppercut 0.6, Thunderclap 0.6, Dominate 0.5, Shockwave 1.0. The "3" comes from "2 or 3 sources" [E].
  - **Exhaust payoffs** (Feel No Pain, Dark Embrace, Ashen Strike, Pact's End, Evil Eye, Forgotten Ritual): expected exhausts per cycle from enablers (True Grit 1, Burning Pact 1, Brand 1, Second Wind ≈ 2, Fiend Fire ≈ 4, Stoke ≈ 4, self-exhaust cards 1 per combat). r = min(1, sources/3.5) [E: "3 or 4 sources"].
  - **Self-damage payoffs** (Rupture, Inferno, Spite, Tear Asunder): HP-loss-on-your-turn sources (Bloodletting, Hemokinesis, Breakthrough, Blood Wall, Offering, Brand, Inferno; Crimson Mantle *pending verification*). r = min(1, sources/3) [H].
  - **Strike-named** (Hellraiser, Perfected Strike): count cards whose name contains "Strike" [M], including Pommel, Twin, Setup, Ashen, Perfected and basic Strikes.
  - **Attacks per turn** (Juggling's third Attack, Stomp's discount, Rage): expected attacks per turn ≈ 5 × attack fraction, capped by energy.
  - **Block payoffs** (Body Slam, Juggernaut, Barricade): block magnitude and block events per turn.
- *Example:* S07, Vicious with only Bash: r ≈ 0.27 → loses to Pommel Strike. *Counterexample:* S11, Dominate with S ≈ 3: r ≈ 1 → wins.
- *Conflicts:* replaces `setupRisk`, the `vulnerable-payoff` count bonus and the `exhaust-payoff` count bonus.
- *Confidence:* medium (expert-backed thresholds; untuned formula).

**R5 — Role annotations replace extracted tags for scoring**
- *Purpose:* fix mis-tags at the source.
- *Inputs:* a hand-curated, per-patch, reviewed annotation for each reward card: `provides` (vulnerable, weak, exhaustOther:n, selfExhaust, hpLossOwnTurn, block, strength, energy, draw:n, strikeNamed), `consumes` (conditions as in R4), `copyBehavior` (R6), `upgradeDelta` class (R11), `setupTurns`, `unmodeled` flags.
- *Effect:* scoring reads roles only. Extracted tags remain as provenance and for diffing.
- *Why:* verified mis-tags, listed in §3.
- *Confidence:* high (fixes verified errors).

**R6 — Copy behavior instead of a blanket duplicate penalty**
- *Purpose:* recognize good and bad duplicates.
- *Inputs:* `copyBehavior`, copies owned.
- *Effect:* `stackable` (most attacks/blocks): no explicit penalty; R3 saturation handles it. `nonStackingPower` (Barricade [M], Corruption [M]): the second copy's effect is 0, so it's dilution only. `selfLimiting` (Battle Trance: "cannot draw additional cards this turn" [M]): ×0.7 for each additional copy [H]. `copyPayoff` (Feel No Pain: Jorbs, "A Feel No Pain or two"): stackable. `uncertain` (Unmovable, Pyre stacking unverified): label as uncertain in the explanation.
- *Example:* S05 Dismantle #2 is good. *Counterexample:* S06 Barricade #2 is dead.
- *Confidence:* high for text-verified cases, medium elsewhere.

**R7 — Cycle-time dilution, measured against Skip**
- *Purpose:* make deck size matter where it should.
- *Inputs:* deck size, draw effects, the candidate's per-draw quality.
- *Effect:* R3's density-style capability already lowers per-turn output when a below-average card is added. Also add [H] a dilution term −λ · (number of key cards) / (N_eff + 1), where key cards are those with role scaling/engine. N_eff = N − (draw-net cards) − powers − self-exhaust cards, following Baalorlord's re-draw formula. Cards that draw exactly 1 have no dilution cost [E]. Battle Trance and Offering reduce N_eff.
- *Why:* Baalorlord cycle-time and re-draw-time analysis.
- *Example:* S18 Skip. *Counterexample:* S14 Pommel Strike+ (draw 2) reduces cycle time.
- *Confidence:* principle high; λ low.

**R8 — Block-density band**
- *Purpose:* keep mitigation consistent.
- *Inputs:* block-role cards / N.
- *Effect:* treat block density as a sub-target of the block job: target 0.30–0.36 [E: ~33%]. Below 0.25, block need ×1.3 [H]; above 0.45, ×0.6 [H]. Raise the target at ascension ≥ 9 [H, from A9 Act 1 attrition D].
- *Example:* S16 (5/20 = 25%) → Shrug It Off. *Counterexample:* a 45%-block deck → take the attack.
- *Confidence:* medium.

**R9 — Energy curve**
- *Purpose:* value energy only when it will be spent, and value expensive cards only if they can be paid for.
- *Inputs:* card costs; energy sources.
- *Effect:* expected energy demand per turn = Σ(cost × draw probability). Energy cards gain value in proportion to (demand − 3)⁺. 2+-cost candidates lose value in proportion to the shortfall. 0-cost cards gain value only when there is an energy sink [E, 2-cost article].
- *Example:* S12 Bloodletting with 9 cards costing 2 or more. *Counterexample:* S13, low curve: Pyre and Bloodletting are worth less.
- *Confidence:* medium.

**R10 — Setup time by stage**
- *Purpose:* discount effects that need setup turns or enabling cards early in the run.
- *Inputs:* `setupTurns`; stage.
- *Effect:* scaling and engine powers are multiplied by a stage factor [H]: Act 1 rewards 1–3: 0.5; Act 1 later 0.7; Act 2 0.9; Act 3 1.0. This applies after R4, so an unsupported payoff is discounted twice only if it is both slow *and* unenabled, which is intended.
- *Why:* GoP thesis; the Untapped timing flip; Jorbs on Act 1 Vicious and Act 1 Tactician-style cards.
- *Exceptions:* boss is a known long scaling check (N4); immediate-impact powers (Crimson Mantle, Unmovable) have setupTurns ≈ 0.
- *Confidence:* medium-high for direction; low for factors.

**R11 — Upgrade value is per card**
- *Purpose:* value offered upgrades and upgrade dependence correctly.
- *Inputs:* `upgradeDelta` class: large (Pommel Strike draw 1→2, Vicious, Whirlwind, Infernal Blade cost→0, Havoc cost→0, Armaments to all cards, Barricade/Dark Embrace/Hellraiser/Stampede cost −1), normal, or negative (Feed: "Don't upgrade").
- *Effect:* an offered upgraded copy gains its upgrade delta, valued at R3 marginal. Base copies of `large` cards are discounted in Act 3 (fewer upgrades left [H]) by 0.8.
- *Example:* S14 Pommel Strike+.
- *Confidence:* medium.

**R12 — Rare and boss-reward handling**
- *Purpose:* avoid both over-skipping and over-forcing rares.
- *Inputs:* offer rarity; is the reward a boss reward (inferable when all three offers are Rare).
- *Effect:* boss rewards use θ − 0.5 [H], so they are less likely to be skipped. Rares still get no blanket bonus.
- *Why:* forum opinion on boss rewards [C]; but experts rate several rares D/C.
- *Confidence:* low (contested).

**R13 — Unmodeled cards don't win by default**
- *Purpose:* stop untagged cards beating modeled ones.
- *Inputs:* annotation completeness.
- *Effect:* unmodeled components get the median marginal score of same-rarity, same-act offers, with a wide uncertainty band. The card can only be recommended if it leads by more than the band. The explanation says: "Scout can't evaluate One-Two Punch's effect; judge it yourself."
- *Confidence:* high (safety rule).

**R14 — Close-call reporting**
- *Purpose:* be honest when the margin is small.
- *Inputs:* score intervals from R4 reliability (±0.2) and R2 stage uncertainty.
- *Effect:* if the intervals of the top two options overlap, or the margin is under 0.75 points [H], show "Close: A or B" with the deciding job difference ("A if you expect elites next; B if your block is the problem").
- *Example:* S02, S09, S15.

### 4B. Rules that need new inputs

**N1 — HP-aware survival weighting** *(inputs: current and max HP)*
- *Effect:* if HP/max < 0.4 [H], block need ×1.5 and setup factor ×0.7. Self-damage cards cost about 1.5 × their HP loss as a fraction of HP [H]. Not Yet and Feed gain value.
- *Why:* fights 2–3 Act 1 elites and survives → much higher win rate "at every HP level" [D, GoP]. The [Spire Codex boss guide](https://spire-codex.com/guides/boss-guide) (2026-07-16, main branch) warns that entering Act 2 bosses below about two-thirds HP is a common way runs fail (paraphrased).
- *Example:* S10 Flame Barrier at 30/80. *Counterexample:* the same deck at 75/80 → Inflame is fine.
- *Confidence:* medium.

**N2 — Reward index or floor within act** *(input: floor number, or rewards taken this act)*
- *Effect:* selects the R2/R10 stage row and the R1 threshold.
- *Why:* Jorbs's "first 2–3 frontload cards"; skip rates rise by reward index [D].
- *Confidence:* high value, low cost.

**N3 — Directional relics** *(input: relic list; `RunContext.Relics` already exists but `DeckRanking` ignores it)*
- *Effect, by relic [M]:*
  - Dingy Rug: gates colorless cards into reward candidates.
  - Bag of Marbles: Vulnerable source +0.6 (turn 1 only).
  - Paper Phrog: Vulnerable payoff magnitude ×1.17 [H, 75% vs 50% bonus].
  - Self-Forming Clay and Demon Tongue: lower the cost of self-damage cards.
  - Strike Dummy: raises Strike-named value.
  - Charon's Ashes: Exhaust payoff.
  - Red Skull, Vajra and Ruined Helmet: Strength.
  - Pen Nib: rewards big attacks.
  - Toasty Mittens: Exhaust and Strength.
  - Brimstone: rewards short fights [E, Skiffington].
- *Why:* "If the Relic does nothing unless you're playing a specific kind of card … it can be run-defining" [E, [Skiffington, 2026-04-27](https://sts2.untapped.gg/en/articles/directional-relic-signals)]. Baalorlord's notes name Self-Forming Clay, Pen Nib, Gorget and Cloak Clasp as specific enablers.
- *Confidence:* medium-high.

**N4 — Act boss** *(input: the boss shown on the map, one of three per act)*
- *Effect:* a long-fight boss raises the scaling weight and setup factor; a burst boss raises frontload and block.
- *Data:* Vantom (Act 1) averages 9 turns, a scaling check; Aeonglass (Act 3) combines burst and scaling (74 damage in 7 turns); Test Subject averages 9.3 turns; Queen "punishes front-loaded decks" [D, Spire Codex boss guide, main branch].
- *Note:* the claim that the boss is visible on the map comes from the [Untapped map guide](https://sts2.untapped.gg/en/guides/how-to-make-the-best-map-choices-in-slay-the-spire-2) via search summary; verify in 0.107.1.
- *Confidence:* medium.

**N5 — Potions** *(input: potion types)*: a damage or Attack potion lowers frontload need [E, Jorbs: "Maybe I'll skip a Frontloaded Damage card because I have a Damage potion"]. Confidence: low priority.

**N6 — Upcoming map** (elites, campfires, shops): upgrade availability (R11) and removal opportunities. Confidence: lowest priority for the MVP; requires a map adapter.

**N7 — Colorless eligibility in OCR** *(input: relic list or a "special reward" toggle)*: only include colorless cards in reward candidates with Dingy Rug or a known event reward (Brain Leech "Share Knowledge" etc. [wiki.gg, edited 2026-10-17]). This removes the 53 single-player colorless candidates from ordinary rewards, which should reduce OCR confusions.

---

## 5. Proposed scoring approach (offline MVP)

**Model:** a deterministic, interpretable "job ledger with marginal value against Skip".

1. **Annotate** (R5): each reward card has role components with magnitudes, conditions, setup turns, copy behavior and upgrade delta. Annotations are reviewed per patch and stored in the strategy pack with provenance.
2. **Deck capability**: for each job j, Cⱼ(D) = (5 / N_eff) × Σ (component magnitude × reliability r), scaled by an energy feasibility factor min(1, energy supply / expected demand) (R9). Units are "starter-card equivalents per turn": Strike = 1 damage unit, Defend = 1 block unit.
3. **Marginal value** (R3): Δ(c) = Σⱼ wⱼ(stage) · [Uⱼ(Cⱼ(D ∪ c)) − Uⱼ(Cⱼ(D))] × setupFactor(c, stage) − dilution(c) (R7). **Skip = 0.** Recommend argmax over {offers, Skip − θ(stage)}.
4. **No double counting, by construction:** each card effect maps to exactly one job component. Synergies only change a component's reliability or magnitude, never add a separate bonus line. Saturation is computed once on the whole deck. Duplicates go through the same path, so there is no separate penalty.
5. **Unknowns** (R13): an unmodeled component gets a neutral prior plus a wide band. Recommend it only if it clearly leads, and say so.
6. **Close calls** (R14): compute low/high scores by perturbing reliabilities ±0.2 and using adjacent stage rows. If the intervals overlap, report "Close: A or B" with the dominant job difference.
7. **Explanation template:** "Take **Dismantle**: 3 cards in your deck apply Vulnerable (Bash+, Tremble, Taunt), so it hits twice most turns (≈16 for 1 energy). Your frontload is the weakest job at this point in Act 1. Tradeoff: a second copy of a 1-cost attack; block density stays at 31%." Show at most three lines: the top job delta, the reliability or enabler fact, and the tradeoff (dilution, energy, HP).
8. **Community data:** do not import win-rate numbers into scores yet. The patches are mixed or unknown, definitions are unpublished, and the data is observational. Use the data for **evaluation cross-checks** (§6.3) and as a low-confidence tie-breaker at most. This matches the repository's existing policy (`docs/catalog-research.md`).

Phase 2 option: replace step 2's analytic approximation with a seeded Monte Carlo hand sampler (draw 5, greedy play under energy), while keeping the same job ledger and explanations.

---

## 6. Evaluation scenarios

All 20 are **explicitly hypothetical**. They use only 0.107.1 Ironclad reward-pool cards (plus Ascender's Bane and one colorless offer with Dingy Rug). The answers are reasoned judgments, **not empirically proven**. "Current" shows what the ported current engine picks. Data figures are Untapped A7+ deltas [D] (Act 1 = act delta; Act 3 = run delta) unless marked GoP.

Starter deck = 5 Strike, 4 Defend, 1 Bash [M] (Spire Codex character data; 80 HP; Burning Blood heals 6 after combat).

| # | Act / Asc | Deck (size) | Offers | Preferred (acceptable) | Reasoning | Current | Tests |
|---|---|---|---|---|---|---|---|
| S01 | 1 / A0 | Starter (10) | Anger, Twin Strike, Feel No Pain | **Anger** or **Twin Strike** | First reward: frontload [E Jorbs]. FNP has 0 exhaust sources [E Baalorlord "3 or 4"]; Act 1 data FNP −8, Anger +5, Twin +2 | Anger (via mis-tagged `selection`) | Unsupported payoff |
| S02 | 1 / A5 | Starter + Ascender's Bane, Pommel Strike, Twin Strike (13) | Battle Trance, Uppercut, Setup Strike | **Close: Uppercut / Battle Trance** | Uppercut: only Weak source plus Vulnerable [E]. BT: 0-cost draw 3 [E]. Both positive [D]. Prefer Uppercut if the boss or elites are near | Uppercut | Close call |
| S03 | 2 / A10 | Exhaust engine: FNP+, Dark Embrace, True Grit+, Burning Pact, Second Wind, Stoke, Forgotten Ritual, Ashen Strike, Bludgeon, Uppercut, 2 Pommel, Flame Barrier, Shrug, Impervious, Battle Trance, Bash+, 2 Strike, 3 Defend (22) | Twin Strike, Setup Strike, Iron Wave | **Skip** | No unmet job. Each common dilutes the engine. Act 2 data −6/−7/−12 with 2–6% pick rates | Setup Strike | Skip |
| S04 | 1 / A7 | Starter (Bash+) + Bludgeon, Uppercut, Pommel, Flame Barrier, Twin Strike (15) | Havoc, Cinder, Thunderclap | **Skip** (Thunderclap if multi-enemy fights are the known weakness) | Frontload already adequate. Havoc "bad floor 1 pick" [E]; Cinder random exhaust [E Nat1]; no Vulnerable payoffs for Thunderclap. Act 1 data −9/−7/−6 | Havoc (mis-tag `draw`) | Skip; traps |
| S05 | 1 / A3 | Starter (Bash+) + Tremble, Taunt, Dismantle (13) | Dismantle, Shrug It Off, Headbutt | **Dismantle** | Useful duplicate: Vulnerable reliability ≈ 0.8–1. "Very premium early pickup" once Vulnerable is reliable [E Jorbs]; Act 1 +4 | Headbutt; Dismantle last | Good duplicate |
| S06 | 2 / A4 | Barricade, Body Slam, Blood Wall, Shrug, Flame Barrier, Uppercut, Pommel, Bash+, 4 Strike, 4 Defend (16) | Barricade, Impervious, Bludgeon | **Impervious** | Barricade #2 does nothing [M]. Impervious "works even better with Barricade" [E] | Bludgeon | Harmful duplicate; false exhaust conflict |
| S07 | 1 / A0 | Starter + Twin Strike (11) | Feel No Pain, Vicious, Pommel Strike | **Pommel Strike** | Vicious has 1 unupgraded source (needs 2–3 [E]); FNP has 0. Act 1 data Vicious −10, FNP −8 | Pommel (narrowly over Vicious) | Unsupported synergy |
| S08 | 2 / A6 | Bash+, True Grit+, Burning Pact, Second Wind, Brand, Forgotten Ritual, Uppercut, Bludgeon, Pommel, Shrug, 3 Strike, 4 Defend (17) | Feel No Pain, Inflame, Hemokinesis | **Feel No Pain** | 4 exhaust enablers meet "3 or 4 sources" [E]. Act 2 run +3 vs Inflame −8, Hemokinesis −11 | Inflame | Supported synergy |
| S09 | 1 / A10 | Starter + Feel No Pain, Anger (12) | Dark Embrace, True Grit, Bludgeon | **Close: Bludgeon / True Grit**; not DE | DE is a second payoff with 0 enablers and costs 2 ("a bit slow" [E Jorbs]). True Grit enables FNP; Bludgeon is an "Excellent early-game power spike" [E Baalorlord]. Act 1 data Bludgeon +4, True Grit −7, DE −10 (unconditional) | True Grit | Hoped-for archetype |
| S10 | 1 / A9 | Starter + Demon Form, Bludgeon, Twin Strike, Pommel (14); **assume HP 30/80, elite next** | Flame Barrier, Inflame, Rupture | **Flame Barrier** (*needs N1*) | Survival over more scaling at low HP. Unconditional data slightly favors Inflame (+2 vs 0), so this answer depends on HP. Without HP, Scout should flag a close call | Inflame; FB last (setup-risk mis-penalty) | Survival vs scaling |
| S11 | 2 / A5 | Bash+, Tremble, Taunt, Uppercut, Dismantle, Bludgeon, Twin, Pommel+, Flame Barrier, Shrug, 3 Strike, 4 Defend (17) | Dominate, Iron Wave, Headbutt | **Dominate** | 4 Vulnerable sources; "One of the best ways to get Strength" [E]. Act 2 +4 vs Iron Wave −12 | Headbutt; Dominate last | Supported scaling |
| S12 | 2 / A2 | Bash, Bludgeon, Uppercut, Flame Barrier, Blood Wall, Fight Me!, Unrelenting, Mangle, Cinder, 4 Strike, 4 Defend (17) | Bloodletting, Whirlwind, Perfected Strike | **Bloodletting** | 9 cards cost 2 or more; energy is the bottleneck [E Baalorlord] | Bloodletting ✓ | Energy constraint |
| S13 | 2 / A3 | Low curve: Anger, Bully, 2 Pommel, Shrug, Iron Wave, Taunt, Battle Trance, Headbutt, Bash+, 3 Strike, 4 Defend (17) | Pyre, Uppercut, Bloodletting | **Uppercut** (Pyre acceptable) | Energy surplus is less valuable without sinks [E 2-cost article]. Pyre "quite slow" [E Jorbs]; Act 2 Pyre −3, Uppercut +1 | Bloodletting | Energy counterexample |
| S14 | 3 / A8 | 27-card upgraded midrange (Uppercut+, Bludgeon+, Flame Barrier+, Taunt+, Bash+, Unmovable, Impervious, Dominate, 2 Dismantle, Hemokinesis, Pommel, …) | Infernal Blade, Hemokinesis+, Pommel Strike+ | **Pommel Strike+** (Skip acceptable) | Arrives upgraded with draw 2, which shortens the cycle. Infernal Blade wants an upgrade and its random attack is unreliable. All three Act 3 deltas negative (−12/−16/−3) | Infernal Blade (untagged bias); Pommel+ last | Upgrade value; late skip |
| S15 | 1 / A1 | Starter + Pommel (11) | Breakthrough, Stomp, Hemokinesis | **Close: any**, lean Hemokinesis/Stomp | Single-target frontload may solve multi-enemy fights [E Jorbs]. Data signals are weak (−4/+1/−2). Scout should report this as close | Breakthrough (tie broken by ID) | Close call; AoE |
| S16 | 2 / A4 | Attack-heavy, block 5/20 (4 Defend + Iron Wave) (20) | Shrug It Off, Twin Strike, Rampage | **Shrug It Off** | Block density 25% vs ~33% target [E]. Shrug draws, so it adds no cycle cost | Rampage (tie broken by ID) | Block density |
| S17 | 2 / A5 | Bloodletting, Hemokinesis, Breakthrough, Blood Wall, Crimson Mantle, Uppercut, Pommel, Shrug, Bash+, 4 Strike, 4 Defend (17) | Rupture, Inflame, Iron Wave | **Rupture** (low confidence) | 4–5 HP-loss sources [M]. "Getting 10+ Strength from this in a fight is quite doable" [E Jorbs]. Unconditional data Act 2 −7, the cautionary case. Crimson Mantle timing needs verification | Inflame | Supported but data-contradicted |
| S18 | 3 / A7 | 28-card exhaust/block deck, Strikes removed (one "Strike"-named card: Ashen Strike; has Offering and Whirlwind) | Juggling, One-Two Punch, Hellraiser | **Skip** (One-Two Punch acceptable) | Hellraiser has 1 Strike-named card [M]. Juggling needs 3 attacks per turn; the deck is ~10/28 attacks. One-Two Punch has the Offering + Whirlwind line [E Jorbs] | Juggling (untagged bias) | Unsupported synergy; skip |
| S19 | 1 boss / A6 | Starter (Bash+) + Bane + Uppercut, Pommel, Twin, Flame Barrier, Taunt, Bludgeon, Shrug (17) | Offering, Demon Form, Barricade | **Offering** | Broad agreement (§2.2 P7). Demon Form is disputed (§2.3); Barricade lacks block engine support | Offering ✓ | Rare reward |
| S20 | 2 / A2, **Dingy Rug** | Bash+, Twin, Bludgeon, Pommel, Shrug, Dismantle, Flame Barrier, Hemokinesis, Iron Wave, 3 Strike, 4 Defend (16) | Shockwave (colorless), Flash of Steel, Iron Wave | **Shockwave** | AoE 3 Weak + Vulnerable [M] supports Dismantle; experts very positive. No reward stats exist for colorless cards. Without Dingy Rug, this screen should raise an OCR doubt (N7) | Shockwave ✓ | Colorless gating |

Assumptions for all scenarios: single-player, no other relevant relics or potions unless stated, average HP unless stated, deck lists as given, and normal (non-elite) rewards unless stated.

Coverage: skip (S03, S04, S18; acceptable in S14); useful duplicate (S05) and harmful duplicate (S06); unsupported synergy (S01, S07, S09, S18); supported synergy (S08, S11, S17); survival (S10, S16); energy (S12, S13); close calls (S02, S09, S15); upgrades (S14); rares (S19); colorless (S20).

### 6.1 Assessing improvement beyond matching these 20

1. **Freeze a held-out set before tuning.** Have a second author write 40+ scenarios independently, with stratified coverage of the same categories, and store them unseen by the rule author. Tune only on these 20 (the dev set); report held-out results once per release.
2. **Expert or strong-player review.** Show reviewers, blind, Scout's recommendation and explanation versus a baseline (current engine, or "highest-pick-rate card"). Score: decision acceptable? (yes/no/close), explanation factually correct about the deck? and would it change your mind? (1–5). Record disagreements rather than forcing agreement; a split panel should match a Scout "close call".
3. **Replay against real decisions.** GoP shows the Spire Codex community export includes offered cards per reward. Restrict to 0.107.1 build IDs, A7+, single-player. Metrics:
   - top-1 agreement with the player's pick, separately for winning and losing runs (an imitation metric, not optimality);
   - Skip precision and recall;
   - per-act agreement.
   Check license and terms before using the export ([API terms](https://github.com/ptrlrd/spire-codex/blob/main/API_TERMS.md)).
4. **Directional cross-check:** in replay, Scout's high-confidence picks should not be cards with strongly negative stage-specific deltas, unless Scout's reliability factor explains why.
5. **Robustness:** small deck perturbations (swap one Strike for one Defend) should rarely flip a non-close recommendation. Track the flip rate.
6. **Explanation audit:** every numeric fact in an explanation (counts, density, sources) must be reproducible from the deck; unit-test it.
7. **Regression:** keep all scenarios as fixtures. A rule change that fixes one scenario but breaks two needs review, not silent reweighting.

---

## 7. Prioritized implementation proposal (first five)

| Priority | Improvement | Expected benefit | Evidence strength | Inputs | Complexity |
|---|---|---|---|---|---|
| 1 | **Evaluation harness first:** scenario fixtures (these 20 as dev; commission a held-out set), the current-engine baseline, and agreement/close-call metrics | Makes every later change measurable; prevents overfitting | High (method) | None new | Low–medium |
| 2 | **Role annotations for the ~80 Ironclad reward cards** (R5, R6 copy behavior, R11 upgrade class), reviewed against 0.107.1 text; scoring reads roles, not extracted tags | Fixes the verified mis-tags behind most current errors (Headbutt, Havoc, Anger, self-Exhaust, payoff/enabler, untagged bias) | High (text-verified) | None new | Medium (curation plus schema) |
| 3 | **Skip and the marginal-value ledger** (R1, R2, R3, R7, R14): deck-level capability with concave needs, Skip = 0 with stage threshold, close-call band | Delivers "take this because your deck needs X"; deck size finally matters; removes the blanket duplicate penalty | Medium-high (expert principles; untuned numbers) | Current, plus reward index once N2 exists | Medium |
| 4 | **Condition reliability and setup timing** (R4, R10, R13) | Separates real synergy from hope (Vicious, FNP, Dominate, Rupture, Hellraiser); matches the strongest data pattern (timing flip) | Medium-high | Current | Medium |
| 5 | **Three cheap new inputs:** HP and max HP, floor number, relic picker (N1, N2, N3), plus colorless gating from Dingy Rug (N7) | Survival-vs-scaling decisions; correct stage; directional relics; fewer OCR candidates | Medium | Manual entry in the deck editor | Low–medium |

Next after these: act boss (N4), potions (N5), the Monte Carlo hand sampler, then map data (N6).

---

## 8. Items to verify in-game on 0.107.1

- Does Crimson Mantle's start-of-turn HP loss count as "on your turn" for Rupture and Inferno?
- Do Unmovable and Pyre stack when two copies are in play?
- Is the boss visible on the map from the start of each act?
- Is there an STS2 rare-offset mechanic, and does skipping affect it?
- Can colorless cards appear in any other ordinary reward context (e.g. with Lead Paperweight)?
- Exact Battle Trance interaction with Dark Embrace and Vicious draws in the same turn.

---

## 9. Sources

Retrieved 2026-10-25 unless noted. Patch is given where known.

**Mechanics and patches**
- [Spire Codex stable cards API](https://spire-codex.com/api/cards): 0.107.1 stable; hash identical to Scout's 2026-09-06 snapshot.
- [Spire Codex characters](https://spire-codex.com/api/characters/IRONCLAD), [relics](https://spire-codex.com/api/relics), [ascensions](https://spire-codex.com/api/ascensions), [changelogs](https://spire-codex.com/api/changelogs).
- [Steam: Major Update #2, v0.107.1](https://store.steampowered.com/news/app/2868840/view/710026912607505280).
- Beta notes: [v0.109.0](https://patched.gg/games/slay-the-spire-2/beta-patch-notes-v01090) (2026-07-17), [v0.110.0](https://patched.gg/games/slay-the-spire-2/beta-patch-notes-v01100) (2026-07-30), [v0.111.0](https://patched.gg/games/slay-the-spire-2/beta-patch-notes-v01110) (2026-08-14); [PCGamesN v0.109](https://www.pcgamesn.com/slay-the-spire-2/patch-notes-july-17); Spire Codex recaps [v0.109](https://spire-codex.com/guides/v0109-patch-recap), [v0.110](https://spire-codex.com/guides/v0110-patch-recap).
- [SpireBuilds roadmap](https://www.spirebuilds.com/guides/slay-the-spire-2-next-update-roadmap) (checked by its author 2026-10-14).
- [wiki.gg Colorless sources (STS2)](https://slaythespire.wiki.gg/wiki/Template:Colorless_Sources_2) (edited 2026-10-17).

**Expert analysis**
- Baalorlord, [Core Deckbuilding Concepts](https://sts2.untapped.gg/en/articles/core-deckbuilding-concepts-in-slay-the-spire) (2026-03-04, pre-0.107.1, concepts patch-independent).
- Jorbs, [Solving the Spire with "Jobs"](https://sts2.untapped.gg/en/articles/slay-the-spire-deckbuilding-strategy-solving-the-spire-with-jobs) (2026-03-06, uses STS1 examples).
- Baalorlord, [In Defense of 2-Cost Cards](https://sts2.untapped.gg/en/articles/in-defense-of-2-cost-cards) (2026-03-09).
- Dillon Skiffington, [Directional Relic Signals](https://sts2.untapped.gg/en/articles/directional-relic-signals) (2026-04-27).
- [Baalorlord Ironclad tier list](https://sts2.untapped.gg/en/tier-list/004de170-026a-4dd4-a280-3b904be0b5d6) (updated 2026-08-21; body says v0.107.1 Main; title says v0.111.0).
- Untapped card pages, e.g. [Vicious](https://sts2.untapped.gg/en/cards/ironclad/vicious) (Baalorlord and Jorbs notes; A7+ single-player reward stats; patch range unstated). All Ironclad reward-card pages were reviewed.
- Nat1Gaming (DoggertQBones), [Ironclad tier list](https://nat1gaming.com/sts2/tier-list/ironclad-card-tier-list/) (August 13 update, published 2026-08-17).

**Data analyses**
- Gamer of Passion, [Card Win Rates vs Tier Lists: 1.1 Million Runs](https://gamerofpassion.com/slay-the-spire-2-card-win-rates/) (2026-10-03; v0.98–v0.111 mixed; Spire Codex export of 2026-10-02).
- Spire Codex, [Ironclad tier list guide](https://spire-codex.com/guides/ironclad-tier-list) (2026-07-15, main) and [Boss guide](https://spire-codex.com/guides/boss-guide) (2026-07-16, main).

**Community**
- Steam, ["perhaps just a deck building skill issue"](https://steamcommunity.com/app/2868840/discussions/0/592939664045771026/?l=english) (Aug 2026; boss rares and skipping).
- Steam, ["Ironclad lacks options?"](https://steamcommunity.com/app/2868840/discussions/0/798965318966902032/?ctp=1) (2026-03-24, pre-0.107.1; perceived rare dependence, disputed).
- Untapped, [map choices guide](https://sts2.untapped.gg/en/guides/how-to-make-the-best-map-choices-in-slay-the-spire-2) (boss visibility; seen via search summary only).

---

## 10. Addendum (2026-10-26): video and Reddit evidence

**Method.** I downloaded English auto-captions for 24 STS2 strategy videos with `yt-dlp`; transcripts were kept outside the repository. For Reddit, I paged through every post in r/slaythespire and r/sts2 from 2026-03-01 to 2026-10-26 (52,091 posts) using the [Arctic Shift](https://arctic-shift.photon-reddit.com) archive. I kept STS2-flaired threads that discuss Ironclad drafting (310 threads with 25+ comments) and downloaded their full comment trees, with scores. Auto-captions contain transcription errors, so card names were checked against the catalog. Upvotes are treated as attention, not correctness.

**Patch dating.** Main-branch history:
- launch builds (March 2026);
- Major Update #1 (2026-04-16): reworked Dominate, Stoke, Spite and Tremble; added Expect a Fight's energy lock; made Colossus Uncommon; removed Grapple;
- v0.107.1 (2026-06-18): reworked Conflagration and Drum of Battle; buffed Juggernaut, Pact's End and Unrelenting; removed Doormaker.

Material from before April 16 is therefore stale for several cards. Material from April to June mostly matches 0.107.1, except for those six cards. Videos and posts made on beta (v0.109+, e.g. Caleb Gannon, 2026-07-17) are excluded from card-specific claims.

### 10.1 Principles the new evidence adds or strengthens

| # | Principle | Evidence | Patch |
|---|---|---|---|
| V1 | **Deck strength ≈ damage × block × velocity.** Value a pick by how much it raises the *weakest* axis; adding to a strong axis has small marginal value. Multiplicative effects (Vulnerable, Weak, block doubling) scale with what the deck already does. | Caleb Gannon, ["Why Your Decks Fail"](https://www.youtube.com/watch?v=HUZdTFIGiIA) (2026-04-07, 258k views) | principle |
| V2 | **Three-question skip test:** does the card solve an immediate problem, open a synergy *while providing value on its own*, or make other cards in the deck better? If none, skip. | vmService, ["When to Skip Cards"](https://www.youtube.com/watch?v=-NM47Yw79LM) (2026-04-05) | principle |
| V3 | **Act goals.** Act 1: raw card strength; rarely skip; 2–3 frontload damage cards. Act 2: synergistic packages; frontload commons are usually skipped. Act 3: acceleration and consistency (draw, energy); most of the pool is dead weight. "If you can't picture a specific scenario where the card helps, skip it." | vmService; Caleb Gannon; Reddit [1vyd218](https://reddit.com/comments/1vyd218): "Floor one skip is almost always the wrong play" | principle |
| V4 | **Never take a strength dip at high ascension.** Take cards that are good now *and* lead into an archetype (bridge cards), and add payoffs only after enablers exist. Examples: Hemokinesis → Breakthrough → Blood Wall → Rupture; Tremble/Molten Fist/Thunderclap → Vulnerable; self-exhausting cards → Feel No Pain/Ashen Strike. Payoffs that need setup: Cruelty, Dark Embrace, Feel No Pain, Rupture, Vicious, Tear Asunder. Payoffs fine on their own: Inferno, Dominate, Ashen Strike. | Caleb Gannon, ["Winning 85% of A10 Ironclad"](https://www.youtube.com/watch?v=hyt7Dr5rn2Y) (2026-04-24) | mostly valid; Tremble/Stoke text already current |
| V5 | **Diminishing returns per job.** The first 2–3 sources of Vulnerable, Weak, AoE or strength-down are very valuable; later ones much less, unless the deck is built around that mechanic. | Caleb Gannon ("Why Your Decks Fail"); Sim the Spire (V9) | principle |
| V6 | **Describe a deck as "damage by X, block by Y, scaling by Z", not by archetype.** Single cards can be the scaling plan. Draw and energy justify each other, and are a luxury for runs already ahead. Deck-size breakpoints are fight-specific. | Jorbs, ["Five Principles of Deckbuilding"](https://www.youtube.com/watch?v=95h86sLZpCI) (2026-05-14) | principle |
| V7 | **Duplicates are card-specific.** Battle Trance: first copy S, second B, third risky (it shuts off draw). Bloodletting: two upgraded copies are fine. Pommel Strike and Shrug It Off: hard to have too many. Stone Armor: an upgrade or a second copy is worth more than double. Unmovable: multiple copies help (Baalorlord; Reddit [1sffb5j](https://reddit.com/comments/1sffb5j)). Barricade: a second copy is dead [M]. | Caleb Gannon; vmService tier list; Baalorlord tier-list video | mostly valid |
| V8 | **Relics change needs.** Block relics (Anchor, Cloak Clasp, Ornamental Fan, Gorget, Self-Forming Clay + Crimson Mantle) replace block cards. Strength relics raise multi-hit value (Sword Boomerang, Whirlwind, Twin Strike). Energy relics make 3-cost cards playable. | Caleb Gannon; Reddit [1w0067f](https://reddit.com/comments/1w0067f) (Twin Strike, 2026-08-27) | principle |
| V9 | **Simulation evidence (A9 Expectimax, Sim the Spire).** Against the Byrdonis elite, each Twin Strike improves median HP by ~5 (3 copies ≈ 15) with diminishing returns; 2 Twin Strike + 2 Taunt beats 4 Twin Strike, so mixing jobs beats stacking one. Early easy-pool fights: removing Strikes beats removing Defends. Adding Strike + Defend leaves mean HP loss unchanged but widens the variance. Overgrowth rewards offense; Underdocks favours Defend+. | [simthespire.wordpress.com](https://simthespire.wordpress.com/); Reddit [1xcsmyb](https://reddit.com/comments/1xcsmyb) (2026-10-22), [1sze45u](https://reddit.com/comments/1sze45u), [1t0t46c](https://reddit.com/comments/1t0t46c), [1x8bylw](https://reddit.com/comments/1x8bylw) | 2026-04 to 2026-10 builds |
| V10 | **Act 1 has two variants (Overgrowth, Underdocks)** with different needs. Iron Wave, Shrug It Off and Blood Wall are better in Underdocks. Both Act 1s have a four-enemy elite, so an AoE card matters. | Baalorlord, ["Every Ironclad Card in STS2"](https://www.youtube.com/watch?v=POaEE_3GDBQ) (2026-05-26) | valid |
| V11 | **Reward generation (decompiled).** Each reward card rolls rarity, card and upgrade separately; Act 1 upgrade chance is 0%. Rare chance uses a pity counter that starts at −5% and rises for each non-rare card generated, capped at 40%, resetting when a rare appears. Base rare chance is 1.49% at A7+, 3% below that; uncommon is fixed at 37%. Cards are generated whether or not you take them, so **skipping does not change rare odds**. | ShotgunCrocodile, ["How STS2 Chooses Your Rewards"](https://www.youtube.com/watch?v=yBEV6m_uugk) (2026-10-03) | verify against 0.107.1 |
| V12 | **Rare combos are unreliable.** You rarely assemble two specific rares (e.g. Conflagration with Demon Form or Brand), so a rare whose value depends on another rare should be discounted. Feed and Not Yet are "win more": they help only decks already winning their fights. | Baalorlord tier-list video | valid |

### 10.2 Card-level consensus and disagreement added by video and Reddit (0.107.1-compatible sources)

| Card | Agreement | Disagreement |
|---|---|---|
| Offering | Every source: take nearly always, regardless of archetype (Reddit 1sffb5j) | — |
| Dominate | Baalorlord S, vmService S, Caleb S; Reddit (late June): near auto-pick at Uncommon ([1uj9irb](https://reddit.com/comments/1uj9irb)) | Weak against Artifact; Aeonglass has it and is frequent at A10 |
| Colossus, Unmovable, Crimson Mantle, Impervious, Bloodletting, Pommel Strike, Uppercut | Top tier with Baalorlord, vmService, Caleb, Frost Prime | — |
| Demon Form | Low for most: Baalorlord D, vmService C, Caleb "trap unless no scaling", Reddit [1ti4rvb](https://reddit.com/comments/1ti4rvb) | Nat1 A; Untapped Act 1 delta positive |
| Feel No Pain | No blind pick: there are no STS1-style exhaust-giving Act 1 enemies (vmService, Baalorlord); great with 3–4 exhaust sources | — |
| Vicious | Needs Vulnerable sources | Baalorlord S vs vmService B vs Jorbs C |
| Cruelty | Baalorlord S, vmService S | Frost Prime: "dead draw" in Act 2 hallways |
| Stoke | Baalorlord S, Caleb S; Reddit: "the exhaust deck is a Stoke deck" ([1ug58cy](https://reddit.com/comments/1ug58cy)) | vmService B (needs upgrade), Frost Prime high C |
| Barricade, Juggernaut, Body Slam | Mostly low (vmService D, Frost Prime low, Caleb "rare build") | Baalorlord B/A/low-A |
| Fight Me! | Good when the deck has no scaling yet (Reddit [1ulodtm](https://reddit.com/comments/1ulodtm), A10 player with ~200 games) | vmService C, Baalorlord A |
| Setup Strike, Juggling, One-Two Punch | Consensus poor (Reddit [1tg6b2g](https://reddit.com/comments/1tg6b2g)) | Baalorlord rates One-Two Punch A |
| Havoc | Consensus poor floor-1 pick | — |

### 10.3 Implications for the rules

- **V1, V5 and V9** directly support §5's concave job-ledger model. A logarithmic utility over job capability is the simplest form that matches the "volume" framing: the weakest axis gains most, and duplicates on one job show diminishing returns.
- **V2 and V3** support stage-dependent Skip thresholds (R1). Floor-1 skips should be rare.
- **V4 and V12** support conditional reliability (R4) plus an explicit discount for rare-on-rare dependencies.
- **V7** replaces blanket duplicate penalties with per-card copy behavior (R6).
- **V8** raises the priority of relic input (N3).
- **V10** suggests adding the Act 1 variant as a cheap input alongside the floor number.
- **V11** removes the open question about Skip affecting rare odds (pending verification).
