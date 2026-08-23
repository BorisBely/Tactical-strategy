# Vision Stage 13 — Vehicle Passenger + Turret Vision

**Статус: CLOSED / VERIFIED** (2026-08-22 22:59:18). `Tools/Tests/Run Vehicle Vision Contract (Play)` → `Assets/_Docs/Logs/Tests/VehicleVisionContract_LAST.txt`. **PASS 35/0**.  
**Это A6+A9** карты `Пехота_дорожная_карта.md`. Не Attention (B). Retain — **A7 CLOSED PASS 31/0**. Не отдача (A10).  
**Не трогали:** Q, пехотные `VisionRange` / `ScopeVisionRange` ассетов, E, Range×, AccuracyCurve, AimTime×, Fire Discipline, полы позы 0.35 / 0.68 / 1.0, RPG 115/130×12, MK19 240×25 / E=300, Stage 12 permit.

Этапы 8–12 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Vehicle Vision Contract (Play)` → `Assets/_Docs/Logs/Tests/VehicleVisionContract_LAST.txt`.  
EditMode: `VehicleVisionContractTests`.  
NavMesh warning harness — шум, не FAIL.

Следующее по зрению — **C1 Sound CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. A7 Retain **CLOSED / VERIFIED PASS 31/0** (`Vision_Stage14_CombatRetain_Catalog.md`).

---

## Закон

```text
один VisionSystem, разные источники параметров
пассажир из окна = пехота на земле (глаз 150, оптика до 300 при ready)
окно режет физический LOS корпус/стекло, не метры обзора
турель: OpticVisionRange если задан, иначе 150; не Pose == Aiming
AimPoint один: Observation → Contact → Combat
```

```text
VisionSource → ResolvedMaxRange → Vision / Contact / AimPoint → GATE / PROJECTILE
```

**Не 100 м.** Потолок окна снят. 101 м для пассажира — тот же Observation, что у пехоты на земле.

---

## VisionSource

```text
InfantryEye | Passenger | Turret
```

Оптика — данные источника, не четвёртый VisionSystem. Нет второго scanner / scheduler / VisibilityChecker.

| Source | Envelope | Scope |
| --- | --- | --- |
| InfantryEye | 150 / optic в Aiming | как этапы 8–11 |
| Passenger | те же 150 / optic | fire-capable ready = scope-eligible (в сиденье нет Aiming) |
| Turret | optic если `OpticVisionRange` > 150, иначе 150 | не зависит от Aiming |

Live M2 / MK19: `OpticVisionRange = 0` → **150**. Тест «250» — injected, не bake ствола.

FOV пассажира: **120°**. Ось — уже существующий look в окно, не новый конус.

---

## Инвентаризация

| Контур | Stage 13 |
| --- | --- |
| `VehiclePassengerFireValidator` 100 м | снят; сектор / стекло / кузов остаются |
| Origin | голова пассажира, если есть; иначе 1.6. Турель — pitch/оружие, если bound |
| MK19 AimPoint | Stage 12, не меняли |
| Hitscan / permit | кормятся `ResolvedMaxRange` |

Вне этапа: CVS shells, UBGL, Attention. Retain — **A7 CLOSED PASS 31/0**.

---

## Лог

`VISION` / `SPAWN`: `source=` и `resolvedRange=` при смене источника/профиля/контакта, не каждый кадр.

---

## Acceptance

- Пассажир 101 м видит; 151 без оптики — нет Observation.
- Пехота рядом на 101 тоже видит (100 не глобальный кап).
- Турель без оптики: 149 да / 151 нет; без Aiming.
- M2 / MK19: один AimPoint; Stage 12 regression.
- Нет второго VisionSystem.

Play stamp: `VehicleVisionContract_LAST.txt` **RESULT=PASS PASS=35 FAIL=0** (2026-08-22 22:59:18). NavMesh warning при спавне harness — тот же шум, что у этапов 8–12, не FAIL. A7 Retain **CLOSED / VERIFIED PASS 31/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. Stage 16 Sound **CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**.

---

## Файлы

- `VisionSourceKind` / `UnitVisionProfile`
- `UnitVision`
- `VehiclePassengerFireValidator`
- `WeaponDefinition.OpticVisionRangeMeters`
- `VehicleVisionContractTests`
- `VehicleVisionContractRuntimeSmoke`
- `VehicleVisionContractTestRunner`
