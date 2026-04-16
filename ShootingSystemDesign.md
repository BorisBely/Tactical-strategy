# Shooting System Design

## Purpose
This document fixes the current design of the shooting system so the project can be implemented step by step without losing agreed decisions.

## Core Idea
The shooting system is built from three main layers:

- Weapon: defines how the platform behaves.
- Ammo: defines what exactly is fired.
- Magazine: defines how many rounds are available and how they are loaded.

Separate from this, the unit contributes personal shooting skill, and attachments modify the weapon platform.

## Main Links

### Weapon
Weapon defines:

- `FireRateRpm`: shots per minute in ideal conditions.
- `AimTimeSeconds`: time needed to reach full aiming quality.
- `EffectiveRangeMeters`: distance up to which range itself gives no extra penalty.
- `BaseShotDispersion`: base spread of the weapon platform.
- `RecoilPerShot`: base recoil penalty added by one shot.
- `SemiAutoRecoilMultiplier`: recoil multiplier for semi-auto fire.
- `AutoRecoilMultiplier`: recoil multiplier for automatic fire.
- `Reliability`: scales jam probability (higher reliability → lower per-shot jam factor); see malfunction section.
- `BaseDurability`: larger value → slower growth of normalized wear `Wear01` (0…1) per shot; see wear accumulation.
- `BaseFoulingBudget`: larger value → slower growth of normalized fouling `Fouling01` (0…1, 100% max) per shot; see fouling accumulation.
- `WearJamStartThreshold`: optional extra gate on normalized wear (0…1). `InverseLerp(threshold, 1, Wear01)` must be greater than 0 for wear-channel jam stress. **0** = rely only on integrity tier bands `C` below.
- `FoulingJamStartThreshold`: same for fouling vs `Fouling01`. **0** = rely only on fouling tier bands `F`.
- `WearJamInfluence`: global multiplier on **wear-channel** jam probability per trigger pull.
- `FoulingJamInfluence`: global multiplier on **fouling-channel** jam probability per trigger pull.
- `SupportedCaliber`: ammo caliber compatibility.
- `SupportedMagazineType`: magazine compatibility.
- `AvailableFireModes`: allowed fire modes.
- `DefaultFireMode`: initial fire mode.
- `AttachmentSlots`: supported attachment slots.

Weapon does not define damage directly.

### Ammo
Ammo defines:

- `Caliber`: caliber type.
- `BaseDamage`: base damage to an unprotected target.
- `Penetration`: penetration capability.
- `ArmorDamage`: damage dealt to armor.
- `ProjectileCount`: projectile count per shot.
- `Velocity`: initial projectile velocity.
- `EffectiveRangeMeters`: effective range of the round itself.
- `SpreadModifier`: modifies current-shot spread.
- `RecoilModifier`: modifies recoil accumulation.
- `WearPerShot`: base wear units added per successful shot; actual Δ`Wear01` = `WearPerShot * attachmentWearProduct / Weapon.BaseDurability`.
- `FoulingPerShot`: base fouling units per shot; Δ`Fouling01` = `FoulingPerShot * attachmentFoulingProduct / Weapon.BaseFoulingBudget`.
- `JamRiskModifier`: multiplies **both** wear- and fouling-channel jam probability for that shot (with magazine and attachment jam products).

Ammo defines the damaging effect. Weapon only delivers the ammo.

### Magazine
Magazine defines:

- `MagazineType`: compatibility category.
- `SupportedCaliber`: which rounds can be loaded into it.
- `Capacity`: maximum number of rounds.
- `RoundLoadTimeSeconds`: time to manually load one round into the magazine outside combat.
- `ReloadTimeModifier`: how this magazine affects reload speed in the weapon.
- `JamRiskModifier`: how this magazine affects malfunction risk.

Magazine does not define damage or weapon behavior outside feeding.

