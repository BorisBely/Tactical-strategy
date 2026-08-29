# Threat Direction Knowledge

**Слой:** тактический **#14C**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.7 (revision **1.3**).  
**Статус:** **OPEN** — 14C.0–14C.6 ✅ EditMode **49/0**. Play **20/0** (11:56:21). **14C.1 ✅** EditMode **40/0**. Play **22/0** (12:58:02). **14C.2 ✅** EditMode **38/0**. Play **19/0** (13:15:02). **14C.3 ✅** EditMode **43/0**. Play **18/0** (13:41:42). **14C.4 ✅** EditMode **36/0**. Play **18/0** (14:47:14). **14C.5 ✅** EditMode **39/0**. Play **18/0** (15:51:13). #13 / #14 / #14B **не reopen**. #15 **не открывать**. #14B **не FROZEN**. #14C **не FROZEN**.

`ThreatDirectionKnowledge` отвечает не на «где точно противник», а на:

```text
В каком направлении относительно меня, скорее всего, находится угроза?
```

Не заменяет:

```text
Vision / LastKnown
SoundPosition
AllyReport / SharedPosition
TargetSelector
CombatIntent
Search
Readiness #14B
Dynamic Cover #13
Tactical Movement #14
```

```text
ThreatDirectionKnowledge
        ↓
   ┌────┴────┐
   ↓         ↓
Cover      Facing     ← 14C.1 overlay, не CoverScore
orientation
        ↓
TacticalPositionPreference  ← 14C.3 overlay, не Move
        ↓
Reorientation / ThreatFit   ← 14C.4, не Reposition
        ↓
Stay / FaceOnly / RepositionAllowed  ← 14C.5 permission, не Move
```

#14C — **knowledge layer**, не tactical action. 14C.1 — ориентация/facing. 14C.2 — качество. 14C.3 — `TacticalPositionPreference` (decision support). 14C.4 — SignificantChange → facing + ThreatFit. 14C.5 — когда менять позицию.

---

## Состояния

```text
None
Expected
Known
Stale
```

| Состояние | Смысл |
|-----------|--------|
| `None` | Нет ни spawn-оценки, ни фактического знания. |
| `Expected` | До контакта / после expiry фактического знания. Направление из центров spawn-групп. |
| `Known` | Фактический канал: Visual LastKnown, Sound или AllyReport. |
| `Stale` | Контакт потерян или канал устарел. Направление ещё держится. |

## Источники (приоритет)

```text
Visual LastKnown
    >
Sound
    >
AllyReport
    >
InitialEstimate
```

Совпадает с Search: `Visual LastKnown > SoundPosition > ReportPosition`.

`Stale` visual всё ещё Visual и **не** уступает Sound / Report, пока не истечёт в Expected / None.

## Поля снимка

```text
Direction     нормаль XZ от юнита (или от оси spawn-групп для Expected)
Compass       N / NE / E / SE / S / SW / W / NW   (+Z = North, +X = East)
Confidence
Uncertainty   полуугол сектора, градусы
Age           секунды с фиксации текущего направления
Source
State
```

Прототип (не freeze): Expected confidence **0.5**, uncertainty **45°**. Visual **0.9 / 15°**. Sound **0.7 / 30°**. Report **0.6 / 35°**. Visual stale → Expected **8 с**. Sound Known→Stale **4 с**, Stale→Expected **4 с**.

---

## 14C.0 Contract  ✅

Типы: `ThreatDirectionKnowledge`, `ThreatDirectionState`, `ThreatDirectionSource`, `ThreatDirectionEstimator`, `ThreatDirectionController`.

API потребителя:

```text
TryGetThreatDirection(out ThreatDirectionKnowledge)
HasThreatDirection
GetThreatDirection
GetThreatSector
GetThreatConfidence
GetThreatUncertainty
```

Потребителю не нужно знать источник.

---

## 14C.1 Initial Spawn Estimate  ✅

При старте боя, **один раз**:

```text
OwnSpawnCenter
EnemySpawnCenter
Direction = Normalize(EnemySpawnCenter - OwnSpawnCenter)
```

Только существующие `CombatTestSpawnMarker` (Player / Enemy) или точки `UnitSceneSpawner`.  
**Не** создавать `ThreatDirectionPoint` / `EnemyDirectionMarker` / `TacticalHintObject`.

Не связывать P01→E01. Центр **группы** вражеских pin. Player и Enemy получают **противоположные** направления. Neutral — без Expected.

---

## 14C.2 Confidence / Uncertainty / Age  ✅

Expected: confidence > 0, uncertainty > 0.  
Visual: confidence ↑, uncertainty ↓.  
Stale: confidence ↓, uncertainty ↑.  
Коэффициенты не балансировать в этом срезе.

