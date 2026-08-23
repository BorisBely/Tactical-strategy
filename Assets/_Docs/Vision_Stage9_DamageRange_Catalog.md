# Vision Stage 9 — каталог дальности урона

**Статус: CLOSED / VERIFIED** (2026-08-22 18:54:04). `Tools/Tests/Run Weapon Range Contract (Play)` → `Assets/_Docs/Logs/Tests/WeaponRangeContract_LAST.txt`. **PASS 53/0**. Проход **9B** записал только range-поля. Q / `ScopeVisionRange` / BaseDamage / recoil **не** ретюнились. Этап 8 остаётся **CLOSED / VERIFIED PASS 29/0** (повтор 18:47:18). Этап 10 кривых точности **CLOSED / VERIFIED PASS 11/0** (`Vision_Stage10_AccuracyAimCurves_Catalog.md`), **не** ретюнит E. Tactical AI **#7** не открывать.

Источник: `Tools/weapon_range_catalog.csv` + `Tools/ammo_range_catalog.csv` → `python Tools/bake_weapon_range.py`.  
Excel: лист **Дальность урона** (`python Tools/export_combat_balance_excel.py`).  
EditMode: `WeaponRangeContractTests`.  
Play: `Tools/Tests/Run Weapon Range Contract (Play)` → `Assets/_Docs/Logs/Tests/WeaponRangeContract_LAST.txt`.

`current_*` в CSV — снимок **до** bake (растянутая шкала). Live YAML должен совпадать с `proposed_*`.

## Формула (не менялась)

```text
E = min(WeaponRange × PhysicalModuleRangeProduct, AmmoRange)
d ≤ E        → multiplier 1
E < d < 2E   → linear
d ≥ 2E       → 0
```

Код: `WeaponDamageRangeMath` / `Tools/weapon_damage_range_model.py`. Неверный инвариант `ZeroDamageAt ≤ 300` не используется.

## Что записано

| Поле | Кто |
| --- | --- |
| `WeaponDefinition.m_EffectiveRangeMeters` | 26 боевых стволов |
| `AmmoDefinition.m_EffectiveRangeMeters` | 8 патронов |
| optic `m_EffectiveRangeModifier` | **1.0** на всех боевых прицелах |

Глушители **1.1** не трогались. `ScopeVisionRange` не трогался.

## Модели

| Категория | Оружие | Урон |
| --- | --- | --- |
| RegularHitscan | винтовки / LMG / CQB | линейный falloff, `0 < E ≤ 300` |
| ShotgunCurve | Benelli | pellet-кривая |
| HeavyHitscan | M2 Browning E=225 | линейный hitscan |
| ProjectileSupport | MK19 ceiling 300 | не live-falloff |

## Роли (утверждено)

| Класс | E | Край | × на краю |
| --- | ---: | ---: | ---: |
| Shotgun | 40 | 40 | 0.35 pellet |
| CQB | 100–115 | 150 | ≥ 0.50 |
| Assault | 140–160 | 200 | ≥ 0.57 |
| LMG | 150–160 | 200 | ≥ 0.67 |
| Marksman | 175–200 | 225–250 | ≥ 0.71 |
| Sniper | 225 | 300 | 0.67 |
| HMG (M2) | 225 | 300 | 0.67 |

Пример: M4 + 6× видит 250, полный урон до 140, на 200 ещё ×0.57.

## Legacy

- `bake_weapon_combat_balance.py` больше **не** пишет EffectiveRange.
- `stretch_distance_balance.py` отказан: exit с отсылкой на `bake_weapon_range.py`.
- Builder fallback-ы синхронизированы с каталогом (AK/M4/Standalone/SVD/ammo).

Этап 10 кривых: **CLOSED / VERIFIED PASS 11/0** — `Vision_Stage10_AccuracyAimCurves_Catalog.md`. Этап 11 дисциплины: **CLOSED / VERIFIED PASS 21/0** — `Vision_Stage11_FireDiscipline_Catalog.md`. Stage 12 projectile: **CLOSED / VERIFIED PASS 30/0** — `Vision_Stage12_ProjectileVision_Catalog.md`. E / `ScopeVisionRange` не ретюнить. Stage 13 (A6+A9) **CLOSED / VERIFIED PASS 35/0**. Stage 14 (A7) **CLOSED / VERIFIED PASS 31/0**.