### Attachments (modules on the weapon)
Attachments are modifiers and behaviors on the **weapon platform**. They are **not** magazines: **magazines stay a separate system** (`MagazineDefinition`, insert/eject, capacity, magazine jam modifier). Do not model the magazine as an `EquippedAttachments` slot.

#### Slot rules (agreed)
- **Default:** at most **one** equipped item per logical slot group on a given weapon instance.
- **Exception:** **flashlight / LCU (laser)** share a rail family: a weapon may define **up to 4** such slots (still at most one module **per** rail slot; e.g. slot A = flashlight OR laser, not both).
- Equip validation: module must match slot type; weapon must expose that slot in data.

#### Slot groups and example modules
| Slot group | Examples | Notes |
|------------|----------|--------|
| **Muzzle** (barrel end) | Suppressor, compensator, flash hider | One muzzle device. Compensator / flash hider: **recoil** handling. Suppressor: **recoil** + **fire sound override** (see audio below). |
| **Under-barrel** | Foregrip, bipod, under-barrel grenade launcher | One under-barrel module. Foregrip: **recoil**. **Bipod:** recoil bonus **only when deployed**; full “deploy” mechanic **later** — **until then, treat as deployed when the unit is prone**. UGL: fire grenade — **later**. |
| **Rail** (above barrel / sides; up to 4) | Flashlight, LCU | **All modules** affect **aim time** (positive or negative per item). Flashlight: **light** (visual: child prefab on named anchor). LCU: **laser beam** (visual). |
| **Optic** | Sight / optic | **Aim time** + **separate multiplier** for **spread / range penalty** at distance (designer-tuned; not the same as `EffectiveRangeMeters` only). |
| *(excluded)* | Magazine | Handled only by the **magazine** pipeline, not attachment slots. |

#### Aim time
- **Every** attachment item contributes a delta to aim ergonomics (some help, some hurt).
- **Combining handling contributions from attachments (aim time, attachment-driven recoil deltas, attachment-driven dispersion deltas):** use **`1 + Σ(bonus)`** where each module supplies a designer `bonus` (can be negative).  
- **Note:** wear/fouling **per-shot multipliers** and jam-risk **products** on attachments may stay **multiplicative (Π)** until explicitly redesigned — they are condition math, not ergonomic stacking.

#### Audio (suppressor + subsonic)
- **Suppressor module:** for now a **single** fire sound override on the module (replaces default weapon fire sound when firing suppressed).
- **`Subsonic` (or equivalent) flag on ammo:** also changes fire sound (typically **quieter**). **If both suppressor and subsonic apply,** use the agreed quietest / combined rule in implementation (stack for extra suppression).

#### Runtime / data hook
- `WeaponDefinition` lists slots via `WeaponAttachmentSlotDefinition`: `SlotType` (`Muzzle`, `UnderBarrel`, `Rail`, `Optic`), `IsRequired`, `AnchorChildName` (child on weapon prefab for module prefab parenting). Repeat `Rail` up to four times for four rails.
- `WeaponRuntimeState` holds `WeaponAttachmentDefinition[]` (or a slot-keyed structure once implemented). Until equip UI/pipeline exists, defaults keep behavior unchanged.
- **Visuals:** module `ItemDefinition` can reference a prefab spawned as a child under a **named anchor** on the weapon prefab (same idea as `LeftHandIkTarget` naming).

## Unit Contribution
The unit supplies the human factor of shooting.

Expected unit-side factors:

- base marksmanship
- aiming skill
- recoil control skill

This means the same weapon should perform differently in different hands.

## Effective Range Rule
The agreed rule:

- Up to `EffectiveRangeMeters`, no extra range penalty is applied by the weapon.
- After that point, a strong additional range penalty starts growing.

This means:

- inside effective range, hit quality is mainly affected by unit skill and current state
- beyond effective range, the weapon begins to lose effectiveness sharply

Player orders may force units to fire at any distance, but performance should degrade strongly after effective range.

