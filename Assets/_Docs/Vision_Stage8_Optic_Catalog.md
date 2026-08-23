# Vision Stage 8 — каталог боевых прицелов

**Статус: CLOSED / VERIFIED** (2026-08-22 16:42:52). `Tools/Tests/Run Vision Optic Range Contract (Play)` → `Assets/_Docs/Logs/Tests/OpticRangeContract_LAST.txt`. **PASS 29/0**. Этап 7 lifecycle остаётся **CLOSED / VERIFIED PASS 37/0**.  
**Не трогаем:** Q, DistanceCurve, AcquireThreshold / AcquireTime, Exposure, Movement, sweep, LOS6, TargetSelector, урон, отдачу, Fire Discipline, `EffectiveRange` оружия и патронов.  
**Источник метров:** `Tools/optic_vision_catalog.csv` → `python Tools/bake_optic_vision_range.py` → поля `WeaponAttachmentDefinition`. Книга: лист **Прицелы** в `Tools/CombatBalanceParameters.xlsx` (`python Tools/export_combat_balance_excel.py`).  
**Play:** тот же меню-пункт. EditMode: `OpticRangeContractTests`. Этап 9 (дальность урона): **CLOSED / VERIFIED** 18:54:04, `WeaponRangeContract_LAST.txt` **PASS 53/0** (`Vision_Stage9_DamageRange_Catalog.md`). Q / `ScopeVisionRange` не ретюнить.

Кратность — **класс**, не `150 × zoom`. Конверт **150…300 м**. `1× → 150` записан в данных, в том числе у переменной оптики. `ScopeVisionRange` и `EffectiveRangeModifier` живут рядом и **не связаны**.

Переменный прицел — один `WeaponAttachmentDefinition`, активный режим:

```text
Optic → ActiveMagnificationMode → ResolvedScopeVisionRangeMeters
```

Низкий режим: `m_LowMagnificationScopeVisionRangeMeters` (150). Высокий: `m_ScopeVisionRangeMeters`. Второго VisionSystem нет. Пока нет игрового/AI переключателя кратности, runtime по умолчанию держит **высокий** режим (`m_HighMagnificationActive = 1`). Тесты переключают клон ассета, диск не пачкают.

Одинаковый оптический тип + только модель/крепление/косметика → одинаковый `ScopeVisionRange`.

Тестовый `Attachment_TestScope_300` в каталог не входит.

Игровые имена и описания — локализация (`russian.json` / `english.json`). Ключи `item.attachment.*` не менять. Тексты абстрактные, без заводских названий.

## Поведение (не по имени файла)

| Класс | ScopeVisionRange | Ассеты |
| --- | ---: | --- |
| 1× коллиматор / голограф | 150 | Reddot1, Reddot3, RDC, Reddot2, AK_Reddot4_Rail |
| 2× компактная труба | 175 | Aimpoint |
| Hybrid / dual / LPVO 1× | 150 | EOTech_G33, ACOG_RMR, ELCAN, Vortex — низкий режим |
| 3× | 200 | Scope1_3x; G33 высокий |
| 3.5× | 210 | Mosin_Scope8; ACOG_RMR высокий |
| 4× | 220 | ACOG, SUSAT, AK_Scope11; ELCAN высокий |
| штурмовой 6× (LPVO) | 250 | Vortex высокий |
| снайперская 6× | 260 | Scope4 |
| снайперская 8× | 280 | Scope5 |
| снайперская 10× | 300 | Scope9 |

Шкала: 150 → 175 → 200 → 210 → 220 → 250 → 260 → 280 → 300.

Снайперский класс **начинается с 6×**. Штурмовой 1–6× на высоком режиме остаётся 250: это не длинная труба.

## Режимы

