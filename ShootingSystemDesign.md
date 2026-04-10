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
- `Reliability`: general resistance to wear, fouling, and malfunctions.
- `WearJamStartThreshold`: wear threshold after which jams become possible.
- `FoulingJamStartThreshold`: fouling threshold after which jams become possible.
- `WearJamInfluence`: how strongly wear affects jam risk after the threshold.
- `FoulingJamInfluence`: how strongly fouling affects jam risk after the threshold.
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
- `WearModifier`: modifies weapon wear growth.
- `FoulingModifier`: modifies weapon fouling growth.
- `JamRiskModifier`: modifies malfunction risk once the weapon is already in the danger zone.

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

### Attachments
Attachments are separate modifiers applied to weapon behavior.

Expected attachment categories:

- optic
- muzzle device
- laser
- foregrip
- stock

Attachment data should eventually contain:

- `AimTimeModifier`: modifies aiming speed.
- `EffectiveRangeModifier`: modifies effective range.
- `RecoilModifier`: modifies recoil accumulation.
- `ReloadTimeModifier`: modifies reload speed.

We do not need full attachment logic immediately, but weapon attachment slots should exist in the architecture early.

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
We explicitly do not use a base jam chance in perfect condition.

Agreed rule:

- a weapon in good condition should not jam
- jam risk appears only after the relevant threshold is crossed

### Wear

- `Wear`: current runtime wear value
- if `Wear <= WearJamStartThreshold`, wear adds no jam risk
- after threshold, wear adds jam risk progressively

### Fouling

- `Fouling`: current runtime fouling value
- if `Fouling <= FoulingJamStartThreshold`, fouling adds no jam risk
- after threshold, fouling adds jam risk progressively

### Suggested Risk Logic

`WearJamFactor = (Wear - WearJamStartThreshold) / (1 - WearJamStartThreshold)` when over threshold

`FoulingJamFactor = (Fouling - FoulingJamStartThreshold) / (1 - FoulingJamStartThreshold)` when over threshold

Then:

`JamRisk = (WearJamFactor * WearJamInfluence) + (FoulingJamFactor * FoulingJamInfluence)`

And final risk:

`FinalJamRisk = JamRisk * Magazine.JamRiskModifier * Ammo.JamRiskModifier`

`Reliability` should reduce growth or severity of these effects, not create a base jam chance from zero.

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