Vision still limits autonomous target acquisition.

## Recoil Model
Recoil is not direct damage or direct spread. It is a penalty that accumulates over repeated fire and worsens future shots.

### Base Formula

`RecoilAddedPerShot = Weapon.RecoilPerShot * FireModeMultiplier * Ammo.RecoilModifier * AttachmentRecoilModifier`

Where:

- `FireModeMultiplier` is semi or auto
- `AttachmentRecoilModifier` is the combined modifier from active attachments

Then:

`CurrentRecoilPenalty += RecoilAddedPerShot`

And over time:

`CurrentRecoilPenalty` decays back down using a recovery value.

### Meaning of Terms

- `RecoilPerShot`: base recoil load from the weapon platform itself
- `SemiAutoRecoilMultiplier`: reduced recoil accumulation in single fire
- `AutoRecoilMultiplier`: increased recoil accumulation in automatic fire
- `Ammo.RecoilModifier`: ammo-specific recoil modifier
- `AttachmentRecoilModifier`: platform control changes from attachments

## Spread Model
Spread is the quality of the current shot, not the accumulated control penalty itself.

### Base Formula

`ShotDispersion = Weapon.BaseShotDispersion * Ammo.SpreadModifier * MovementModifier * StanceModifier * AimModifier * RangeModifier * RecoilPenaltyToDispersion`

This keeps spread and recoil related, but not identical:

- `SpreadModifier` affects the current shot directly
- `RecoilModifier` affects how much recoil penalty is added
- the accumulated recoil penalty then worsens future shots

## Aiming Model
Aiming should not be binary.

Weapon should have an `AimProgress` that rises over `AimTimeSeconds` until full aiming quality is reached.

This is affected by:

- base weapon aiming speed
- attachments
- movement
- stance
- weapon switching or target changes

## Malfunction Model
We do not want a flat “base jam %” on a mint weapon: **tier bands** on integrity `C` and fouling `F` ensure that in the **none** band there is no jam from that channel. Above that, jam chance scales with **how deep** the weapon is into wear/fouling and with **tier severity**.

### Runtime condition (normalized)

- **`Wear01`** ∈ [0,1]: 0 = mint, 1 = worst wear. **Integrity** `C = round(100 * (1 - Wear01))`, 100 = mint.
- **`Fouling01`** ∈ [0,1]: 0 = clean, 1 = **100%** fouling. **`F = round(100 * Fouling01)`**.

After each **successful** shot (round consumed), condition updates:

- `ΔWear01 = Ammo.WearPerShot * Π(WearPerShotMultiplier on attachments) / BaseDurability`
- `ΔFouling01 = Ammo.FoulingPerShot * Π(FoulingPerShotMultiplier on attachments) / BaseFoulingBudget`

(`BaseDurability` / `BaseFoulingBudget` are at least 1 in code.)

### Optional definition thresholds (extra gate)

Jam **stress** for each channel uses Unity `InverseLerp` from the weapon’s start threshold to 1:

- Wear: `stressWear = InverseLerp(WearJamStartThreshold, 1, Wear01)` — if **0**, no wear-channel roll (unless tier is terminal).
- Fouling: `stressFoul = InverseLerp(FoulingJamStartThreshold, 1, Fouling01)` — same.

With thresholds **0** (recommended when relying purely on `C`/`F` bands), `stressWear ≈ Wear01` and `stressFoul ≈ Fouling01` for the linear middle range.

### Jam trigger: two channels, mutually exclusive per shot

**Wear** and **fouling** still use **separate** tier tables (bands below). **At most one** channel can produce a jam on a single trigger check.

**Implementation order** (`UnitWeaponMalfunctionController`):

1. Compute **wear-channel** probability `pWear`. If `Random.value < pWear` and wear tier allows a kind → jam **source = wear**, pick light/heavy from wear tier. **Stop.**
2. Else compute **fouling-channel** probability `pFoul`. If `Random.value < pFoul` and fouling tier allows → **source = fouling**. **Stop.**
3. Else no jam.

