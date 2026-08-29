# Dynamic Cover

**Слой:** тактический **#13**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.6.  
**Статус:** **CLOSED / FROZEN 27.08.2026.** EditMode **169/0**. Play `CoverIntegration_LAST.txt` **PASS 18/0** (10:27). 13.0–13.8 закрыты. Cover ≠ Fire. Cover ≠ Move. Occupancy ≠ GeometryVersion. **#14 CLOSED / FROZEN.**

Укрытия **не расставляет дизайнер**. Нет `CoverPoint_001`. Солдат читает геометрию мира.

Не менять замороженное:

```text
Vision / Detection / Identity / Memory
RoE / CombatIntent
A10 recoil / weapon ballistic envelope
θ / Recoil / AimTime / WorkingRange / VisionRange
G5 Score / Target hysteresis (#12)
Command Priority (#11)
```

Cover **не** жмёт Fire. Cover не пишет AimPoint. Cover не сливает `AI.EngageTarget` и `Combat.SelectedTarget`.

**#13 отвечает «где мне выгодно находиться?»** Destination / RepositionRequest.  
**#14 ответит «как туда безопасно добраться?»** Path / cover-to-cover movement. В #13 pathing не строить.

```text
понять, что нужна позиция
        ↓
локальные геометрические возможности   ← shared cache
        ↓
пригодные укрытия
        ↓
оценка своей ситуации                 ← individual
        ↓
выбор позиции
        ↓
занятие
        ↓
работа из неё
```

10 юнитов **не** делают 10 раз один и тот же дорогой анализ стен.

---

## Shared ≠ Individual

### Общий пространственный слой (13.0)

> Какие здесь вообще существуют потенциальные тактические позиции?

Геометрия: walls, corners, obstacles, surfaces, height, NavMesh → `CoverCandidate`.

В кэше **нет** «лучшего укрытия». C3 для снайпера плохо, для LMG хорошо — это individual score.

### Индивидуальный слой (13.3)

> Какая из этих позиций выгодна именно мне сейчас?

weapon, target, mission, rank, current position, threat, exposure, formation.

Один shared список. Разные score / selection. Нормально: A→C4, B→C2, C→C4.

---

## 13.0 Shared Spatial Cover Cache  ✅ закрыт внутри открытого #13

### Регион

Локальная сетка, не вся сцена. `CoverSpatialMath.DefaultRegionSizeMeters = 16`.

```text
NeedCover → Region(world) → Cache
```

### Lazy generation

Первый запрос региона генерирует кандидатов и кладёт в кэш. Следующий юнит **reuse**.

### Dedup

A, B, C одновременно просят R5 → **одна** generation. In-flight запрос того же региона не стартует вторую.

### Поля кандидата (shared)

```text
CandidateId
Position
Normal
CoverType
StandingValid / CrouchValid / PartialValid
NavMeshValid
RegionId
GeometryVersion
Occupancy   (Available / Reserved / Occupied — groundwork, не group reservation)
```

Позже (не в 13.0/13.1): corner metadata, peek directions, surface. Score — #13.3.

### Не в shared cache

```text
"это лучшее укрытие"
individual score
selection / decision
```

### Invalidation

`GeometryVersion` в кэше и в мире. Несовпадение → rebuild.

v1: ручное / событийное invalidation (`BumpGeometryVersion`, `InvalidateRegion`). Не детектить каждое разрушение автоматически.

Jobs / async **не** решать заранее.

EditMode 13.0: A1–A6 PASS.

---

## 13.1 Candidate generation  ✅ закрыт внутри открытого #13

Кэш не переписывать. Stub `ICoverCandidateSource` заменён на:

```text
SharedCoverSpatialCache
        ↓
CoverCandidateGenerator
        ↓
ICoverGeometrySource  (сцена или mock)
        ↓
sample → NavMesh → clearance → dedup → spatial cap 16
        ↓
CoverCandidate[]
```

`CoverCandidate` на 13.1 значит **потенциальная тактическая позиция, связанная с геометрией**, не «это укрытие» и не «это хорошо для солдата».

### Геометрия

Только статичный мир: walls, buildings, obstacles, boxes / barriers.  
Не: vehicles, destruction, characters, squad, reservations, weapon, rank.

Запрос только региона 16 m + `GeometryMarginMeters` (default 1.5). Не весь мир.

### Sampling

Не 1 collider → 1 candidate. Позиции вдоль поверхности. Алгоритм sampling — **прототип**, не freeze; верхний API кэша не зависит от него.

Поля: Position, Normal (сторона геометрии), RegionId, GeometryVersion. `CoverType = None`. Score не добавлять.

### Фильтры