| Optic | Mode | Mag | ScopeVisionRange | EffectiveRangeModifier | AimTimeModifier |
| --- | --- | ---: | ---: | ---: | ---: |
| Attachment_M4_Reddot1 | 1x | 1 | 150 | 1.00 | 0.98 |
| Attachment_M4_Reddot3 | 1x | 1 | 150 | 1.00 | 0.98 |
| Attachment_M4_RDC | 1x | 1 | 150 | 1.00 | 0.98 |
| Attachment_M4_Reddot2 | 1x | 1 | 150 | 1.00 | 0.98 |
| Attachment_AK_Reddot4_Rail | 1x | 1 | 150 | 1.00 | 0.95 |
| Attachment_M4_Aimpoint | 2x | 2 | 175 | 1.10 | 1.00 |
| Attachment_M4_EOTech_G33 | 1x | 1 | 150 | 1.15 | 1.14 |
| Attachment_M4_EOTech_G33 | 3x | 3 | 200 | 1.15 | 1.14 |
| Attachment_M4_Scope1_3x | 3x | 3 | 200 | 1.15 | 1.14 |
| Attachment_Mosin_Scope8 | 3.5x | 3.5 | 210 | 1.20 | 1.22 |
| Attachment_M4_ACOG_RMR | 1x | 1 | 150 | 1.20 | 1.22 |
| Attachment_M4_ACOG_RMR | 3.5x | 3.5 | 210 | 1.20 | 1.22 |
| Attachment_M4_ACOG | 4x | 4 | 220 | 1.20 | 1.20 |
| Attachment_M4_SUSAT | 4x | 4 | 220 | 1.20 | 1.24 |
| Attachment_AK_Scope11 | 4x | 4 | 220 | 1.18 | 1.24 |
| Attachment_M4_ELCAN_SpecterDR | 1x | 1 | 150 | 1.25 | 1.24 |
| Attachment_M4_ELCAN_SpecterDR | 4x | 4 | 220 | 1.25 | 1.24 |
| Attachment_M4_Vortex_Razor | 1x | 1 | 150 | 1.35 | 1.34 |
| Attachment_M4_Vortex_Razor | 6x | 6 | 250 | 1.35 | 1.34 |
| Attachment_M4_Scope4 | 6x | 6 | 260 | 1.35 | 1.46 |
| Attachment_M4_Scope5 | 8x | 8 | 280 | 1.45 | 1.56 |
| Attachment_M4_Scope9 | 10x | 10 | 300 | 1.60 | 1.55 |

`AimTimeModifier` — боевые значения на момент Stage 8, **не** пересчитаны из VisionRange. `EffectiveRangeModifier` в таблице выше — снимок **до** Stage 9. После 9B все боевые прицелы имеют Range× **1.0**; `ScopeVisionRange` не менялся. Distance curves перенесены на этапе 10 (`Vision_Stage10_AccuracyAimCurves_Catalog.md`); `ScopeVisionRange` / AimTime× при этом не ретюнились.

## Решения

- **Aimpoint.** Компактная 2× труба, не коллиматор. **175** — между глазом 150 и 3× 200. Mag>1 не может остаться 150.
- **Mosin_Scope8.** Индекс ассета, не 8×. Компактный **3.5× / 210**, не снайперская труба.
- **ACOG_RMR.** Два канала: верхний 1× / 150 и основной 3.5× / 210. Не зум.
- **SUSAT.** 4× / 220. Сверху открытый запасной прицел, не второй канал обзора.
- **ELCAN.** Два режима **1× / 4×**, высокий = 220. **Vortex** 1–6× высокий = 250 (штурмовой 6×).
- **G33.** Гибрид **1× / 3×**, высокий = 200 как фиксированный 3×.
- **Scope4 / 5 / 9** — снайперская лестница **6× / 8× / 10×** → **260 / 280 / 300**. Только Scope9 = 300. Scope4 чуть выше штурмового 6×, потому что это длинная труба.

## Контракт тестов

1. Все боевые оптики: `150 ≤ ScopeVisionRange ≤ 300`.
2. 1× (включая низкий переменный) = 150.
3. Mag > 1 → `> 150`.
4. Хотя бы один = 300, никто > 300.
5. Vortex 1× @ 175 не виден; 6× @ 175 виден; 6× @ 251 не виден.
6. Vision 250 + modifier 1.0 vs 1.5 → одинаковый VisionRange и hitscan cap.
7. Hitscan и выстрел режутся `ResolvedVisionRange`, не modifier.

Этап 9: **CLOSED / VERIFIED** (2026-08-22 18:54:04, `WeaponRangeContract_LAST.txt` **PASS 53/0**). Каталог: `Vision_Stage9_DamageRange_Catalog.md`. `ScopeVisionRange` не ретюнить.  
Этап 10: **CLOSED / VERIFIED** (2026-08-22 21:35:32, `AccuracyAimCurveContract_LAST.txt` **PASS 11/0**). Каталог: `Vision_Stage10_AccuracyAimCurves_Catalog.md`. Distance curves внутри 150/300; `ScopeVisionRange` / AimTime× / Range× не ретюнились.
