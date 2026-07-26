# Combat Vehicle System

Self-contained vehicle combat package for drop-in use in another Unity project (URP).

## What this package is

- Wheeled and tracked drive
- Turret aim + weapon fire
- Optional track-break / mine damage
- ScriptableObject tunings per vehicle
- Prefabs, FX, audio, models

## What it is not

- No keyboard / mouse Input System polling
- No demo camera or vehicle switcher
- No sample terrain / props scene

## External control (TPS)

Drive everything through `VehicleBrain`:

```csharp
using CombatVehicleSystem;

VehicleBrain brain = vehicle.GetComponent<VehicleBrain>();
brain.SetControlActive(true);
brain.SetCommand(new VehicleCommand
{
    Steer = move.x,          // -1..1
    Throttle = move.y,       // -1..1
    Brake = handbrake,
    FireHeld = fire,
    AimWorldPoint = aimHitPoint,
    HasAimPoint = true
});
```

When the player exits the vehicle, call `SetControlActive(false)` (resets command and parks turret/weapons).

## Folder map

| Path | Contents |
|------|----------|
| `Scripts/` | Runtime + Editor builder (`Tools → Combat Vehicle System`) |
| `Prefabs/Vehicles/` | Desert / Forest vehicle prefabs (run Build menu once) |
| `Prefabs/Combat/` | Shells (`ShellProjectile`), muzzle, impacts, mine, destroy-track |
| `Content/` | Models, vehicle materials/textures (transfer catalog) |
| `Effects/` | FX materials/textures + shell catalog copies |
| `Audio/` | Engine + shot clips |
| `Data/Tunings/` | `VehicleTuning` assets (8 vehicles) |
| `Documentation/` | Tuning tables, FX/audio inventory |

## Requirements

- Unity 6 / URP
- Physics (WheelCollider)

## First-time setup in this project

1. Wait until Unity finishes importing `CombatVehicleSystem` (no spinning progress).
2. Menu: **Tools → Combat Vehicle System → Build Full Package Prefabs**.
   - Converts Desert/Forest vehicle prefabs onto the new components (`VehicleBrain`, motors, `TurretAim`, `WeaponMount`, …).
   - Rewires shells to `ShellProjectile`, mines to `ExplosiveMine`.
   - Refreshes tuning assets under `Data/Tunings/`.
3. Open a vehicle prefab, add `VehicleCommandInspectorDriver`, tick **Drive From Inspector**, set Steer/Throttle in the Inspector to verify (no keyboard).

If the menu is missing, scripts did not compile — fix console errors, then re-open the project.

## Debug

Add `VehicleCommandInspectorDriver` on a vehicle to drive axes from the Inspector (no keyboard). Useful for verifying the pack before TPS wiring.