NavMesh sample → иначе reject.  
Body clearance (может ли юнит физически стоять) → иначе reject. Не crouch/standing.  
Dedup по `DedupRadiusMeters`.  
Cap 16 **пространственным разнообразием**, не tactical quality.

### Debug

`CoverCandidateDebugDraw`: ● candidate, → normal, □ region. Цвет cached / generated / rejected. Без score.

### Приёмка 13.1 (26.08.2026 15:42)

EditMode `[DynamicCover] finished` **43/0** (13.0 cache + 13.1 generation).  
Play `CoverGeneration_LAST.txt` **PASS 18/0**: miss → 16 candidates (samples=34, navReject=0, clearReject=0); hit без geometry query; 20 units / 3 regions → 3 generation.

### Меню

```text
── Current ──
Tools/Tests/Run Dynamic Cover (EditMode)    # 13.0–13.8
Tools/Tests/Run Dynamic Cover (Play)        # CoverIntegration_LAST.txt
```

---

## 13.2 Cover Classification  ✅ закрыт внутри открытого #13

Геометрический кандидат → тип укрытия. **Не** score, **не** конкретный враг, **не** lean.

```text
13.1 "что физически существует?"
        ↓
13.2 "каким типом геометрического укрытия это является?"
        ↓
13.3 "насколько это полезно конкретному солдату?"
```

Shared cache хранит потенциал относительно `Normal` (CoverBacked = огонь со стороны геометрии):

```text
StandingValid / CrouchValid / PartialValid / CornerValid
CoverType
StandingProfile / CrouchProfile  (Head, Torso, Pelvis, Legs: 0..1 occlusion, не урон)
```

Не хранить BestForSniper / ThreatScore / MissionScore.

`CoverType`: None, Crouch, Standing, Partial, Corner.  
Corner ≠ lean. Lean — 13.7.

Прототип защиты: sample rays (вариант B) на 4 точки тела. Пороги **не freeze**.

Golden: низкая стена → standing exposed, crouch protected → `Crouch`.

Классификация выполняется **один раз на candidate при generation**, не на юнита.

### Приёмка 13.2 (26.08.2026 19:50)

EditMode `[DynamicCover] finished` **60/0** (13.0 cache + 13.1 generation + 13.2 classification).  
Play `CoverClassification_LAST.txt` **PASS 12/0**: 16 classified (standing=5, crouch=4, corner=7, none=0); cache hit same type; 20 units / 3 regions → 3 classification batches; overlay by CoverType, no score.

Partial покрыт EditMode. В Play-арене в этом прогоне `partial=0` — не блокер: низкая/высокая стена дали Crouch/Standing/Corner.

---

## 13.3 Individual evaluation  ✅ закрыт внутри открытого #13

Shared cache знает, что позиция **существует** и какие у неё геометрические свойства.  
Индивидуальный AI решает, насколько она **полезна именно ему сейчас**.

```text
Shared Candidate
        ↓
My situation
        ↓
PositionEvaluation { candidate, score, valid, factors }
        ↓
BestCandidate
```

```text
Selected ≠ Move
Selected ≠ Fire
Selected ≠ Lean
```

Score **не** пишется в `CoverCandidate` / shared cache. 20 юнитов региона = 1 geometry generate + 20 cheap eval.

### Ситуация юнита

```text
UnitPosition, Stance (Standing/Crouch), Target?, Mission, WeaponClass, RankClass, HostileDirection
```

Stance явный. Автовыбор Standing/Crouch/Lean/PointAim — не этот срез.

### Факторы (каждый своим методом)

Первое внедрение: **Protection + Visibility − TravelCost** несут вес. Остальные — тонкий baseline / интерфейс, не doctrine.

```text
PositionScore =
    Protection + Visibility + FireLane
  + MissionRelevance + WeaponSuitability + EscapeOptions
  - Exposure - TravelCost - Danger
```

`CoverScoreMath.ProtectionScore` / `VisibilityScore` / `TravelCost` / `FireLaneScore` / `MissionScore` / `WeaponScore` / …  
`PositionScore` только складывает. Весы **не freeze**.

Weapon = `CoverWeaponClass` (Rifle / Sniper / Lmg), не #15 doctrine.  
Rank = маленький modifier, не #15B.  
EscapeOptions в срезе = 0.  
Danger минимальный (open / hostile direction), не suppression field.

Visibility / FireLane читают LOS. Это **не** Fire.

### Current + SwitchingCost

Всегда оценивается текущая позиция.  
`CoverSwitchMath.ShouldReposition(current, best, cost)` — интерфейс, веса не калибровать.  
BestCandidate можно знать и **не** идти: `RepositionRecommended = false`.

### Individual cache