---

## 14C.3 Visual Override  ✅

Событие (rising edge, не каждый кадр Visible):

```text
HostileVisible → LastKnown → Expected/… → Known (Visual)
HostileLost    → Known → Stale   (направление то же)
```

LastKnown без экстраполяции, как в perception-контракте.

---

## 14C.4 Sound / Report fallback  ✅

Только если нет более сильного актуального источника.

```text
Visual
  ↓ fallback
Sound
  ↓ fallback
Report
  ↓ fallback
InitialEstimate
```

Gunshot = существующий combat sound cue. Report = Hostile identity + confidence > 0.

---

## 14C.5 Expiry / Decay  ✅

```text
Known → Stale → Expected   (если есть InitialEstimate)
Known → Stale → None       (если spawn-оценки не было)
```

Expected **не** истекает до конца тактической сессии.  
`Tick(now)` двигает Age / expiry. **Не** перечитывает позицию врага.

---

## 14C.6 Logs  ✅

Канал `THREAT_DIRECTION`. Только смена source / state / compass, не каждый кадр.

```text
source=Initial state=Expected dir=N confidence=...
source=Visual state=Known dir=NE confidence=...
source=Sound state=Known dir=E confidence=...
state=Stale dir=NE age=...
```

---

## 14C.1 Cover Orientation & Facing  ✅

Потребитель поверх замороженного #13. **Не** reopen CoverScore / PathScore / Reservation / Occupancy / 0.60.

```text
ExistingCoverScore
        +
ThreatDirectionAdjustment
        ↓
FinalCoverPreference
```

Ориентация укрытия в этом проекте: `CoverNormal` смотрит в сторону огня (стена сзади). Alignment = `dot(CoverNormal, ThreatDirection)`.

```text
хорошо закрывает угрозу  → bonus
боком                    → side
открыто на угрозу        → penalty
```

Коэффициенты прототип, не freeze.

Stay / Reposition и `RepositionRecommended` остаются на сыром `CoverScore`. Occupied + valid LOS → Stay Committed даже если направление N→NE.

Facing = центр сектора угрозы. Поворот только на событиях `ThreatDirectionChanged` / `CoverAcquired` / `ReadinessChanged`. Deadband **12°**. Не крутит Transform каждый кадр. Не меняет `ReadinessState`. Search остаётся на `SearchPosition`.

Логи (события): `COVER_DIRECTION`, `FACING_DIRECTION`.

Меню: `Tools/Tests/Run Threat Direction Cover (EditMode)` и `(Play)` → `ThreatDirectionCover_LAST.txt`. Оригинальный 14C.0–14C.6 набор не трогать.

Приёмка 14C.1 (29.08.2026 12:58). EditMode **40/0**. Play `ThreatDirectionCover_LAST.txt` **22/0** (12:58:02).

1. До первого контакта Expected влияет на предпочтение cover.
2. Visual заменяет Expected на следующей переоценке.
3. Facing без постоянного вращения (deadband 12°).
4. ThreatDirection не ломает Stay Committed и не reopen #13/#14.

---

## 14C.2 Confidence & Uncertainty  ✅

Качество знания поверх уже доказанных Direction / 14C.1. **Не** меняет механизм направления. **Не** reopen #13/#14.

```text
ThreatDirection
Confidence
Uncertainty
```

Visual > Sound > Report > InitialEstimate. Known → Stale → Expected/None. Expected не истекает; это fallback после expiry фактического знания.

Cover: `ThreatDirectionAdjustment * CoverInfluence(confidence)`. Низкая уверенность — слабое предпочтение. Stay Committed не ломается.

Facing: DesiredFacing = центр сектора. Slack растёт с Uncertainty / низкой Confidence — не крутиться в одну точку при широком конусе.

Лог `THREAT_DIRECTION_UPDATE` на смене source/state/compass и на квантованном quality (не каждый кадр). `THREAT_DIRECTION` 14C.6 без изменений.

Приёмка 14C.2 (29.08.2026 13:15). EditMode **38/0**. Play `ThreatDirectionQuality_LAST.txt` **19/0** (13:15:02).

1. Качество зависит от источника.
2. Старое знание постепенно слабее.
3. Cover и Facing учитывают Confidence / Uncertainty.
4. Нет дополнительных постоянных сканов.

Меню: `Tools/Tests/Run Threat Direction Quality (EditMode)` и `(Play)`.

---

## 14C.3 Tactical Positioning  ✅

Потребитель поверх 14C.1 / 14C.2. **Не** новая система укрытий. **Не** reopen CoverScore / PathScore / Reservation / Occupancy / 0.60. **Не** Move / Reserve / Release / ConfirmOccupied. **Не** новый scan.

