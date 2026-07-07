# Shooting FX Notes

Настройки FX стрельбы вынесены в data-driven профили. Юнит остаётся оркестратором (события, пулы), оружие — источником сокетов и данных.

## Текущая архитектура

- `WeaponVfxProfile` (ScriptableObject) — muzzle, shell, trail, impact для конкретного оружия.
- `WeaponDefinition.VfxProfile` — ссылка на профиль платформы.
- `EquippedWeapon` — сокеты `Barrel`, `ShellEject` (позиция и ориентация FX).
- На `PlayerUnit` остаются только runtime-компоненты и размеры пулов:
  - `UnitWeaponMuzzleVfx`
  - `UnitWeaponParticleShellEjection`
  - `UnitWeaponImpactVfx`
  - `UnitWeaponShellEjection` (физические гильзы, если в профиле `Physical`)

## AK-47

- Профиль: `Assets/GameData/Shooting/AK/WeaponVfxProfile_AK47.asset`
- Привязан к: `Assets/GameData/Shooting/AK/Weapon_AK47.asset`
- Режим гильз: `Particle` (физика отключена через профиль)
- Позиция и направление particle-гильз совпадают с физическим выбросом:
  `ShellEject.position` + `ShellEject.forward` (см. `WeaponVfxUtility.TryGetShellEjectionPose`).
- В профиле только визуальная подстройка mesh FX:
  - `Shell Prefab Ejection Axis` — локальная ось FX-префаба (`+X` для `FX_ShellEjection_Particle`)
  - `Shell Local Euler Offset` — финальный поворот mesh

## Что ещё сделать позже

- Профили для M4 и других оружий.
- Разные muzzle FX по типу насадки (компенсатор, пламегаситель и т.д.).
- Impact FX по типу поверхности (metal / wood / glass / flesh), не только бетон по слою `Target`.
- Бетон: в профиле массив `Concrete Impact Decal Prefabs` (AK — `01/02/03`), случайный выбор при попадании.
- Surface component или material mapping на colliders.
- URP-материалы для PolygonMilitary particle FX, если розовые.
- Общий pooling/service вместо отдельных пулов в каждом компоненте.
- Глобальный лимит и cleanup decals.

## Точки входа в коде

- `WeaponVfxProfile` — данные FX оружия.
- `WeaponVfxUtility` — suppressor, ориентация гильз, play particles.
- `UnitWeaponMuzzleVfx` — дульная вспышка.
- `UnitWeaponImpactVfx` — след и декаль.
- `UnitWeaponParticleShellEjection` — particle-гильзы.
- `UnitWeaponShellEjection` — физические гильзы.
- `UnitWeaponHitscanShooting.ShotTrace` — трасса для trail/impact.