So fouling is **not** rolled if wear already “won” the metaphorical race; the two channels never fire together on one pull.

### Implemented jam probability (per channel)

Let `T` = **tier stress multiplier** (code constants): LightOnly **0.35**, LightOrHeavy **0.70**, HeavyOnly **1.0**; **None** / **Terminal** → this channel’s `p = 0` (terminal handled separately).

Let `R = Lerp(1.35, 0.25, Reliability)` with `Reliability` clamped to [0,1].

Let `J = clamp( JamRiskModifier_ammo * JamRiskModifier_mag * Π(JamRiskModifier_attachments), 0, 10 )` using the round **in chamber** for the attempt.

Then:

- **`pWear = clamp01( stressWear * WearJamInfluence * T * R * J )`**
- **`pFoul = clamp01( stressFoul * FoulingJamInfluence * T * R * J )**

(`T` is from the **wear** tier when computing `pWear`, and from the **fouling** tier when computing `pFoul`.)

**Light vs heavy** in the LightOrHeavy band: single inspector share `LightShareInMixedTier` on the malfunction controller (probability of light; else heavy).

### Design note vs old “per-tier P_jam table”

An earlier revision described a designer-authored table of `P_jam_on_trigger` per tier per channel. **Current code** replaces that with the **continuous** `stress * influence * T * R * J** formula above; tier bands still define **whether** the channel can jam at all and **`T`**. Designers tune `WearJamInfluence`, `FoulingJamInfluence`, `Reliability`, ammo/mag/attachment jam products, and durability budgets instead of pasting six independent tier probabilities on the controller.

### Operational malfunctions — single unified scenario (agreed gameplay)

One **shared** player-facing flow for **both** light and heavy. **Which** malfunction rolled (light vs heavy) still matters for **when** the fault actually clears and for **ammo/casing** rules in phase A.

#### Common rules

- While **any** malfunction is active, the weapon **cannot fire**: no hitscan, no recoil/spread bookkeeping as for a real shot until the fault is fully cleared.
- **No** extra “repeat light malfunction” RNG immediately after a fix (that idea is **cancelled**).
- **Bolt rack ladder** (same numbers everywhere it applies): up to **3** attempts per phase, per-attempt chance that the rack **would** clear a **light** stoppage on that try: **50% → 75% → 100%**.
- **Failed rack attempt:** no round consumed, no shell casing.
- **Successful rack that actually clears the malfunction:** consume the chamber round and spawn a shell casing (normal extraction). Applies whenever a phase ends in a **real** clear (see phases below).
- **Animator:** reuse **existing** parameters, states, and `AnimationEvent` names already wired for reload/bolt; no new parameter naming requirement in this doc.
- **Reliability:** use to soften malfunction **severity** or **trigger rate** (implementation discretion), without inventing a base jam chance from a mint weapon.

#### Phase A — magazine **seated** (always first)

- Run the rack ladder (50% / 75% / 100%) with the magazine still in the weapon.
- After each attempt, evaluate RNG:
  - If **light** malfunction **and** this attempt **succeeds** → malfunction **clears**, apply round + casing, **done** (no phase B).
  - If **heavy** malfunction → phase A **never** clears the fault, even if RNG “succeeds” on a try: **no** round consumed and **no** casing from phase A (the stoppage is not fixed until phase B completes). Continue through the three attempts, then go to phase B.

#### Phase B — **heavy only:** strip magazine → rack → insert magazine

- **Remove magazine:** magazine returns to inventory with **unchanged** round count; it is **not** marked broken.
- Run the **same** rack ladder (50% / 75% / 100%) with mag **out**. On an attempt that **succeeds**, apply round + casing and treat the chamber/stoppage as cleared for this sub-step; then **insert magazine** (may be the **same** magazine again — a “new” mag is **not** required).
- Completing insert after a successful clear in this phase ends the **heavy** malfunction. Chamber feeding after insert follows normal reload/chamber rules.

