# Effects and Audio Inventory

Catalog of combat FX and audio used by Combat Vehicle System.

## Audio (`Audio/`)

| File | Role | Used by |
|------|------|---------|
| `Engines/S_Engine_Mono_01.wav` | Looping engine | `VehicleBrain` → engine `AudioSource` |
| `Shots/S_Shot_Stereo_01.wav` | Fire one-shot | `WeaponMount` shot clip |

Original AMX had a second engine clip with a missing GUID — not included.

## Muzzle flashes (`Prefabs/Combat/Muzzle/`)

| Prefab | Caliber |
|--------|---------|
| `Muzzle_12_7` | 12.7mm MG |
| `Muzzle_14_5` | 14.5mm HMG |
| `Muzzle_30` | 30mm autocannon |
| `Muzzle_Tank` | Tank / large cannon |

## Impacts (`Prefabs/Combat/Impacts/`)

| Prefab | Notes |
|--------|-------|
| `Impact_12_7` | Light hit + smoke |
| `Impact_14_5` | Light hit + smoke |
| `Impact_30` | Sparks, debris, larger smoke |
| `Impact_Tank` | Heavy HE-style burst |

## Shells (`Prefabs/Combat/Shells/` + `Effects/Prefabs/`)

| Prefab | Component | Default impact |
|--------|-----------|----------------|
| `Shell_12_7` | `ShellProjectile` | Impact_12_7 |
| `Shell_14_5` | `ShellProjectile` | Impact_14_5 |
| `Shell_30` | `ShellProjectile` | Impact_30 |
| `Shell_Tank` | `ShellProjectile` | Impact_Tank |

## Supporting FX materials / textures

Copied into:

- `Effects/Materials/` — smoke, trail, debris, sparkles
- `Effects/Textures/` — particle and trail textures

## Other combat prefabs

| Prefab | Role |
|--------|------|
| `Prefabs/Combat/Destroy_Track` | Debris spawned on track break |
| `Prefabs/Combat/TM62_Mine` | `ExplosiveMine` (tag `Mine`) |

## Typical vehicle → FX mapping

| Vehicle | Shell | Muzzle | Impact |
|---------|-------|--------|--------|
| Stryker / MRAP | 12.7 | 12.7 | 12.7 |
| BRDM2 | 14.5 | 14.5 | 14.5 |
| BTR80A / Bradley | 30 | 30 | 30 |
| AMX_10 / T72 / M1A2 | Tank | Tank | Tank |

Exact wiring is on each vehicle prefab `WeaponMount`.