Ключ: region + GeometryVersion + candidate set + quantized unit/target + stance/mission/weapon/rank + hostile.  
Reuse, если ключ тот же. Invalidate: target / mission / weapon / geometry / существенный сдвиг позиции.

Не каждый кадр.

### Не в 13.3

Move, auto-stance, Lean, Fire, suppression, flank, group, weapon doctrine, rank behaviour.

### Приёмка 13.3 (26.08.2026 21:27)

EditMode `[DynamicCover] finished` **80/0** (13.0–13.2 + evaluation).  
Play `CoverEvaluation_LAST.txt` **PASS 15/0**: rifle **C4** (не C1); sniper C3>C2; LMG C2>C1; Recruit/Veteran тот же set; 20 units / 3 regions → 3 geometry + 20 scores; overlay scores; **не Move**. Score не на shared candidate.

---

## 13.4 Emergency Cover  ✅ закрыт внутри открытого #13

**Overlay, не state.** ImmediateThreat уже Emergency / HoldState в #11: Attack / Defense / Idle / Search остаются. EmergencyCover overlay разрешён и во время Search. 13.4 не добавляет `UnitAIState`.

Отвечает **куда спрятаться**, не как дойти. Selected ≠ Move. `#14` позже читает destination. 13.4 **не** вызывает `IUnitMoveCommand` и **не** пишет `UnitAIStateContext.Destination` (Attack/Defense уже Walk туда).

```text
ImmediateThreat
  → #11 HoldState (включая Search)
  → текущее укрытие достаточно? Stay (без generate)
  → иначе SharedCoverSpatialCache → emergency score → acceptable/closest иначе fallback
  → EmergencyCoverDestination
```

### Overlay vs state

| State + ImmediateThreat | 13.4 |
|---|---|
| Attack | остаётся Attack, overlay |
| Defense | остаётся Defense, overlay |
| Idle | остаётся Idle, overlay |
| Search | сначала #11 ReturnState, **потом** overlay на restored mission |
| Retreat / Flee | **нет overlay** (уже уходят; не спорить с их Destination) |

Поля overlay (не команда, не AI state):

```text
EmergencyCoverActive
EmergencyCoverDestination
SelectedCandidateId
Result = Stay | Selected | Fallback | None
Reason = ImmediateThreat | CurrentCoverSufficient | NoAcceptableCandidate | NoCandidates
```

### Триггер (первый проход)

Только **ImmediateThreat** (окно `ImmediateThreatSource`).

Не в этом срезе: Wound, Suppression, Explosion, «cover became invalid» как отдельное событие мира.

Внутри окна:

1. Текущая позиция достаточно защищена → **Stay**, новый region generate **не** стартует.
2. Иначе один query shared cache → score → pick → кэш решения.
3. Threat ещё активен и geometry / position / version не изменились → **reuse**.
4. Threat истёк → overlay inactive (последний dest может остаться для debug; не Move).

Повторные попадания, пока уже protected, **не** должны поднимать `SharedCoverSpatialCache.GenerationCount`.

### Emergency score ≠ 13.3 tactical score

Не использовать `CoverScoreMath.PositionScore` как emergency total.

Профиль `CoverEmergencyScoreMath` (prototype, не freeze):

- Primary: Protection, TravelCost / time-to-cover, Danger, reachable (`NavMeshValid`)
- Secondary (thin): Visibility, FireLane, Mission, Weapon
- Rank/weapon могут nudge через существующий `CoverSituation`. Нет #15 doctrine.

**Acceptable threshold** `EmergencyAcceptableThreshold` (prototype): среди `score >= threshold` выбрать **наименьший travel** (близкое приемлемое бьёт далёкое отличное). Нет приемлемых, но кандидаты есть → **best fallback** (наивысший emergency score). Нет кандидатов → Result=None, не изобретать точку.

`CoverType.None` никогда не acceptable. Fallback может взять наименее плохой **valid** кандидат; никогда не invent point.

Current-cover Stay: тот же protection idea, что occupying / stance profile в 13.3. Если текущий emergency score уже ≥ threshold → Stay.

### Wiring

Геометрия как в 13.3: тесты/Play инжектят `SharedCoverSpatialCache`. Scene-wide singleton не требуется.

Хук из `TryAutonomousTransitions` **после** `ApplyImmediateThreatPriority`. ImmediateThreat Search больше не complete. #11 владеет state.

Solver достаточно чистый для EditMode: `ImmediateThreat + CoverSituation + IReadOnlyList<CoverCandidate> → EmergencyCoverDecision`.

### Debug / лог

`UnitActionLog.EmergencyCover = "EMERGENCY_COVER"` — не каждый кадр: только decision generated / Stay / selected changed / invalidated.

