# Target + Fire Calibration

**Слой:** тактический **#12**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.5.  
**Статус:** **CLOSED / FROZEN 26.08.2026.** EditMode **18/0**. Play `TargetCalibration_LAST.txt` **PASS 26/0**. Регрессия: EditMode `[FrozenLayers] finished` **62/0** (Search 2.0 + Command Priority); Play `FrozenLayersPlay_LAST.txt` **114/0** (#7 18/0, #8 36/0, #9 20/0, #10 22/0, #11 18/0). #13 CLOSED / FROZEN: `Dynamic_Cover.md`.

Калибруется **выбор и переключение цели**, не стрельба вообще.

Не менять замороженное:

```text
Vision
Detection
Identity
Memory
RoE
CombatIntent
A10 recoil
Weapon ballistic envelope
θ / Recoil / AimTime / WorkingRange / VisionRange
```

G5 `TargetSelectionMath.Score` **не переписывается**. Weapon / mission — маленькие добавки после Score. Hysteresis — отдельный switch gate.

```text
Perception / Knowledge
        ↓
кандидаты
        ↓
Target Selection
        ↓
Engageable?
        ↓
CombatIntent / RoE
        ↓
Track / Aim / Fire
        ↓
Fire Discipline
```

## Selection ≠ Fire

```text
Target Selection = кого держать как боевую цель
Fire Decision    = можно ли сейчас по нему стрелять
```

`Selected + Engageable=false` — норма.  
`Selected + Engageable=true + G6=Aim` — норма.  
High Threat повышает selection score и **не** жмёт Fire.

## Score (G5, без изменений)

Observed, confidence, Threat, Hostile, inverse distance, stale penalty.  
LastKnown — только подсказка дистанции, **никогда AimPoint**.

## Hysteresis

```text
NewScore > CurrentScore + SwitchThreshold
```

`SwitchThreshold = 0.45` (зафиксировано Play Arena).  
Чуть лучше → remain. Значительно лучше → switch. Текущая потеряна / ineligible → switch без hysteresis.

## Weapon suitability

Маленькая поправка, не роли и не cover.

| Класс | Нюдж |
|-------|------|
| Shotgun / Pistol / SMG | ближняя ↑ |
| SniperRifle | далёкая ↑ |
| Rifle / LMG / HMG | мягкий пик около 0.45×EffectiveRange |

`WeaponSuitabilityWeight = 0.35`. Не ретюнит EffectiveRange / WorkingRange.

## Mission

`Attack`/`Defense` `TargetEntity` даёт `MissionBonus = 0.6`, не ForcedPriority и не `AI.EngageTarget`.

Случайный контакт B перебивает mission A только если:

```text
Score(B) > Score(A) + MissionBonus + SwitchThreshold
```

Пример: High+Hostile incidental бьёт mission Unknown на той же дистанции. Одно High без Hostile — нет.

## AI.EngageTarget vs Combat.SelectedTarget

**Не сливать.**

```text
AI.EngageTarget     = Hostile + VisibleNow, max Threat  («хочу вести бой»)
Combat.SelectedTarget = G5 knowledge + hysteresis     («этот контакт сейчас удовлетворяет боевым условиям»)
```

Расхождение — диагностический факт: `EngageTargetMismatch` + `TargetCombatMismatch.Explanation`. G6 не перезаписывает SelectedTarget.

## AimPoint

```text
Selected ≠ AimPoint автоматически
Selected + Observed + LOS AimPoint → Engageable
Selected + no AimPoint → Track
LastKnown → NEVER AimPoint
```

## Лог SELECT

Тот же канал, без нового:

```text
SELECT selected=E02 score=12.4 runnerUp=E01:11.0 switch=1 switchReason=HigherScore
       currentScore=10.1 candidateScore=12.4 switchThreshold=0.45 engageable=1 aim=1

SELECT selected=E02 switch=0 switchReason=Hysteresis
       currentScore=12.4 candidateScore=12.6 switchThreshold=0.45
```

## Тесты

```text
── Current ──
Tools/Tests/Run Dynamic Cover (EditMode)

── Regression ──
Tools/Tests/Run Regression (Play)
Tools/Tests/Run Regression (EditMode)
Tools/Tests/Archive/Regression/Run Target Calibration (Play)
Tools/Tests/Archive/Regression/Run Target Calibration (EditMode)
```

Play regression в одной сессии гоняет #7 ImmediateThreat, #8 Combat Events, #9 Sound In AI, #10 Search 2.0, #11 Command Priority. Каждый слой по-прежнему пишет свой `*_LAST.txt`; сводка — `FrozenLayersPlay_LAST.txt`.

EditMode regression — Search 2.0 + Command Priority одним NUnit-прогоном.

Одиночный слой: `Tools/Tests/Archive/Regression/`.

| Набор | Меню | Отчёт |
|-------|------|-------|
| #12 A–J | `Archive/Regression/Run Target Calibration (EditMode)` | Console `[TargetCalibration] finished` |
| #12 Play Arena | `Archive/Regression/Run Target Calibration (Play)` | `TargetCalibration_LAST.txt` |
| #7–#11 Play | `Tools/Tests/Run Regression (Play)` | `FrozenLayersPlay_LAST.txt` + слоевые LAST |
| #10+#11 EditMode | `Tools/Tests/Run Regression (EditMode)` | Console `[FrozenLayers] finished` |

EditMode: A deterministic, B hysteresis hold, C meaningful switch, D lost current, E no LOS → Track, F memory never Fire, G Unknown selectable, H Friendly never, I mission, J AI/Combat mismatch not merged. Плюс weapon nudge и High Threat ≠ Fire.

Play строит `TargetCalibrationArena` в рантайме (E1 / E2 / E3 вокруг AI), не baked `.unity`.

## Не в #12

Cover, Position, Movement, Weapon Role, Rank, CQB.  
Ретюн θ / Recoil / AimTime / WorkingRange / VisionRange.  
Автослияние AI и Combat целей.  
Перепись G5 Score.

## CLOSED / FROZEN 26.08.2026

```text
selection deterministic
target hysteresis
meaningful switching
потеря LOS не ломает selection
LastKnown не AimPoint
Unknown contract
Friendly rejection
AI/Combat mismatch объясним
Mission relevance определена
weapon suitability определена
Fire decision не смешан с selection
#7–#11 PASS
```

Слой **FROZEN**. Не ретюнить G5 Score, G6, RoE, A10, perception ради «лучшего выбора». #13 CLOSED / FROZEN: `Dynamic_Cover.md`.