```text
ExistingCoverScore
        +
DirectionScore          (CoverNormal ↔ ThreatDirection)
        +
FacingScore             (выход / CoverNormal ↔ ThreatDirection)
        ↓
TacticalPositionPreference
```

`DirectionScore` — уже существующий 14C.1 Adjustment (bonus / side / penalty).  
`FacingScore` — непрерывное удобство огневой оси.  
`FinalAdjustment = (DirectionScore + FacingScore) × CoverInfluence(confidence) × SectorOverlap` для бонуса; штраф за «спиной к угрозе» не обнуляется overlap.

Uncertainty: `Cover protected sector ∩ Threat cone`. Узкий сектор 5° не получает тот же бонус, что широкий фронт на широком Expected. Только итоговый `ThreatDirectionKnowledge`, без перебора врагов.

Stay / Reposition остаются на сыром `CoverScore`. Occupied + valid → Stay Committed. 14C.3 может сменить Best, **не** инициирует Reposition. Переоценка только при material change направления / invalid cover / новом reposition request. Лёгкий yaw внутри того же octant — cache.

Плохой CoverScore не становится лучшим из‑за хорошего направления: overlay ограничен, CoverScore остаётся базой.

Лог (события): `TACTICAL_POSITION` (`dirScore` / `facingScore` / `weight` / `overlap` / `adj`).

Меню: `Tools/Tests/Run Threat Direction Position (EditMode)` и `(Play)` → `ThreatDirectionPosition_LAST.txt`. Наборы 14C и 14C.1 / 14C.2 не складывать.

Приёмка 14C.3 (29.08.2026 13:41). EditMode **43/0**. Play `ThreatDirectionPosition_LAST.txt` **18/0** (13:41:42).

1. Direction влияет на preference.
2. Confidence масштабирует влияние.
3. Uncertainty учитывается секторно.
4. Current valid cover не сбрасывается.
5. Нет новых сканов.
6. Нет прямого Move.
7. #13/#14 нетронуты.

14C.4 Dynamic Threat Reorientation ✅.

---

## 14C.4 Dynamic Threat Reorientation  ✅

Потребитель поверх 14C.1–14C.3. **Не** Move / Release / Reserve / ConfirmOccupied. **Не** новый scan. **Не** Fire / Aim / смена AIState. **Не** reopen CoverScore / 0.60.

```text
Δangle < deadband          → ничего
Δangle ≥ 50° AND conf ≥ 0.4 → ThreatDirectionChanged
N → NE (45°)               → коррекция facing, не смена фронта
N → E / N → S              → смена фронта
```

Facing: существующий `ThreatDirectionFacingController` + slack 14C.2. Низкая confidence (< 0.4) не крутит уже зафиксированный facing. Поворот тела — `TurnToTargetTime × ArmFatigue` (14B.6), направление от fatigue не зависит.

Occupied cover не сбрасывается. `CoverThreatFit` Good/Poor (Good = alignment ≥ 0.5). Cover остаётся физически valid; 14C.5 решит, нужен ли Reposition.

События (не каждый кадр): `THREAT_DIRECTION_CHANGED`, `FACING_UPDATE`, `COVER_THREAT_FIT`.

Меню: `Tools/Tests/Run Threat Direction Reorientation (EditMode)` и `(Play)` → `ThreatDirectionReorientation_LAST.txt`. Наборы 14C / 14C.1–14C.3 не складывать.

Приёмка 14C.4 (29.08.2026 14:47). EditMode **36/0**. Play `ThreatDirectionReorientation_LAST.txt` **18/0** (14:47:14).

1. Мелкие изменения игнорируются.
2. Существенные изменения обновляют направление.
3. Юнит меняет DesiredFacing.
4. Fatigue замедляет поворот, не направление.
5. Occupied cover не сбрасывается.
6. Нет новых постоянных scan/raycast.

14C.5 Threat Direction → Reposition Decision ✅.

---

## 14C.5 Threat Direction → Reposition Decision  ✅

Потребитель поверх 14C.4. **Не** выбирает окончательный cover. **Не** Reserve / Release / ConfirmOccupied / Move. **Не** новый scan. **Не** Fire / Aim / Readiness / G6 / смена AIState. **Не** reopen CoverScore / 0.60.

```text
Δangle < 80° OR conf < 0.75  → FaceOnly
Fit Good                     → Stay
currentId == bestId          → Stay
Fit Poor AND best Fit Good   → RepositionAllowed
заметное преимущество        → RepositionAllowed
иначе                        → Stay
```