#### Malfunction **type** vs condition — **exact bands (integer %, debug-friendly)**

Use **integer** percentages in logs and tier lookup so breakpoints are unambiguous.

**Integrity `C`** (0–100): **100 = mint**, **0 = terminal / ruined**. Map from runtime degradation `Wear01` (0 = mint, 1 = worst) with:

`C = Mathf.Clamp(Mathf.RoundToInt(100f * (1f - Wear01)), 0, 100)` (or floor/ceil — pick one and keep consistent).

| Integer `C` | Jam tier (when **wear** channel actually rolls a jam) |
|-------------|--------------------------------------------------------|
| **80 ≤ C ≤ 100** | **None** — wear cannot cause a jam |
| **60 ≤ C ≤ 79** | **Light only** |
| **40 ≤ C ≤ 59** | **Light or heavy** (one type per incident; split from tuning table) |
| **1 ≤ C ≤ 39** | **Heavy only** |
| **C = 0** | **Terminal** — no rack/reload cure; broken / workshop |

**Fouling `F`** (0–100): **0 = clean**, **100 = worst** (your “0–20 clean” maps to **0–20** here).

| Integer `F` | Jam tier (when **fouling** channel actually rolls a jam) |
|-------------|-----------------------------------------------------------|
| **0 ≤ F ≤ 20** | **None** |
| **21 ≤ F ≤ 40** | **Light only** |
| **41 ≤ F ≤ 60** | **Light or heavy** |
| **61 ≤ F ≤ 99** | **Heavy only** |
| **F = 100** | **Terminal** |

Wear and fouling use **identical tier meanings** for **light / mixed / heavy / terminal**, but **separate** rolls and separate `stress` inputs. **Only one** channel can produce a jam on a given check.

#### Balancing (current)

Tune **`WearJamInfluence`**, **`FoulingJamInfluence`**, **`Reliability`**, per-shot **`J`** (ammo, magazine, attachments), **`BaseDurability` / `BaseFoulingBudget`**, and ammo **`WearPerShot` / `FoulingPerShot`**. Tier band table remains the **gate** for “can this channel jam at all?” and sets **`T`**. If a future CSV/SO per-tier `P_jam` is desired, it would replace the closed-form `pWear`/`pFoul` above — not implemented today.

#### AI / unit behavior

- Same sequence for units: phase A rack attempts first; if malfunction is heavy, continue with strip → phase B rack → insert (same mag allowed).

#### Implementation note

Exact marriage to `UnitWeaponReloadController` / fire gating is coding detail; this section locks **behavior and tier logic** only.

## Runtime State
Later runtime state should include at least:

- current magazine
- current ammo count
- current fire mode
- current aim progress
- current recoil penalty
- next allowed shot time
- current wear
- current fouling

## Roadmap

### Step 1
Create the data foundation only:

- weapon definition
- ammo definition
- magazine definition
- base enums and shared types
- attachment slot support in weapon data
- reliability, wear threshold, fouling threshold, and malfunction-related fields

No actual firing logic yet.

### Step 2
Add runtime weapon state to the unit.

### Step 3
Connect equipped weapon, magazine, and ammo.

### Step 4
Add command flow:

- start fire
- stop fire
- single shot attempt

### Step 5
Add aim progress and ergonomics behavior.

### Step 6
Add recoil accumulation and recovery.

### Step 7
Add effective range penalty behavior.

### Step 8
Add hit resolution logic.

### Step 9
Add ammo-driven damage and penetration.

### Step 10
Add reload and magazine handling.

### Step 11
Add AI shooting behavior.

### Step 12
Add effects, UI, debug tools, and balancing.

## Current First Implementation Goal
The next implementation target is Step 1 only:

- build the data model cleanly
- do not yet implement firing behavior
- keep the system extensible