`CoverCandidateDebugDraw`: `Emergency Cover Active`, Stay vs Selected, rejected=below_threshold.

### Не в 13.4

Path, street movement, cover-to-cover, Lean, CQB, weapon/rank doctrine, squad reservation, suppression, Retreat/Flee как cover fallback.

### Приёмка 13.4 (26.08.2026 21:53)

EditMode `[DynamicCover] finished` **99/0** (13.0–13.3 + emergency A–F).  
Play `CoverEmergency_LAST.txt` **PASS 20/0**: open ground destination, **не Walk**; Stay + no second generate; far wins vs close-poor; Recruit/Veteran/LMG/Sniper независимые scores на shared list; 20 units / 3 regions → 3 geometry; overlay Emergency Cover Active. Attack/Defense Destination не переписывается.  
Регрессия #7–#11: EditMode `[FrozenLayers] finished` **62/0**; Play `FrozenLayersPlay_LAST.txt` **114/0** (#7 18/0, #8 36/0, #9 20/0, #10 22/0, #11 18/0).

---

## 13.5 Tactical Cover / Position Switching  ✅ закрыт внутри открытого #13

**Не менять закрытые 13.0–13.4.** Occupancy не часть решения (13.6). Lean не здесь (13.7). Path не здесь (#14).

13.4 научил **спасаться**. 13.5 учит **осознанно Stay / Reposition**, когда непосредственной аварии нет.

```text
Текущая позиция
      ↓
осталась ли она хорошей?
      ↓
есть ли рядом более выгодная?
      ↓
насколько она лучше?
      ↓
оправдан ли риск смены?
      ↓
STAY / REPOSITION
```

> Солдат не меняет позицию просто потому, что нашёл другую. Меняет только когда выигрыш оправдывает стоимость и риск перемещения.

Результат — **не** `Move()`:

```text
PositionDecision
{
  decision = Stay | Reposition
  current = C1
  selected = C7
  reason = BetterTacticalPosition | ImprovementTooSmall | CurrentInvalid | ...
}
```

#14 потом читает selected / destination.

### Emergency ≠ Tactical

| | Emergency (13.4) | Tactical (13.5) |
|---|---|---|
| Когда | ImmediateThreat | относительно безопасен |
| Цель | быстро снизить опасность | выгодная долговременная позиция |
| Score | `CoverEmergencyScoreMath` | 13.3 `PositionScore` + SwitchingCost |
| Близкое плохое vs далёкое отличное | близкое может победить | далёкое может победить |

Одна пара точек — два разных решения. Не смешивать профили.

### CurrentTacticalPosition

Снимок, не вечный score:

```text
CandidateId  Position  CoverType  GeometryVersion
valid  occupied
```

Score **пересчитываемый**. Occupied здесь = «я стою тут», не squad reservation.

### Stay / Switch

```text
new > current + SwitchingCost  → Reposition
иначе Stay
```

`CoverSwitchMath.ShouldReposition` — существующий gate. Первый проход SwitchingCost: **distance + exposure**. Later: time, loss of cover/LOS, mission interruption, danger.

Без cost: 7 → 7.2 → 7.4 → бесконечный поиск идеала. С cost=1: 7 vs 7.2 → Stay; 7 vs 9 → Switch.

Tie-break deterministic: score → distance → stability → CandidateId. Одинаковые входы → одинаковое решение.

### Invalid vs Degraded

**Invalid** (обязан искать новую): wall removed, NavMesh changed, point inaccessible, GeometryVersion / cover lost.

**Degraded** (менять только если новый вариант существенно лучше): Cover still exists, но TacticalValue упал (враг ушёл из сектора, новый enemy direction, LOS/exposure). Это важнее, чем только «укрытие уничтожено».

### Re-evaluation только по событию

Не каждый тик, не query cover каждый Update.

Trigger: Target changed, enemy pressure, Mission, GeometryVersion, current cover degraded, LOS materially changed, Weapon changed.

Нет события → нет recomputation (EditMode: 100 ticks без event → 0 eval).

После Reposition — **position commitment**: minor improvements ignored, пока действует; major tactical change overrides. Конкретный hold-time / N evaluations — по тестам, не freeze формулы.

### Mission

Лучший cover ≠ лучшая tactical position, если из него нельзя выполнить задачу. 13.3 `MissionRelevance` уже есть; 13.5 обязана его учитывать.

Defense: контроль сектора зоны может бить «идеал за периметром».  
Attack: ближе к цели может бить чуть более безопасное «назад».  
Веса не freeze. WeaponSuitability — вход для #15, без sniper/LMG doctrine здесь.

Occupancy: два юнита могут выбрать один кандидат. Availability поле можно подготовить, решение «кто получает C07» — #16.

### Лог / overlay

`POSITION_DECISION` (не каждый кадр; только decision generated / changed):

```text
POSITION_DECISION current=C03 best=C07 currentScore=7.8 bestScore=9.1 switchingCost=1.4 decision=REPOSITION reason=BetterPosition
POSITION_DECISION current=C03 best=C07 currentScore=7.8 bestScore=8.2 switchingCost=1.4 decision=STAY reason=ImprovementTooSmall
```

Debug: CURRENT / BEST / SWITCH COST / RESULT. Current = solid marker, Best = highlighted.

### EditMode ✅

| Id | Контракт |
|----|----------|
| A | current=8, cand=8.1, cost=1 → Stay |
| B | current=8, cand=10, cost=1 → Switch |
| C | equal scores → Stay |
| D | 8 vs 8.2 stay; repeat stay; 9.5 switch |
| E | current invalid → must select new |
| F | GeometryVersion change → invalidate |
| G | same set, different mission → different scores |
| H | significant target change → reevaluate |
| I | 100 ticks, no event → no recomputation |
| J | same state 100× → same candidate |

### Play ✅

1. Good enough current + чуть лучший рядом → Stay.  
2. Significantly better → RepositionRequest, **не Walk**.  
3. Cover intact, LOS/value changed (враг сместился) → reevaluate.  
4. Geometry invalid → обязательный reposition request.  
5. Attack: safe backwards vs slightly worse but advances mission → mission-aware.

Performance slice: 20 units / 3 regions → 3 geometry; 100 ticks без event → нет recomputation.

### Не в 13.5

Move, path, street, cover-to-cover locomotion, Lean, occupancy as assignment, squad, weapon/rank doctrine, Emergency score как tactical total.

### Приёмка 13.5 (26.08.2026 22:30)

EditMode `[DynamicCover] finished` **109/0** (13.0–13.4 + tactical A–J).  
Play `CoverTactical_LAST.txt` **PASS 22/0**: Stay vs Switch с cost=1; RepositionRequest **не Walk**; target change → reeval; invalid / GeometryVersion → обязательный reposition; Attack vs Defense разные scores; 20 units / 3 regions → 3 geometry; overlay CURRENT / BEST / SWITCH COST / RESULT; 100 ticks без event → нет recomputation.

---

## 13.6 Occupancy / Reservation groundwork  ✅ закрыт внутри открытого #13

**Не менять закрытые 13.0–13.5.** Это **не** Group AI, не формации, не ранговый приоритет, не #14 Move, не Lean (13.7).

Задача узкая: понять, занята или временно зарезервирована ли конкретная тактическая позиция, и не дать двум юнитам одновременно считать одну точку свободной.

```text
Shared Candidate
      ↓
Occupancy / Reservation   ← runtime layer, не geometry
      ↓
Available / Reserved / Occupied
      ↓
Individual Evaluation (unavailable → exclude, score не обнулять)
```

Occupancy **не** определяет, какая позиция лучше. Великолепная точка может быть недоступна.

### Состояния

`Available` — свободна.  
`Reserved` — юнит собирается занять (`TryReserve`, атомарно).  
`Occupied` — юнит фактически в позиции (`ConfirmOccupied`; #14 потом вызовет по прибытии).

Reservation: `CandidateId + RegionId + UnitId + CreatedAt + ExpiresAt + Version`. Без SquadId / Role / Formation.

`Explicit Release` + `ReservationTTL`. Смерть, новый приказ (#11), GeometryVersion → release. Occupied не истекает по TTL.

Ключ слота = `(RegionId, CandidateId)`: R1/C1 и R2/C1 независимы.

`OccupancyVersion` отделён от `GeometryVersion`. Приход солдата не инвалидирует spatial cache.

`OccupancyRadius` — параметр, не freeze. Candidate reservation ≠ physical proximity.

Emergency: занятая точка → следующая (C08, C09), не «нет укрытия».

### API

`TryReserve` / `Release` / `ConfirmOccupied` / `ReleaseOccupied` / `IsAvailable` / `GetReservation` / `ReleaseUnit`.

Лог: `POSITION_RESERVATION`. Overlay: `C01 AVAILABLE` / `C02 RESERVED … TTL`.

### EditMode ✅

| Id | Контракт |
|----|----------|
| A | Available → Reserved → Occupied → Available |
| B | второй юнит на тот же слот → fail |
| C | тот же юнит повторно → idempotent, без duplicate |
| D | Release → другой может занять |
| E | TTL истекает → Available |
| F | смерть → release |
| G | новый приказ (#11) → release |
| H | GeometryVersion → release **без** geometry query |
| I | разные кандидаты → оба succeed |
| J | тот же CandidateId, разные регионы → независимы |
| K | 100×16 ≤16 occupied; 100 simultaneous → 1 winner |
| L | unavailable исключён из выбора, score не 0 |
| M | emergency пропускает reserved, берёт альтернативу |
| N | occupancy не regenerate geometry |

### Play ✅

1. Два солдата / одно укрытие — первый Reserved, второй C2. Score не 0.  
2. Release → второй может взять C1.  
3. Смерть → Available.  
4. Emergency пропускает reserved.  
5. 20 units / 3 regions → 3 geometry; OccupancyVersion ≠ GeometryVersion.  
6. 100 simultaneous TryReserve → 1 winner.  
7. Overlay AVAILABLE / RESERVED; Idle **не Walk**.  
8. ConfirmOccupied API, не Move.

### Не в 13.6

Group, squad, formation, rank priority, commander, path, lean, CQB, weapon doctrine, «MG/sniper position».

### Приёмка 13.6 (26.08.2026 22:54)

EditMode `[DynamicCover] finished` **127/0** (13.0–13.5 + occupancy A–N).  
Play `CoverOccupancy_LAST.txt` **PASS 19/0**: два юнита / один слот; release; death; emergency skip; 20/3 → 3 geometry; OccupancyVersion отдельно; ConfirmOccupied **не Walk**. Overlay AVAILABLE / RESERVED.

---

## 13.7 Lean / Peek Integration  ✅ закрыт внутри открытого #13

**Не менять закрытые 13.0–13.6.** Lean — локальный инструмент из уже выбранной позиции. Corner / Partial дают **возможность**, не `if Corner then Lean()`.

```text
Current position
  → Corner / Partial
  → LOS without lean
  → if already good → No Lean
  → else Left/Right × Small/Medium/Deep
  → minimum depth that works
  → UnitSpineLean.SetLeanLevel
```

Тактический слой решает **нужен ли** lean. Существующий `UnitSpineLean` знает **как**. Второго LeanController нет.

`CoverPeekSolver`: gain vs risk (прототип, не freeze). `CoverPeekOverlay`: event-driven (позиция / цель / LOS / cover), `CommittedUntil`. Return: цель пропала, команда, позиция, Search/Retreat/Flee.

Глубина: `Small/Medium/Deep` → spine levels **1/2/3**. Ранг **не** режет физический lean. Оружие — extension point (#15), не доктрина.

Moving lean: `CoverMovementLeanContract` (Normal / Leaning + direction/depth). **Когда** идти в lean — #14.

CoverPeek **не** вызывает Fire и не меняет Combat Executor.

Лог: `PEEK`, `LEAN` (`result=Return reason=TargetLost`). Overlay: No Lean / Left Small… / Selected.

### EditMode

A Corner opportunity / straight wall / partial  
B left / right / only / neither  
C visible without lean → no lean; hidden → candidate  
D reveal → lean; reveal nothing → no lean  
E small / medium / deep min sufficient  
F request → `UnitSpineLean`, нет `LeanController`  
G target gone → return  
H/I Fire call count = 0  
J same geometry → same decision  
K 20 units / 1 generation / 20 evals  

### Play

`CoverPeek_LAST.txt`. S1 No Lean; S2 Corner lean; S3 Small not Deep; S4 Deep; S5 Right; S6 Return; S7 existing spine + no Fire; S8 20/1/20; event-driven cache; moving-lean contract.

### Не в 13.7

CQB, slice-the-pie, room clearing, formation, #14 path, weapon doctrine, Combat Executor.

### Приёмка 13.7 (27.08.2026 09:51)

EditMode `[DynamicCover] finished` **151/0** (13.0–13.6 + peek A–K, event-driven, moving-lean contract).  
Play `CoverPeek_LAST.txt` **PASS 18/0**: No Lean если цель видна; Corner → Lean; Small не Deep; Deep когда нужно; Right если Left бесполезен; Return при потере цели; `UnitSpineLean` без второго controller; Fire call count = 0; 20 units / 1 generation / 20 evals; event-driven cache; moving-lean contract без политики #14.

---

## 13.8 #13 Final Integration & Acceptance  ✅ CLOSED / FROZEN 27.08.2026

**Не менять закрытые 13.0–13.7.** Не добавлять новую механику. Задача: доказать, что Dynamic Cover — одна система, а не семь отдельных прогонов.

```text
Geometry → Cache → Generate → Classify → Evaluate
  → Emergency / Tactical → Reserve → Occupy (harness)
  → Peek / Lean (UnitSpineLean)
```

Границы: Cover ≠ Move. Lean ≠ Fire. Occupancy ≠ GeometryVersion. Shared ≠ individual score. Emergency ≠ Tactical profile.

Golden: open + no query → ImmediateThreat → Emergency C07 → Reserved → harness Occupy → threat ends → C11 substantially better → Reposition → Corner Peek → existing lean.

EditMode: G1 reuse 20/3→3; G2 individual scores; G3 emergency≠tactical; G4 Stay; G5 Reposition; G6 one reservation; G7 lean executor; G8 invalid; golden chain; 100/10 → 10 generations; concurrent 20 units / 16 region slots → unique reservations, no double-book; Retreat (#11) releases; type matrix.

Play: `CoverIntegration_LAST.txt`. Overlay: region / candidate / current / selected / reserved / occupied / lean.

### Приёмка 13.8 (27.08.2026 10:27)

EditMode `[DynamicCover] finished` **169/0** (13.0–13.8, включая concurrent 20 units / 16 slots).  
Play `CoverIntegration_LAST.txt` **PASS 18/0**: golden quiet→threat→C07 reserve→occupy→C11 reposition→lean; no Walk; occupancy ≠ geometry; G1 20/3→3; G6 one winner; Retreat (#11) releases; overlay; `UnitSpineLean` без второго controller.

#13 **CLOSED / FROZEN.** #14 **CLOSED / FROZEN.** Cover-меню остаются regression этого слоя (не смешивать с FrozenLayers #7–#11). Archive #12 остаётся archive.

---

## Лог

Каналы (тот же `UnitActionLog`, без консоли):

```text
COVER_QUERY   region=R5_2 reason=UnderFire
COVER_CACHE   region=R5_2 generated=1 candidates=8
COVER_CACHE   region=R5_2 reuse=1
COVER_CANDIDATE  ...
POSITION_SCORE  unit=... candidate=C7 score=8.21 protection=3.0 ... selected=1
POSITION_SELECT  ...
POSITION_SWITCH  ...
EMERGENCY_COVER  result=Selected reason=ImmediateThreat candidate=C3 dest=(...) active=1
POSITION_DECISION current=C03 best=C07 currentScore=7.8 bestScore=9.1 switchingCost=1.4 decision=REPOSITION reason=BetterPosition
POSITION_RESERVATION unit=... candidate=C02 region=R5_2 state=Reserved ttl=8
PEEK  candidate=C07 direction=Left available=1 visibilityGain=... risk=... decision=Lean
LEAN  direction=Left depth=Small reason=TargetAccess
LEAN  result=Return reason=TargetLost
```

POSITION_SCORE только при evaluation generated / selected changed / invalidated. Не каждый кадр.

SNAP (когда будет unit cover component):

```text
cover=Partial region=R5_2 candidate=C3 score=7.4 distance=4.8 exposure=0.22
cover=none coverQuery=active candidates=6
```

---

## Тесты

```text
── Frozen #13 ──
Tools/Tests/Run Dynamic Cover (EditMode)     # 13.0–13.8
Tools/Tests/Run Dynamic Cover (Play)         # CoverIntegration_LAST.txt

── Frozen #7–#11 ──
Tools/Tests/Run Regression (Play)            #7–#11
Tools/Tests/Run Regression (EditMode)        #10+#11
Tools/Tests/Archive/Regression/              #12 + Cover Generation / Classification / Evaluation / Emergency / Tactical / Occupancy / Peek Play
```

### 13.0 EditMode ✅

| Id | Контракт |
|----|----------|
| A1 | first request generates cache |
| A2 | second request reuses cache |
| A3 | in-flight / simultaneous same region → one generation |
| A4 | different regions generate independently |
| A5 | invalidated region regenerates |
| A6 | valid region does not regenerate |

### 13.1 EditMode ✅  (в общем прогоне 43/0 вместе с 13.0)

A geometry source (region-only query, empty, multiple surfaces)  
B generation (position, normal, region, GeometryVersion, CoverType=None)  
C NavMesh accept/reject  
D clearance accept/reject  
E dedup identical / near / distinct  
F cap ≤16, >16 → 16, deterministic  
G 30 → ≤16 spatially spread  
Cache: miss → generate; hit → no geometry query; in-flight one generate; invalidate → version 2  
20 units / 3 regions → 3 generations

### 13.1 Play ✅

`CoverGeneration_LAST.txt` **18/0** (15:42). Стены + obstacle → 16 candidates, cache reuse, 3 generation на 3 региона.

### 13.2 EditMode ✅

A Standing / Crouch / both / Partial / Corner / None  
B standing exposed + crouch protected на одной низкой стене  
C protection profile: torso / head / partial / разные геометрии  
D CoverBacked vs OpenSide → разный тип  
E same geometry → same classification; 20 units / 3 regions → 3 generation (= 3 classification batches)

### 13.2 Play ✅

`CoverClassification_LAST.txt` **12/0** (19:50). Low + high wall. Overlay by CoverType. Classification once per generate.

### 13.3 EditMode ✅

A same candidate → different unit scores; deterministic; factors sum  
B better protection / stance  
C visible vs blocked target  
D nearer wins when equal; far can win if substantially better  
E current good → no forced move; substantially better → candidate wins (без Walk)  
F weapon interface baseline  
G mission changes score  
H eval cache reuse / invalidate (target, geometry, mission)  
I 20 units / 3 regions → 3 geometry generations + 20 independent scores

### 13.3 Play ✅

`CoverEvaluation_LAST.txt` **15/0** (21:27). Rifle C4; sniper C3>C2; LMG C2>C1; shared geometry + independent scores. Overlay `C4 *`. Не Move.

### 13.4 EditMode ✅

A ImmediateThreat стартует eval; нет threat → нет query; повтор threat пока protected → нет второго generate  
B current protected → Stay; insufficient → search  
C close acceptable бьёт far excellent; ближе, но below threshold — reject  
D acceptable → Selected; нет acceptable → fallback; нет кандидатов → нет destination  
E Attack/Defense/Idle state не меняется; Search+threat ReturnState; Retreat без overlay; не пишет Attack Destination  
F 20 units / 3 regions / incoming fire → 3 geometry generations

### 13.4 Play ✅

`CoverEmergency_LAST.txt` **20/0** (21:53). Open ground dest, не Walk; Stay; far vs close-poor; independent weapon/rank; 3 geometry / 3 regions. Overlay `Emergency Cover Active`.  
Регрессия #7–#11: EditMode **62/0**, Play **114/0**.

### 13.5 Play / EditMode ✅

EditMode **109/0**. Play `CoverTactical_LAST.txt` **22/0** (22:30). Stay vs Switch с SwitchingCost; hysteresis; invalid; mission-aware; event-only reevaluation; decision ≠ Move. 20 units / 3 regions → 3 geometry. Overlay CURRENT / BEST / SWITCH COST / RESULT.

### 13.6 Play / EditMode ✅

EditMode **127/0**. Play `CoverOccupancy_LAST.txt` **19/0** (22:54). `CoverOccupancyBoard`: Available / Reserved / Occupied. TryReserve атомарный. Score не обнуляется. Emergency ищет альтернативу. OccupancyVersion ≠ GeometryVersion. ConfirmOccupied без Move.

### 13.7 Play / EditMode ✅

EditMode **151/0**. Play `CoverPeek_LAST.txt` **18/0** (09:51). Peek opportunity ≠ обязательный lean. Минимальная достаточная глубина. Существующий `UnitSpineLean`. Return. Не Fire. 20 units / 1 shared generation. Moving-lean API без #14 policy.

### 13.8 Play / EditMode ✅

EditMode **169/0**. Play `CoverIntegration_LAST.txt` **18/0** (10:27). Golden chain. Shared reuse. Emergency ≠ tactical. Unique reservations (20 units / 16 slots). Cover ≠ Move. Lean ≠ Fire. Occupancy ≠ GeometryVersion.

### Обычный Play / арена (не cover-меню)

Editor bake пишет кандидатов в `TacticalWorld` на `CombatTestArena_150x50`. Play читает bake и крутит occupancy (Available / Reserved / Occupied). Спавнер не создаёт cache. Overlay по-прежнему не Move и не пишет Attack Destination; `ResolvePointMovementHop` может взять RepositionRequest как walk goal. Чеклист: `Пехота_зрение_бой_AI.md` (28.08.2026). Актуальный Play: **§8.11** (`Infantry_20260828_204049`). Формулы #13 не менялись.

---

## Не в #13

CQB, Formation, Squad reservation, Flanking, Suppression, full weapon tactical profiles (#15), full rank behaviour (#15B), urban tactical routing (#14), Prone, Vehicle cover, Advanced destruction, Jobs «потому что надо».

Cover не начинает Fire.

---

## CLOSED / FROZEN 27.08.2026

```text
динамический поиск без CoverPoints
геометрия переиспользуется
несколько юнитов региона ≠ несколько дорогих query
кэш invalidates
кандидаты ограничены (16 / region)
crouch / standing / partial / corner различаются
индивидуальная оценка
одинаковое укрытие разное для разных юнитов
Emergency Cover
Tactical Cover
SwitchingCost стабилизирует Stay / Reposition
invalid ≠ degraded
Lean подключён, не переписан
Cover ≠ Fire
Cover ≠ Move
Occupancy ≠ GeometryVersion
command interruption освобождает reservation
multi-unit: 100/10 → 10 generations
PlayMode CoverIntegration 18/0
EditMode DynamicCover 169/0
```
