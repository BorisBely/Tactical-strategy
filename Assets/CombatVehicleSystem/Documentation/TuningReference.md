# Vehicle Tuning Reference

Values extracted from original Low_Poly_Vehicles_Controller Desert prefabs and stored in `Data/Tunings/*.asset`.

## Drive

| Vehicle | Type | MotorForce | TopSpeed (km/h) | MaxBrakeTorque | TrackScrollScale | CenterOfMass |
|---------|------|------------|-----------------|----------------|------------------|--------------|
| Stryker | Wheeled | 2500 | 90 | 5000 | 1 | (0, -1, 0) |
| BTR80A | Wheeled | 2500 | 90 | 2500 | 1 | (0, -1, 0) |
| BRDM2 | Wheeled | 1500 | 90 | 5000 | 1 | (0, -1, 0) |
| MRAP | Wheeled | 1500 | 90 | 5000 | 1 | (0, -1, 0) |
| AMX_10 | Wheeled | 1500 | 70 | 5000 | 1 | (0, -1, 0) |
| Bradley M2 | Tracked | 1500 | 75 | 1000 | 50 | (0, -1, 0) |
| T72 | Tracked | 1500 | 55 | 2000 | 2 | (0, -1, 0) |
| M1A2 Abrams | Tracked | 1500 | 65 | 2000 | 2 | (0, -1, 0) |

## Turret

| Vehicle | TurnRate | DownPitchLimit | UpPitchLimit | DefaultAimDistance | LimitYaw |
|---------|----------|----------------|--------------|--------------------|----------|
| Stryker | 120 | 20 | 60 | 200 | false |
| BTR80A | 120 | 12 | 60 | 200 | false |
| BRDM2 | 100 | 4 | 60 | 200 | false |
| MRAP | 60 | 20 | 60 | 200 | false |
| AMX_10 | 50 | 4 | 60 | 200 | false |
| Bradley M2 | 120 | 12 | 60 | 200 | false |
| T72 | 120 | 12 | 60 | 200 | false |
| M1A2 Abrams | 120 | 12 | 60 | 200 | false |

## Weapon

| Vehicle | FireInterval | ShellSpeed | HullRecoil | Mag | Spread | BarrelKick | KickSpeed | ReturnSpeed | Typical shell |
|---------|--------------|------------|------------|-----|--------|------------|-----------|-------------|---------------|
| BRDM2 | 0.12 | 250 | 10 | 300 | 0.1 | z 0.08 | 40 | 10 | 12.7 / 14.5 |
| Stryker | 0.17 | 150 | 200 | 300 | 0.1 | none | 8 | 18 | 12.7 |
| MRAP | 0.17 | 200 | 100 | 100 | 0.1 | none | 8 | 18 | 12.7 |
| BTR80A | 0.2 | 500 | 1200 | 300 | 0.1 | z 0.15 | 40 | 10 | 30mm |
| Bradley M2 | 0.3 | 500 | 1200 | 300 | 0.1 | z 0.15 | 40 | 10 | 30mm |
| AMX_10 | 1.0 | 3500 | 5000 | 100 | 0 | z 1.5 | 40 | 3 | Tank |
| T72 | 2.0 | 3500 | 6000 | 300 | 0 | z 1.0 | 40 | 10 | Tank |
| M1A2 Abrams | 2.0 | 3500 | 6000 | 300 | 0 | z 1.0 | 40 | 10 | Tank |

Shared weapon defaults: `HitFxLifetime=10`, `ShellLifetime=25`, shot pitch `0.9..1.1`.

## Mine

| Asset | ExplosionForce |
|-------|----------------|
| TM62_Mine | 12000 |

## Notes

- Wheel steer angles stay on prefab `WheelAxle` (not in SO): typically ±10..20 for wheeled, AMX ±12.
- Track break module is wired only on T72 and M1A2 (matches original).
- Forest prefabs use the same tunings; only materials/skins differ.