`ThreatRepositionAngleThreshold = 80°` (N→NE 45° — только поворот). `ThreatRepositionConfidenceThreshold = 0.75` (Visual 0.9 проходит, Expected 0.5 / слабый sound — нет). Margin = `0.45` по CoverScore / Preference / `PositionAdjustment`, **или** Fit Poor → кандидат Good. Occupied cover 14C.5 сам не снимает.

Live: `CoverSituation.ThreatRepositionAllowed` → `TacticalCoverSolver` Occupied Stay Committed уступает только при этом флаге. Дальше существующие #13 (кандидат) и #14 (ход). Без флага Occupied остаётся Committed, как в 14C.1–14C.4.

Событие (не каждый кадр): `THREAT_REPOSITION`.

Меню: `Tools/Tests/Run Threat Direction Reposition (EditMode)` и `(Play)` → `ThreatDirectionReposition_LAST.txt`. Наборы 14C / 14C.1–14C.4 не складывать.

Приёмка 14C.5 (29.08.2026 15:51). EditMode **39/0**. Play `ThreatDirectionReposition_LAST.txt` **18/0** (15:51:13).

1. Мелкое изменение → FaceOnly.
2. Большое + низкая confidence → FaceOnly.
3. Большое + высокая confidence + плохой fit + лучший кандидат Good → RepositionAllowed.
4. Текущий cover ещё хороший → Stay.
5. Occupied не снимается автоматически.
6. Нет новых постоянных scan/raycast. Нет прямого Move.

#14C по смыслу завершён (knowledge + потребители 14C.1–14C.5). **Не FROZEN**, пока не сказано. #15 не открывать.

---

## Что #14C НЕ делает

```text
❌ CoverScore / PathScore / 0.60 / Reservation формула
❌ 14C.5 сам не выбирает cover / не Reserve / не Release / не Move
❌ Aim / Fire / ReadinessState / G6
❌ новый scan / raycast
❌ prediction движения врага
❌ sharing знания на всех юнитов
❌ точная позиция неизвестного врага
❌ замена SearchPosition
```

14C.5 выдаёт `RepositionAllowed`. Дальше работают существующие #13 / #14. #13 не reopen.

---

## События обновления

```text
Spawn / BattleStart
HostileVisible
HostileLost
GunshotHeard
AllyReport
KnowledgeExpiry
```

Нет отдельного Update-loop поиска врага.

---

## Приёмка (весь #14C)

**Приёмка 14C.0–14C.6 (29.08.2026 11:56).** EditMode **49/0**. Play `ThreatDirection_LAST.txt` **20/0** (11:56:21).  
**Приёмка 14C.1 (29.08.2026 12:58).** EditMode **40/0**. Play `ThreatDirectionCover_LAST.txt` **22/0** (12:58:02).  
**Приёмка 14C.2 (29.08.2026 13:15).** EditMode **38/0**. Play `ThreatDirectionQuality_LAST.txt` **19/0** (13:15:02).  
**Приёмка 14C.3 (29.08.2026 13:41).** EditMode **43/0**. Play `ThreatDirectionPosition_LAST.txt` **18/0** (13:41:42).  
**Приёмка 14C.4 (29.08.2026 14:47).** EditMode **36/0**. Play `ThreatDirectionReorientation_LAST.txt` **18/0** (14:47:14).  
**Приёмка 14C.5 (29.08.2026 15:51).** EditMode **39/0**. Play `ThreatDirectionReposition_LAST.txt` **18/0** (15:51:13).

Инварианты:

1. До первого контакта юнит уже знает вероятное направление угрозы.
2. Знание из существующих spawn points.
3. Новые объекты на сцене не нужны.
4. Реальный контакт заменяет предположение.
5. Обновление по событиям, не каждый кадр.
6. Knowledge / 14C.1–14C.4 сами не Move. 14C.5 только разрешение; #13/#14 исполняют.

**14C.5 ✅.** #14C по смыслу завершён. **Не FROZEN.** #15 не открывать.

Меню knowledge: `Tools/Tests/Run Threat Direction (EditMode)` и `(Play)`.  
Меню 14C.1: `Tools/Tests/Run Threat Direction Cover (EditMode)` и `(Play)`.  
Меню 14C.2: `Tools/Tests/Run Threat Direction Quality (EditMode)` и `(Play)`.  
Меню 14C.3: `Tools/Tests/Run Threat Direction Position (EditMode)` и `(Play)`.  
Меню 14C.4: `Tools/Tests/Run Threat Direction Reorientation (EditMode)` и `(Play)`.  
Меню 14C.5: `Tools/Tests/Run Threat Direction Reposition (EditMode)` и `(Play)`.
