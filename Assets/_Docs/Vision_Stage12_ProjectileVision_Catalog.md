# Vision Stage 12 — Projectile Vision Contract

**Статус: CLOSED / VERIFIED** (2026-08-22 22:32:12). `Tools/Tests/Run Projectile Vision Contract (Play)` → `Assets/_Docs/Logs/Tests/ProjectileVisionContract_LAST.txt`. **PASS 30/0**.  
**Это A4+A5** карты `Пехота_дорожная_карта.md` одним законом. Не Attention (B). Пассажир/турель как VisionSource — Stage 13 **CLOSED PASS 35/0**. Не retain 18 м (A7). Не отдача (A10).  
**Не трогали:** Q, `VisionRange`, `ScopeVisionRange`, E, Range×, BaseDamage, recoil, Cone, Threat, Memory, Identity, AccuracyCurve, AimTime×, Fire Discipline, полы позы 0.35 / 0.68 / 1.0, RPG 115/130 м/с × 12 с, MK19 240 м/с × 25 с, каталожный E MK19 = 300.

Этапы 8–11 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Projectile Vision Contract (Play)` → `Assets/_Docs/Logs/Tests/ProjectileVisionContract_LAST.txt`.  
EditMode: `ProjectileVisionContractTests`.  
NavMesh warning harness — шум, не FAIL.

Следующее по зрению — **C1 Sound CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. A7 **CLOSED / VERIFIED PASS 31/0**. Stage 13 **CLOSED / VERIFIED PASS 35/0** (`Vision_Stage13_VehicleVision_Catalog.md`).

---

## Закон

```text
дальность жизни снаряда ≠ право назначить цель
Observed + inside ResolvedMaxRange → можно Fire
LastKnown / RecentlyLost / Lost → нельзя direct-fire
после законного Launch снаряд может лететь дальше VisionRange
```

```text
VISION → CONTACT → SELECT → G6 = Fire → GATE → Projectile Launch → physical flight
```

Второй VisionSystem нет. RPG не `WeaponDefinition`; envelope = уже существующий `UnitVision.ResolvedMaxRange`.

---

## Три дальности

1. **VisionRange** — `UnitVision.ResolvedMaxRange` (глаз 150; оптика только при активном scope).
2. **Fire/Guidance** — `distance(muzzle, Observed AimPoint) ≤ ResolvedMaxRange`.
3. **Physical** — RPG 12 с × 115/130; MK19 25 с × 240. **Не** `lifetime = VisionRange / speed`.

MK19 E=300 — targeting/замысел этапа 9, не live-falloff гранаты.

---

## Инвентаризация

| Контур | Stage 12 |
| --- | --- |
| RPG-7 / disposable | GATE permit; нет muzzle.forward без AimPoint |
| MK19 | тот же принцип; турель не целится в SelectedTarget без engageable |
| Ручные гранаты 5–35 м | **вне этапа** |
| CombatVehicleSystem shells | **вне этапа** |
| UBGL (слот без ассетов) | **вне этапа** |

Не добавляли: damage/falloff/splash/броню/взрыв, кривые RPG 0…500, оптические слоты на трубу.

---

## GATE

`UnitWeaponFireController.TryAuthorizeProjectileLaunch` — допуск без hitscan и без расхода патрона винтовки.

Deny: `NoAimPoint` | `NotG6Fire` | `OutsideVision` | `NoLOS`.

RPG: permit до анимации огня и ещё раз в spawn. MK19 spawn по-прежнему на `ShotFired` после hitscan-GATE; наведение только по engageable AimPoint.

Упреждение после permit: AimPoint + extra TOF сверх уже существующих 0.5 с hitscan. Лофт `RocketBallistics` не ретюнили.

---

## Лог

Тег `PROJECTILE` только на попытке запуска (успех или deny), не каждый кадр полёта.

```text
weapon=... tgt=... aim=... distance=... visionRange=... physicalRange=... result=Launch|fireDenied=...
```

---

## Acceptance

- RPG использует существующий VisionSystem.
- Нет запуска по цели за VisionRange.
- LastKnown не AimPoint.
- GATE — единая точка допуска.
- Lifetime не режется зрением.
- MK19: 300 м — envelope замысла, не физический потолок гранаты.
- Нет второго scan / постоянного raycast ради projectile vision.

Play stamp: `ProjectileVisionContract_LAST.txt` **RESULT=PASS PASS=30 FAIL=0** (2026-08-22 22:32:12). NavMesh warning при спавне harness — тот же шум, что у этапов 8–11, не FAIL. Stage 13 **CLOSED / VERIFIED PASS 35/0**. A7 **CLOSED / VERIFIED PASS 31/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. Stage 16 Sound **CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**.

---

## Файлы

- `ProjectileLaunchPermit.cs`
- `UnitWeaponFireController.TryAuthorizeProjectileLaunch`
- `UnitRocketLauncherOrderController`
- `VehicleTurretGunnerBridge`
- `ProjectileVisionContractTests`
- `ProjectileVisionContractRuntimeSmoke`
- `ProjectileVisionContractTestRunner`
